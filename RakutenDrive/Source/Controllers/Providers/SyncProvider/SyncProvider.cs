using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Windows.Security.Cryptography;
using Windows.Storage;
using Windows.Storage.Provider;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using RakutenDrive.Controllers.CloudFilterAPIControllers;
using RakutenDrive.Models;
using RakutenDrive.Resources;
using RakutenDrive.Services;
using RakutenDrive.Source.Services;
using RakutenDrive.Utils;
using RakutenDrive.Utils.CredentialManagement;
using Vanara.PInvoke;
using FileAttributes = System.IO.FileAttributes;


namespace RakutenDrive.Controllers.Providers.SyncProvider;

/// <summary>
///     Provides functionalities to manage file synchronization operations, handling local and server-side operations,
///     and maintaining synchronization consistency with the connected storage service.
/// </summary>
[SupportedOSPlatform("windows")]
[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
public sealed partial class SyncProvider
{
	// ----------------------------------------------------------------------------------------
	// INIT
	// ----------------------------------------------------------------------------------------
	#region INIT

	/// <summary>
	///     Represents a provider for synchronizing data between a local machine and a remote server.
	/// </summary>
	/// <remarks>
	///     This class initializes and manages the synchronization context, settings, and operations required
	///     to keep local and remote data in sync. It handles event subscriptions, processing workflows,
	///     and timers that monitor both local and remote changes.
	///     The <see cref="SyncProvider" /> class is intended to simplify data synchronization tasks
	///     by abstracting the underlying operations such as file change detection, data fetching,
	///     and failure handling in a structured manner.
	/// </remarks>
	public SyncProvider(SyncProviderParameters parameter)
	{
		_syncContext = new SyncContext
		{
			LocalRootFolder = parameter.LocalDataPath,
			LocalRootFolderNormalized = parameter.LocalDataPath.Remove(0, 2),
			ServerProvider = parameter.ServerProvider,
			SyncProviderParameter = parameter,
			SyncProvider = this
		};

		_syncContext.ServerProvider.SyncContext = _syncContext;
		_optionalChunkSize = _optionalChunkSizeFaktor * _chunkSize;
		_fetchDataRunningQueue = new ConcurrentDictionary<Guid, DataActions>();

		_syncContext.ServerProvider.ServerProviderStateChanged += OnServerProviderStateChanged;
		_syncContext.ServerProvider.FileChanged += OnServerProviderFileChanged;

		_syncActionBlock = new ActionBlock<SyncDataParam>(SyncAction, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 8, CancellationToken = _globalShutDownToken });

		_changedLocalDataQueue = new ActionBlock<ProcessChangedDataArgs>(ChangedLocalDataAction, new ExecutionDataflowBlockOptions
		{
			/* Ensure sequential processing */
			MaxDegreeOfParallelism = 1, CancellationToken = _globalShutDownToken
		});
	}

	#endregion

	// ----------------------------------------------------------------------------------------
	// PROPERTIES
	// ----------------------------------------------------------------------------------------
	#region PROPERTIES

	public delegate void ShowNotification(string title, string message);


	//public event EventHandler<int>? QueuedItemsCountChanged;
	//public event EventHandler<int> FailedDataQueueChanged;

	//public readonly ConcurrentDictionary<LocalChangedData, FailedData> FailedDataQueue = new();
	public ShowNotification? OnShowNotification { get; set; }

	private readonly ActionBlock<ProcessChangedDataArgs> _changedLocalDataQueue;
	private readonly ConcurrentDictionary<string, string> _downloadURLs = new();
	private readonly ConcurrentDictionary<string, CancellationTokenSource> _fetchCancellations = new();

	/* 2MB chunkSize for File Download / Upload */
	private readonly int _chunkSize = 1024 * 1024 * 2;

	/* Files which are not synced. (RegEx) */
	private readonly string[] _fileExclusions =
	[
		@".*\\Thumbs\.db",
		@".*\\Desktop\.ini",
		@".*\.tmp",
		@".*Recycle\.Bin.*",
		@".*\~.*"
	];

	private readonly FileService _fileService = new();
	private readonly ConcurrentSet<string> _filesToRestore = new();
	private readonly CancellationTokenSource _globalShutDownTokenSource = new();

	//private readonly LogService _logService = new();

	/* optionalChunkSize = chunkSize * optionalChunkSizeFaktor */
	private readonly int _optionalChunkSize;

	/* If optional Offset is supplied, Prefetch x times of chunkSize */
	private readonly int _optionalChunkSizeFaktor = 2;

	/* Buffer size for P/Invoke Call to CFExecute max 1 MB */
	//private readonly int _stackSize = 1024 * 512;

	private readonly ActionBlock<SyncDataParam> _syncActionBlock;
	private readonly SyncContext _syncContext;
	private bool _isConnected;
	private DateTimeOffset _logTimeOffset = DateTimeOffset.UtcNow;
	private string? _syncRootID;
	private CancellationToken _globalShutDownToken => _globalShutDownTokenSource.Token;
	private bool _disposedValue;
	//private bool _syncInProgress;
	private FileOperations? _fileOperations;

	/* Prevent overlapping sync runs (0 or 1 at a time) */
	private readonly SemaphoreSlim _syncGate = new(1, 1);

	/* Track the long-running API log loop so we can await it at shutdown. */
	private Task? _apiLogLoopTask;

	/* CFAPI callback gating - block new callbacks during shutdown and track in-flight ones. */
	private volatile bool _acceptNewCallbacks = true;
	private int _inflightCallbackCount;

	#endregion

	// --------------------------------------------------------------------------------------------
	// ACTIONS
	// --------------------------------------------------------------------------------------------
	#region ACTIONS

	/// <summary>
	///     Performs the synchronization action on the provided data parameter.
	/// </summary>
	/// <param name="data">
	///     The data parameter containing information about the synchronization context, including folder path,
	///     cancellation token, and synchronization mode.
	/// </param>
	/// <returns>A task representing the asynchronous operation of the synchronization process.</returns>
	private Task SyncAction(SyncDataParam data)
	{
		return SyncDataAsyncRecursive(data.Folder, data.Ctx, data.SyncMode);
	}


	/// <summary>
	///     Asynchronously processes changes in local data based on the provided arguments.
	/// </summary>
	/// <param name="data">
	///     An instance of <see cref="ProcessChangedDataArgs" /> containing details about the local data change,
	///     including the type of change, file paths, placeholders, sync mode, and other relevant information.
	/// </param>
	/// <returns>
	///     A <see cref="Task" /> representing the asynchronous operation of processing the changed local data.
	/// </returns>
	private Task ChangedLocalDataAction(ProcessChangedDataArgs data)
	{
		return ProcessChangedLocalDataAsync(data.ChangeType, data.FullPath, data.OldFullPath, data.LocalPlaceHolder, data.RemotePlaceholder, data.SyncMode, _globalShutDownToken);
	}

	#endregion

	// ----------------------------------------------------------------------------------------
	// PUBLIC METHODS
	// ----------------------------------------------------------------------------------------
	#region PUBLIC METHODS

	/// <summary>
	///     Releases all resources used by the <see cref="SyncProvider" /> instance.
	/// </summary>
	/// <remarks>
	///     This method calls <see cref="Dispose(bool)" /> with the disposing parameter set to true
	///     and suppresses finalization for the object using <see cref="GC.SuppressFinalize" />.
	///     Override <see cref="Dispose(bool)" /> to include additional cleanup logic if needed.
	/// </remarks>
	public void Dispose()
	{
		/* Ändern Sie diesen Code nicht. Fügen Sie Bereinigungscode in der Methode "Dispose(bool disposing)" ein. */
		Dispose(true);
		// ReSharper disable once GCSuppressFinalizeForTypeWithoutDestructor
		GC.SuppressFinalize(this);
	}


	/// <summary>
	///     Generates a unique identifier for the synchronization root.
	/// </summary>
	/// <returns>
	///     A string representing the unique synchronization root ID, which is constructed
	///     using the provider information, the current Windows identity, and a hash of the local root folder path.
	/// </returns>
	/// <remarks>
	///     This method combines several components such as the provider ID, current user SID,
	///     and the hash of the local root folder path to create a unique identifier. The generated ID
	///     is used in various parts of the synchronization workflow to uniquely identify the sync root.
	/// </remarks>
	public string GetSyncRootID()
	{
		var syncRootID = _syncContext.SyncProviderParameter.ProviderInfo.ProviderId.ToString();
		syncRootID += @"!";
		syncRootID += WindowsIdentity.GetCurrent().User?.Value;
		syncRootID += @"!";

		// Provider Account -> Used Hash of LocalPath asuming that no Account would be synchronized to the same Folder.
		syncRootID += _syncContext.LocalRootFolder.GetHashCode();
		return syncRootID;
	}


	/// <summary>
	///     Retrieves the registry key path for the Sync Root Manager in the HKEY_LOCAL_MACHINE hive.
	/// </summary>
	/// <returns>
	///     A string representing the full registry key path for the Sync Root Manager associated with the current Sync Root ID.
	/// </returns>
	public string GetSyncRootManagerRegistryKeyHKLM()
	{
		return @"Software\Microsoft\Windows\CurrentVersion\Explorer\SyncRootManager\" + GetSyncRootID();
	}


	/// <summary>
	///     Retrieves the CLSID (Class Identifier) associated with the namespace for the current Sync Root Manager.
	/// </summary>
	/// <returns>
	///     A string representing the Namespace CLSID stored in the Windows Registry for the current Sync Root Manager.
	/// </returns>
	/// <remarks>
	///     This method accesses the Windows Registry to extract the "NamespaceCLSID" value from the Sync Root Manager's
	///     registry key path.
	/// </remarks>
	public string? GetNamespaceCLSID()
	{
		var subkey = Registry.LocalMachine.OpenSubKey(GetSyncRootManagerRegistryKeyHKLM(), false);
		if (subkey != null)
		{
			return subkey.GetValue("NamespaceCLSID") as string;
		}

		return null;
	}


	/// <summary>
	///     Retrieves the registry key path under HKEY_CURRENT_USER (HKCU) corresponding to the Namespace for the Sync Root Manager.
	/// </summary>
	/// <returns>
	///     A string representing the registry key path under HKEY_CURRENT_USER (HKCU) for the Sync Root Manager's Namespace.
	/// </returns>
	/// <remarks>
	///     This method constructs the registry key path by appending the Namespace CLSID to a predefined base path for desktop
	///     namespaces.
	/// </remarks>
	public string GetSyncRootManagerNameSpaceRegistryKeyHKCU()
	{
		return @"Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\" + GetNamespaceCLSID();
	}


	/// <summary>
	///     Unregisters an existing sync root for the application, removing any associated configuration or directory registration.
	/// </summary>
	/// <param name="deleteRootDir">
	///     Indicates whether the associated root directory should be deleted after unregistration. Defaults to
	///     true.
	/// </param>
	/// <returns>
	///     A <see cref="Task" /> that represents the asynchronous operation. The task result contains a boolean value indicating
	///     whether the operation succeeded.
	/// </returns>
	public async Task<bool> UnregisterExistingRoot(bool deleteRootDir = true)
	{
		try
		{
			if (!Directory.Exists(_syncContext.LocalRootFolder))
			{
				return true;
			}

			var path = await StorageFolder.GetFolderFromPathAsync(_syncContext.LocalRootFolder);
			var syncRootInfo = StorageProviderSyncRootManager.GetSyncRootInformationForFolder(path);
			if (syncRootInfo != null)
			{
				return Unregister(syncRootInfo.Id, deleteRootDir);
			}
		}
		catch
		{
			return false;
		}

		return false;
	}


	/// <summary>
	///     Determines whether the specified folder is already registered as a sync root.
	/// </summary>
	/// <param name="folderPath">The <see cref="StorageFolder" /> representing the folder to check for sync root registration.</param>
	/// <returns>True if the folder is already registered as a sync root; otherwise, false.</returns>
	public static bool IsSyncRootRegistered(StorageFolder folderPath)
	{
		try
		{
			/* Get the sync root information for the specified folder */
			Log.Debug($"Checking sync root registration for folder: {folderPath.Path}");
			var syncRootInfo = StorageProviderSyncRootManager.GetSyncRootInformationForFolder(folderPath);

			/* If syncRootInfo is not null, the sync root is already registered */
			return syncRootInfo != null;
		}
		catch (Exception)
		{
			Log.Info(@"Syncroot not yet registered!");
			return false;
		}
	}


	/// <summary>
	///     Initiates the sync process asynchronously for the specified synchronization mode.
	/// </summary>
	/// <param name="syncMode">
	///     Specifies the type of synchronization to perform.
	///     Acceptable values are <see cref="SyncMode.Local" />, <see cref="SyncMode.Full" />, or other valid <see cref="SyncMode" />
	///     options.
	/// </param>
	/// <returns>Returns a <see cref="Task" /> that represents the asynchronous operation.</returns>
	/// <remarks>
	///     This method delegates to an overloaded variant, providing default values for optional parameters,
	///     such as the cancellation token or relative path.
	/// </remarks>
	public Task SyncDataAsync(SyncMode syncMode)
	{
		return SyncDataAsync(syncMode, string.Empty, _globalShutDownToken);
	}


	/// <summary>
	///     Initiates a data synchronization process based on the specified synchronization mode and relative path.
	/// </summary>
	/// <param name="syncMode">Specifies the mode of synchronization, such as local or full.</param>
	/// <param name="relativePath">
	///     The relative path of the directory or file to be synchronized. An empty or null value will synchronize
	///     the root.
	/// </param>
	/// <param name="ctx">Token for propagating cancellation notifications to the synchronization task.</param>
	/// <returns>A task representing the asynchronous synchronization operation.</returns>
	public Task SyncDataAsync(SyncMode syncMode, string relativePath)
	{
		return SyncDataAsync(syncMode, relativePath, _globalShutDownToken);
	}


	/// <summary>
	///     Initiates synchronization of data in the specified sync mode, with the option to provide a cancellation token.
	/// </summary>
	/// <param name="syncMode">The mode of synchronization to perform. Can be Local, Full, or FullQueue.</param>
	/// <param name="ctx">A token to observe while awaiting the task, which can be used to cancel the operation.</param>
	/// <returns>A <see cref="Task" /> that represents the asynchronous operation.</returns>
	public Task SyncDataAsync(SyncMode syncMode, CancellationToken ctx)
	{
		return SyncDataAsync(syncMode, string.Empty, ctx);
	}


	/// <summary>
	///     Synchronizes data based on the specified sync mode, relative path, and cancellation token.
	/// </summary>
	/// <param name="syncMode">The synchronization mode indicating the type of sync operation to perform (e.g., local or full).</param>
	/// <param name="relativePath">The relative path to the directory or file to synchronize, if applicable.</param>
	/// <param name="ctx">The <see cref="CancellationToken" /> to observe for cancellation of the operation.</param>
	/// <returns>A task that represents the asynchronous operation.</returns>
	public async Task SyncDataAsync(SyncMode syncMode, string relativePath, CancellationToken ctx)
	{
		/* Fast bail-out if provider is not connected */
		if (!_isConnected)
		{
			Log.Debug("SyncDataAsync skipped: provider not connected.");
			return;
		}

		/* Prevent overlapping runs (non-blocking acquire) */
		bool entered;
		try
		{
			entered = await _syncGate.WaitAsync(0, ctx).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			Log.Debug("SyncDataAsync gate acquisition canceled (shutdown).");
			return;
		}

		if (!entered)
		{
			Log.Debug("SyncDataAsync skipped: a sync is already in progress.");
			return;
		}

		try
		{
			//_syncInProgress = true;

			/* --- keep your original status announcements (guarded just in case) --- */
			try
			{
				switch (syncMode)
				{
					case SyncMode.Local:
						CldApi.CfUpdateSyncProviderStatus(_syncContext.ConnectionKey, CldApi.CF_SYNC_PROVIDER_STATUS.CF_PROVIDER_STATUS_SYNC_INCREMENTAL);
						break;
					case SyncMode.Full:
						CldApi.CfUpdateSyncProviderStatus(_syncContext.ConnectionKey, CldApi.CF_SYNC_PROVIDER_STATUS.CF_PROVIDER_STATUS_SYNC_FULL);
						break;
				}
			}
			catch (Exception ex)
			{
				/* Don’t let a status set kill the run */
				Log.Debug($"Unable to set provider status for {syncMode}: {ex.Message}");
			}

			/* Offload the heavy recursive work to a pool thread; Task.Run unwraps async */
			await Task.Run(async () =>
			{
				var rel = relativePath;
				if (!string.IsNullOrEmpty(rel))
				{
					rel = "\\" + rel; // preserve your original leading backslash behavior
				}

				await SyncDataAsyncRecursive(_syncContext.LocalRootFolder + rel, ctx, syncMode).ConfigureAwait(false);
			}, ctx).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			Log.Debug("SyncDataAsync canceled.");
		}
		catch (Exception ex)
		{
			/* If you want callers to know, rethrow here; otherwise swallow to keep engine alive. */
			Log.Error($"SyncDataAsync failed: {ex}");
		}
		finally
		{
			//_syncInProgress = false;

			/* Restore to IDLE (best-effort, don’t crash on error; only if still connected) */
			if (_isConnected)
			{
				try
				{
					CldApi.CfUpdateSyncProviderStatus(_syncContext.ConnectionKey, CldApi.CF_SYNC_PROVIDER_STATUS.CF_PROVIDER_STATUS_IDLE);
				}
				catch (Exception ex)
				{
					Log.Debug($"Failed to set provider status to IDLE: {ex.Message}");
				}
			}

			_syncGate.Release();
		}
	}


	/// <summary>
	///     Initializes the synchronization provider for the local folder and establishes a connection with the remote server.
	/// </summary>
	/// <returns>
	///     A task that represents the asynchronous operation of starting the synchronization process.
	/// </returns>
	/// <remarks>
	///     This method ensures the local sync root folder exists, registers the sync provider,
	///     establishes event callback mappings, connects to the synchronization root,
	///     and finalizes initialization tasks, including connecting to the server and updating the provider status.
	///     It is responsible for enabling synchronization and setting the system to a ready state.
	/// </remarks>
	public async Task Start()
	{
		if (_isConnected)
		{
			return;
		}

		if (Directory.Exists(_syncContext.LocalRootFolder) == false)
		{
			Log.Debug($"Creating Local Root Folder: {_syncContext.LocalRootFolder}");
			Directory.CreateDirectory(_syncContext.LocalRootFolder);
		}

		_syncRootID = GetSyncRootID();
		await Register(_syncRootID);

		_callbackMappings =
		[
			new CldApi.CF_CALLBACK_REGISTRATION { Callback = FETCH_PLACEHOLDERS,           Type = CldApi.CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_FETCH_PLACEHOLDERS },
			new CldApi.CF_CALLBACK_REGISTRATION { Callback = CANCEL_FETCH_PLACEHOLDERS,    Type = CldApi.CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_CANCEL_FETCH_PLACEHOLDERS },
			new CldApi.CF_CALLBACK_REGISTRATION { Callback = FETCH_DATA,                   Type = CldApi.CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_FETCH_DATA },
			new CldApi.CF_CALLBACK_REGISTRATION { Callback = CANCEL_FETCH_DATA,            Type = CldApi.CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_CANCEL_FETCH_DATA },
			new CldApi.CF_CALLBACK_REGISTRATION { Callback = NOTIFY_FILE_OPEN_COMPLETION,  Type = CldApi.CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_NOTIFY_FILE_OPEN_COMPLETION },
			new CldApi.CF_CALLBACK_REGISTRATION { Callback = NOTIFY_FILE_CLOSE_COMPLETION, Type = CldApi.CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_NOTIFY_FILE_CLOSE_COMPLETION },
			new CldApi.CF_CALLBACK_REGISTRATION { Callback = NOTIFY_DELETE,                Type = CldApi.CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_NOTIFY_DELETE },
			new CldApi.CF_CALLBACK_REGISTRATION { Callback = NOTIFY_DELETE_COMPLETION,     Type = CldApi.CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_NOTIFY_DELETE_COMPLETION },
			new CldApi.CF_CALLBACK_REGISTRATION { Callback = NOTIFY_RENAME,                Type = CldApi.CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_NOTIFY_RENAME },
			new CldApi.CF_CALLBACK_REGISTRATION { Callback = NOTIFY_RENAME_COMPLETION,     Type = CldApi.CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_NOTIFY_RENAME_COMPLETION },
			CldApi.CF_CALLBACK_REGISTRATION.CF_CALLBACK_REGISTRATION_END
		];

		_logTimeOffset = DateTimeOffset.UtcNow;
		_syncContext.ConnectionKey = default;

		Log.Debug("Connecting sync root ...");

		var ret = CldApi.CfConnectSyncRoot(_syncContext.LocalRootFolder, _callbackMappings, IntPtr.Zero, CldApi.CF_CONNECT_FLAGS.CF_CONNECT_FLAG_REQUIRE_PROCESS_INFO | CldApi.CF_CONNECT_FLAGS.CF_CONNECT_FLAG_REQUIRE_FULL_FILE_PATH, out _syncContext.ConnectionKey);
		ret.ThrowIfFailed();

		InitWatcher();
		_fileOperations = new FileOperations(_syncContext.LocalRootFolder, ShowMessage);

		Log.Debug("Connecting to server...");
		var connectResult = _syncContext.ServerProvider.Connect();
		Log.Debug("Connection result: " + connectResult.Status);

		ret = CldApi.CfUpdateSyncProviderStatus(_syncContext.ConnectionKey, CldApi.CF_SYNC_PROVIDER_STATUS.CF_PROVIDER_STATUS_IDLE);
		if (ret.Succeeded == false)
		{
			Log.Warn("Error with CfUpdateSyncProviderStatus: " + ret);
		}

		if (_apiLogLoopTask == null || _apiLogLoopTask.IsCompleted)
		{
			_apiLogLoopTask = StartApiFileLogLoop(_globalShutDownToken);
		}
		else
		{
			Log.Warn("API file log loop was already running - duplicate start avoided.");
		}

		_isConnected = true;
		Log.Debug("Ready.");
	}


	/// <summary>
	///     Stops the synchronization provider, optionally deleting the synchronization root directory.
	/// </summary>
	/// <param name="deleteRootDir">
	///     A boolean value specifying whether the synchronization root directory should be deleted.
	///     If set to true, the directory will be removed; otherwise, it will be preserved.
	/// </param>
	/// <remarks>
	///     This method halts all synchronization activities, ensuring that all running
	///     tasks or operations are gracefully terminated. If exceptions occur during the
	///     shutdown process, they are logged into the system for debugging purposes.
	///     Use this method to clean up resources and stop background tasks when the synchronization
	///     operations are no longer needed.
	/// </remarks>
	public void Stop(bool deleteRootDir = true)
	{
		try
		{
			StopAsync(deleteRootDir).GetAwaiter().GetResult();
		}
		catch (Exception ex)
		{
			Log.Error($"Stop (sync wrapper) failed: {ex}");
		}
	}


	/// <summary>
	///     Stops the synchronization provider asynchronously and performs cleanup operations.
	/// </summary>
	/// <param name="deleteRootDir">
	///     A boolean value indicating whether to delete the local root directory during shutdown.
	///     The default value is <c>true</c>.
	/// </param>
	/// <returns>
	///     A <see cref="Task" /> that represents the asynchronous operation of stopping the synchronization provider.
	///     The task completes once all operations and background tasks are halted, and the provider is properly disconnected and unregistered.
	/// </returns>
	/// <remarks>
	///     This method performs an orderly shutdown of the synchronization provider by:
	///     - Canceling active background operations.
	///     - Completing message pipelines and awaiting their completion to avoid potential hangs.
	///     - Disconnecting from the remote server/provider.
	///     - Optionally deleting the local root directory, based on the <paramref name="deleteRootDir" /> parameter.
	///     It ensures proper teardown and disconnection of system resources while logging the process.
	/// </remarks>
	public async Task StopAsync(bool deleteRootDir = true)
	{
		if (_isConnected == false)
		{
			return;
		}

		Log.Info("Stopping SyncProvider (orderly) ...");

		try
		{
			/* Block new CFAPI callbacks and drain in-flight callbacks first. */
			await BlockCallbacksAndDrainAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

			/* Stop generating new local work ASAP. */
			StopWatcher();

			/* Cancel in-flight fetches & background ops. */
			CancelFetch();
			_fileOperations?.Shutdown();
			_fileOperations = null;

			/* Signal all components to wind down. */
			_globalShutDownTokenSource.Cancel();

			/* Optionally mark as DISCONNECTED if that enum exists in your binding. */
			TrySetProviderStatus(_syncContext.ConnectionKey, "CF_PROVIDER_STATUS_DISCONNECTED");

			/* Close dataflow pipelines: stop accepting, then let them drain. */
			_syncActionBlock.Complete();
			_changedLocalDataQueue.Complete();

			await AwaitWithTimeout(_syncActionBlock.Completion, TimeSpan.FromSeconds(10), "syncActionBlock").ConfigureAwait(false);
			await AwaitWithTimeout(_changedLocalDataQueue.Completion, TimeSpan.FromSeconds(10), "changedLocalDataQueue").ConfigureAwait(false);

			/* Ensure our Task.Run(...) FETCH_DATA tasks are done (dictionary empties via EndFetch). */
			await WaitForActiveFetchesAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

			/* Await long-running API log loop if present. */
			if (_apiLogLoopTask != null)
			{
				await AwaitWithTimeout(_apiLogLoopTask, TimeSpan.FromSeconds(5), "apiLogLoop").ConfigureAwait(false);
				_apiLogLoopTask = null;
			}

			/* Disconnect provider plumbing. */
			_syncContext.ServerProvider.Disconnect();

			/* CFAPI disconnect (can wait for kernel callbacks) - off the UI thread. */
			var ret = await Task.Run(() => CldApi.CfDisconnectSyncRoot(_syncContext.ConnectionKey)).ConfigureAwait(false);
			if (ret.Succeeded)
			{
				_isConnected = false;
			}

			Log.Debug("DisconnectSyncRoot: " + ret);

			/* Mark TERMINATED if available. */
			TrySetProviderStatus(_syncContext.ConnectionKey, "CF_PROVIDER_STATUS_TERMINATED");

			/* Unregister & (optionally) delete root — also off the UI thread to avoid UI stalls. */
			if (_syncRootID != null)
			{
				var syncRootIdCopy = _syncRootID;
				await Task.Run(() => Unregister(syncRootIdCopy, deleteRootDir)).ConfigureAwait(false);
				_syncRootID = null;
			}

			Log.Info("SyncProvider stopped.");
		}
		catch (Exception ex)
		{
			Log.Error($"StopAsync failed: {ex}");
		}
	}


	/// <summary>
	///     Await a task with a timeout so shutdown cannot hang indefinitely.
	///     Logs a debug line if we timed out, and logs any exceptions.
	/// </summary>
	private static async Task AwaitWithTimeout(Task task, TimeSpan timeout, string name)
	{
		try
		{
			var completed = await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false);
			if (completed != task)
			{
				Log.Debug($"Timeout while awaiting {name} during shutdown.");
				return;
			}

			/* Observe the task result to surface any non-cancel errors */
			await task.ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			Log.Debug($"{name} canceled during shutdown."); // expected
		}
		catch (Exception ex)
		{
			Log.Error($"Error while awaiting {name}: {ex}");
		}
	}


	/* Wait for outstanding FETCH_DATA tasks we spawned via Task.Run */
	/* (tracked by _fetchCancellations via StartFetch/EndFetch) */
	private async Task WaitForActiveFetchesAsync(TimeSpan timeout)
	{
		var sw = Stopwatch.StartNew();
		while (sw.Elapsed < timeout)
		{
			if (_fetchCancellations.IsEmpty)
			{
				return;
			}

			await Task.Delay(100).ConfigureAwait(false);
		}

		Log.Debug($"Timeout waiting for active fetches to drain; remaining: {_fetchCancellations.Count}");
	}


	/// <summary>
	///     Determines whether the specified file path is excluded based on predefined exclusion patterns.
	/// </summary>
	/// <param name="relativeOrFullPath">
	///     The relative or full path of the file or directory to be checked against the exclusion patterns.
	/// </param>
	/// <returns>
	///     <c>true</c> if the specified path matches any of the exclusion patterns; otherwise, <c>false</c>.
	/// </returns>
	public bool IsExcludedFile(string relativeOrFullPath)
	{
		foreach (var match in _fileExclusions)
		{
			if (Regex.IsMatch(relativeOrFullPath, match, RegexOptions.IgnoreCase))
			{
				return true;
			}
		}

		return false;
	}


	/// <summary>
	///     Determines whether a file or directory should be excluded based on its path and attributes.
	/// </summary>
	/// <param name="relativeOrFullPath">The relative or full path of the file or directory to check.</param>
	/// <param name="attributes">The file or directory attributes used to assess exclusion criteria.</param>
	/// <returns>
	///     Returns <c>true</c> if the file or directory is excluded based on its attributes or path;
	///     otherwise, returns <c>false</c>.
	/// </returns>
	public bool IsExcludedFile(string relativeOrFullPath, FileAttributes attributes)
	{
		if (attributes.HasFlag(FileAttributes.System) || attributes.HasFlag(FileAttributes.Temporary))
		{
			return true;
		}

		return IsExcludedFile(relativeOrFullPath);
	}


	/// <summary>
	///     Reports the progress of a file operation to the system and notifies the components of the current progress.
	/// </summary>
	/// <param name="transferKey">The transfer key identifying the file operation whose progress is being reported.</param>
	/// <param name="total">The total size of the file or operation in bytes.</param>
	/// <param name="completed">The number of bytes that have been completed so far.</param>
	/// <param name="relativePath">The relative path of the file being processed.</param>
	/// <remarks>
	///     This method updates the progress of a file operation using the <see cref="Vanara.PInvoke.CldApi.CfReportProviderProgress" />
	///     function
	///     and raises the <see cref="FileProgressEvent" /> to notify components about the current progress.
	/// </remarks>
	public void ReportProviderProgress(CldApi.CF_TRANSFER_KEY transferKey, long total, long completed, string relativePath)
	{
		/* Report progress to System */
		var ret = CldApi.CfReportProviderProgress(_syncContext.ConnectionKey, transferKey, total, completed);

		/* Report progress to components */
		try
		{
			FileProgressEvent?.Invoke(this, new FileProgressEventArgs(relativePath, completed, total));
		}
		catch (Exception ex)
		{
			Log.Error("Exception: " + ex.Message);
		}
	}

	#endregion

	// ----------------------------------------------------------------------------------------
	// PRIVATE METHODS
	// ----------------------------------------------------------------------------------------
	#region PRIVATE METHODS

	/// <summary>
	///     Validates required fields for sync root registration and returns a tuple (isValid, errorMessage).
	///     This avoids passing invalid data into WinRT which can cause AccessViolation in some environments.
	/// </summary>
	private (bool ok, string error) ValidateSyncRootArgs(string rootID, string localRootFolder, string displayName, string iconResourcePath)
	{
		if (string.IsNullOrWhiteSpace(rootID))
		{
			return (false, "SyncRoot ID is empty.");
		}

		if (rootID.IndexOf('\0') >= 0)
		{
			return (false, "SyncRoot ID contains invalid characters.");
		}

		/* Basic path check before calling WinRT */
		if (string.IsNullOrWhiteSpace(localRootFolder) || !Directory.Exists(localRootFolder))
		{
			return (false, $"Local root folder does not exist: \"{localRootFolder}\"");
		}

		if (string.IsNullOrWhiteSpace(displayName))
		{
			return (false, "DisplayNameResource is empty.");
		}

		/* Icon can be optional, but when provided ensure it resolves to an existing file or DLL resource spec */
		if (!string.IsNullOrWhiteSpace(iconResourcePath))
		{
			try
			{
				/* Accept "foo.dll,-101" resource specs as well as absolute file paths */
				if (iconResourcePath.Contains(",") == false && !File.Exists(iconResourcePath))
				{
					return (false, $"IconResource not found at path: \"{iconResourcePath}\"");
				}
			}
			catch (Exception ex)
			{
				Log.Warn($"IconResource validation failed: {ex.Message}");
			}
		}

		return (true, string.Empty);
	}


	/// <summary>
	///     Registers the sync root with retries on transient COM errors (e.g. RPC_E_SERVERCALL_RETRYLATER / ERROR_BUSY).
	///     Returns true on success and sets out error on failure.
	/// </summary>
	private bool TryRegisterWithBackoff(StorageProviderSyncRootInfo info, out string error)
	{
		error = string.Empty;
		var delays = new[] { 100, 250, 500 }; // milliseconds

		for (var attempt = 0; attempt < delays.Length; attempt++)
		{
			try
			{
				StorageProviderSyncRootManager.Register(info);
				return true;
			}
			catch (COMException cex)
			{
				/* Busy or not available yet - retry */
				var hr = cex.HResult;
				/* Common transient: 0x8001010A RPC_E_SERVERCALL_RETRYLATER, 0x800700AA ERROR_BUSY */
				if (hr == unchecked((int)0x8001010A) || hr == unchecked((int)0x800700AA))
				{
					Log.Warn($"Register transient failure (attempt {attempt + 1}): 0x{hr:X8} {cex.Message}");
					Thread.Sleep(delays[attempt]);
					continue;
				}

				error = $"Register failed (COM 0x{hr:X8}): {cex.Message}";
				return false;
			}
			catch (AccessViolationException aex)
			{
				error = $"Register failed (AccessViolation): {aex.Message}";
				return false;
			}
			catch (Exception ex)
			{
				error = $"Register failed: {ex}";
				return false;
			}
		}

		error = "Register failed after retries (busy).";
		return false;
	}


	/// <summary>
	///     Determines whether the specified path corresponds to an existing file or directory.
	/// </summary>
	/// <param name="name">The path to check for existence, which can point to a file or directory.</param>
	/// <returns>True if the specified path exists as a file or directory; otherwise, false.</returns>
	internal static bool FileOrDirectoryExists(string name)
	{
		return Directory.Exists(name) || File.Exists(name);
	}


	/// <summary>
	///     Registers a sync root with the system using the provided root ID if it is not already registered.
	/// </summary>
	/// <param name="rootID">A string representing the unique identifier for the sync root to be registered.</param>
	/// <remarks>
	///     This method checks whether sync root registration is supported on the current platform.
	///     If supported and the sync root is not yet registered, it creates a new <see cref="StorageProviderSyncRootInfo" /> instance
	///     with the necessary details and registers it using <see cref="StorageProviderSyncRootManager.Register" />.
	/// </remarks>
	/// <returns>A task that represents the asynchronous register operation.</returns>
	/// <exception cref="NotSupportedException">Thrown when sync root registration is not supported on the current platform.</exception>
	private async Task Register(string rootID)
	{
		if (StorageProviderSyncRootManager.IsSupported() == false)
		{
			throw new NotSupportedException();
		}

		Log.Debug($"Registering with rootID {rootID} ...");

		var localRootFolder = _syncContext.LocalRootFolder;
		var displayName = _syncContext.SyncProviderParameter.ProviderInfo.ProviderName;
		var shortcutIcon = Path.Combine(AppContext.BaseDirectory, "Assets", "Images", "Icons", "Logo_light.ico");

		if (!File.Exists(shortcutIcon))
		{
			/* Fallback to a system icon resource if app icon is missing */
			shortcutIcon = "imageres.dll,-3";
		}

		/* Pre-validate arguments to avoid invalid parameters reaching WinRT */
		var validate = ValidateSyncRootArgs(rootID, localRootFolder, displayName, shortcutIcon);
		if (validate.ok == false)
		{
			var msg = $"Cannot register sync root: {validate.error}";
			Log.Error(msg);
			ShowMessage("Sync setup failed", msg);
			return;
		}

		StorageFolder path;
		try
		{
			path = await StorageFolder.GetFolderFromPathAsync(localRootFolder);
		}
		catch (Exception ex)
		{
			var msg = $"Cannot open local root folder \"{localRootFolder}\": {ex.Message}";
			Log.Error($"Register aborted: {ex}");
			ShowMessage("Sync setup failed", msg);
			return;
		}

		if (!IsSyncRootRegistered(path))
		{
			Log.Info($"Registering SyncRoot: {path}");
			StorageProviderSyncRootInfo SyncRootInfo = new()
			{
				Id = rootID,
				AllowPinning = true,
				DisplayNameResource = _syncContext.SyncProviderParameter.ProviderInfo.ProviderName,
				HardlinkPolicy = StorageProviderHardlinkPolicy.None,
				HydrationPolicy = StorageProviderHydrationPolicy.Partial,
				HydrationPolicyModifier = StorageProviderHydrationPolicyModifier.AutoDehydrationAllowed | StorageProviderHydrationPolicyModifier.StreamingAllowed,
				InSyncPolicy = StorageProviderInSyncPolicy.FileLastWriteTime,
				Path = path,
				PopulationPolicy = StorageProviderPopulationPolicy.Full,
				ProtectionMode = StorageProviderProtectionMode.Unknown,
				ProviderId = _syncContext.SyncProviderParameter.ProviderInfo.ProviderId,
				Version = _syncContext.SyncProviderParameter.ProviderInfo.ProviderVersion,
				IconResource = shortcutIcon,
				ShowSiblingsAsGroup = false,
				RecycleBinUri = null,
				Context = CryptographicBuffer.ConvertStringToBinary(GetSyncRootID(), BinaryStringEncoding.Utf8)
			};

			SyncRootInfo.StorageProviderItemPropertyDefinitions.Add(new StorageProviderItemPropertyDefinition { DisplayNameResource = "Description", Id = 0 });

			if (!TryRegisterWithBackoff(SyncRootInfo, out var err))
			{
				Log.Error(err + $"\nId={rootID}; Path={path.Path}; DisplayName={displayName}; Icon={shortcutIcon}");
				ShowMessage("Sync setup failed", err);
				return;
			}

			var result = IsSyncRootRegistered(path);
			Log.Debug("Register result: " + result);
		}
	}


	/// <summary>
	///     Unregisters the specified sync root and optionally deletes the root directory.
	/// </summary>
	/// <param name="rootId">The identifier of the sync root to unregister.</param>
	/// <param name="deleteRootDir">Determines whether the root directory should be deleted. Defaults to true.</param>
	/// <returns>
	///     Returns <c>true</c> if the sync root is successfully unregistered and the root directory is deleted if specified;
	///     otherwise, <c>false</c>.
	/// </returns>
	private bool Unregister(string rootId, bool deleteRootDir = true)
	{
		try
		{
			StorageProviderSyncRootManager.Unregister(rootId);
			if (deleteRootDir)
			{
				/* delete root folder */
				var syncRootPath2 = StringHelper.GetFullPathRootFolder();

				/* Delete the folder */
				if (Directory.Exists(syncRootPath2))
				{
					/* true to delete all subdirectories and files\ */
					if (!TryDeleteDirectoryWithBackoff(syncRootPath2, rootId))
					{
						Log.Error("[Unregister] Failed to clean up sync-root folder '{syncRootPath2}'. It may be in use by Explorer or a shell extension.");
						ShowMessage("Sync cleanup warning", "The sync folder couldn't be removed because it was in use. We'll retry later or remove it on reboot.");
					}
				}
			}

			return true;
		}
		catch (COMException cex)
		{
			Log.Error($"Unregister failed (COM 0x{cex.HResult:X8}): {cex.Message}");
			ShowMessage("Sync cleanup failed", $"Unable to unregister sync root (0x{cex.HResult:X8}). {cex.Message}");
			return false;
		}
		catch (AccessViolationException aex)
		{
			Log.Error($"Unregister failed (AccessViolation): {aex.Message}");
			ShowMessage("Sync cleanup failed", aex.Message);
			return false;
		}
		catch (Exception ex)
		{
			Log.Error($"Unregister failed: {ex}");
			ShowMessage("Sync cleanup failed", ex.Message);
			return false;
		}
	}


	/// <summary>
	///     Displays a message with a specified title and content.
	/// </summary>
	/// <param name="title">The title of the message to be displayed.</param>
	/// <param name="message">The content of the message to be displayed.</param>
	private void ShowMessage(string message)
	{
		ShowMessage(string.Empty, message);
	}


	/// <summary>
	///     Displays a message to the user with a specified title and message content.
	/// </summary>
	/// <param name="title">The title of the message to be displayed.</param>
	/// <param name="message">The content of the message to be displayed.</param>
	/// <remarks>
	///     This method invokes the <see cref="OnShowNotification" /> delegate, if assigned,
	///     to handle the message display logic. It is typically used to provide notifications or alerts to the user.
	/// </remarks>
	private void ShowMessage(string title, string message)
	{
		OnShowNotification?.Invoke(title, message);
	}


	/// <summary>
	///     Retrieves a list of server-side file placeholders within the specified relative directory path asynchronously.
	/// </summary>
	/// <param name="relativePath">The relative path of the directory to retrieve the file placeholders from.</param>
	/// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
	/// <returns>
	///     A task that represents the asynchronous operation. The task result contains a <see cref="GenericResult{List{Placeholder}}" />
	///     object
	///     that contains the list of file placeholders and a status indicating the operation's success or failure.
	/// </returns>
	/// <remarks>
	///     This method interacts with the server to asynchronously fetch and compile a list of file placeholders,
	///     filtering out excluded and system files. Cancellation is respected during the process.
	/// </remarks>
	internal async Task<GenericResult<List<Placeholder>>> GetServerFileListAsync(string relativePath, CancellationToken cancellationToken)
	{
		Log.Debug("GetServerFileListAsync: " + relativePath);

		GenericResult<List<Placeholder>> completionStatus = new();
		completionStatus.Data = new List<Placeholder>();

		using (var fileList = _syncContext.ServerProvider.GetNewFileList())
		{
			var result = await fileList.OpenAsync(relativePath, cancellationToken);
			Log.Debug(result.Succeeded.ToString());
			Log.Debug(result.Message);

			if (!result.Succeeded)
			{
				completionStatus.Status = result.Status;
				return completionStatus;
			}

			var getNextResult = await fileList.GetNextAsync();
			while (getNextResult.Succeeded && !cancellationToken.IsCancellationRequested)
			{
				var relativeFileName = relativePath + "\\" + getNextResult.Placeholder.RelativeFileName;

				if (!IsExcludedFile(relativeFileName) && !getNextResult.Placeholder.FileAttributes.HasFlag(FileAttributes.System))
				{
					completionStatus.Data.Add(getNextResult.Placeholder);
				}

				if (cancellationToken.IsCancellationRequested)
				{
					break;
				}

				getNextResult = await fileList.GetNextAsync();
			}

			var closeResult = await fileList.CloseAsync();

			completionStatus.Status = closeResult.Status;
			cancellationToken.ThrowIfCancellationRequested();

			Log.Debug("GetServerFileListAsync Completed: " + relativePath);
			return completionStatus;
		}
	}


	/// <summary>
	///     Retrieves a list of local placeholders representing files and directories
	///     from the specified absolute path.
	/// </summary>
	/// <param name="absolutePath">
	///     The absolute file system path from which to retrieve the list of placeholders.
	/// </param>
	/// <param name="cancellationToken">
	///     A token to observe for cancellation requests, used to terminate the operation prematurely if necessary.
	/// </param>
	/// <returns>
	///     A list of <see cref="Placeholder" /> objects representing the files and directories
	///     found in the specified absolute path.
	/// </returns>
	/// <exception cref="OperationCanceledException">
	///     Thrown if the operation is canceled by the provided <paramref name="cancellationToken" />.
	/// </exception>
	internal List<Placeholder> GetLocalFileList(string absolutePath, CancellationToken cancellationToken)
	{
		List<Placeholder> localPlaceholders = new();
		DirectoryInfo directory = new(absolutePath);

		foreach (var fileSystemInfo in directory.EnumerateFileSystemInfos())
		{
			cancellationToken.ThrowIfCancellationRequested();
			localPlaceholders.Add(new Placeholder(fileSystemInfo));
		}

		return localPlaceholders;
	}


	/// <summary>
	///     Recursively syncs a given folder and its contents with the server or local storage.
	/// </summary>
	/// <param name="folder">The full path to the folder that will be synchronized.</param>
	/// <param name="ctx">The cancellation token to observe for cancellation requests.</param>
	/// <param name="syncMode">The synchronization mode indicating the level of syncing to perform.</param>
	/// <returns>Returns a task containing a boolean value indicating whether any files were hydrated during the synchronization.</returns>
	private async Task<bool> SyncDataAsyncRecursive(string folder, CancellationToken ctx, SyncMode syncMode)
	{
		var relativeFolder = GetRelativePath(folder);
		var anyFileHydrated = false;
		List<Placeholder> remotePlaceholderes;

		using (CloudFilterAPI.ExtendedPlaceholderState localFolderPlaceholder = new(folder))
		{
			var isExcludedFile = IsExcludedFile(folder, localFolderPlaceholder.Attributes);

			/* Get Filelist from Server on FullSync */
			if (syncMode >= SyncMode.Full && !isExcludedFile)
			{
				var getServerFileListResult = await GetServerFileListAsync(relativeFolder, ctx);
				if (getServerFileListResult.Status != CloudFilterAPI.NtStatus.STATUS_NOT_A_CLOUD_FILE)
				{
					getServerFileListResult.ThrowOnFailure();
				}

				remotePlaceholderes = getServerFileListResult.Data;
			}
			else
			{
				remotePlaceholderes = new List<Placeholder>();
			}

			if (isExcludedFile)
			{
				localFolderPlaceholder.ConvertToPlaceholder(true);
				localFolderPlaceholder.SetPinState(CldApi.CF_PIN_STATE.CF_PIN_STATE_EXCLUDED);
				localFolderPlaceholder.SetInSyncState(CldApi.CF_IN_SYNC_STATE.CF_IN_SYNC_STATE_IN_SYNC);
			}

			using (var findHandle = Kernel32.FindFirstFile(@"\\?\" + folder + @"\*", out var findData))
			{
				var fileFound = findHandle.IsInvalid == false;

				/* Check existing local placeholders */
				using (CloudFilterAPI.AutoDisposeList<CloudFilterAPI.ExtendedPlaceholderState> localPlaceholders = new())
				{
					while (fileFound)
					{
						if (findData.cFileName != "." && findData.cFileName != "..")
						{
							var fullFilePath = folder + "\\" + findData.cFileName;

							CloudFilterAPI.ExtendedPlaceholderState localPlaceholder = new(findData, folder);
							localPlaceholders.Add(localPlaceholder);

							var remotePlaceholder = (from a in remotePlaceholderes where string.Equals(a.RelativeFileName, findData.cFileName, StringComparison.CurrentCultureIgnoreCase) select a).FirstOrDefault();
							if (localPlaceholder.IsDirectory)
							{
								if (localPlaceholder.IsPlaceholder)
								{
									if (!localPlaceholder.PlaceholderState.HasFlag(CldApi.CF_PLACEHOLDER_STATE.CF_PLACEHOLDER_STATE_PARTIAL) || !localPlaceholder.PlaceholderState.HasFlag(CldApi.CF_PLACEHOLDER_STATE.CF_PLACEHOLDER_STATE_IN_SYNC) || isExcludedFile || localPlaceholder.PlaceholderInfoStandard.PinState == CldApi.CF_PIN_STATE.CF_PIN_STATE_PINNED)
									{
										if (syncMode == SyncMode.Full)
										{
											/* using ActionBlock because of need to manage multiple tasks concurrently, benefit from automatic parallelism, or need to control the degree of concurrency. */
											await _syncActionBlock.SendAsync(new SyncDataParam { Ctx = ctx, Folder = fullFilePath, SyncMode = syncMode });
											anyFileHydrated = true;
										}
										else
										{
											if (await SyncDataAsyncRecursive(fullFilePath, ctx, syncMode))
											{
												anyFileHydrated = true;
											}
										}
									}
									else
									{
										localPlaceholder.SetInSyncState(CldApi.CF_IN_SYNC_STATE.CF_IN_SYNC_STATE_IN_SYNC);
										/* Ignore Directorys which will trigger FETCH_PLACEHOLDER */
									}
								}
								else
								{
									if (syncMode == SyncMode.Full)
									{
										/* using ActionBlock because of need to manage multiple tasks concurrently, benefit from automatic parallelism, or need to control the degree of concurrency. */
										await _syncActionBlock.SendAsync(new SyncDataParam { Ctx = ctx, Folder = fullFilePath, SyncMode = syncMode });
										anyFileHydrated = true;
									}
									else
									{
										if (await SyncDataAsyncRecursive(fullFilePath, ctx, syncMode))
										{
											anyFileHydrated = true;
										}
									}
								}
							}
							else
							{
								DynamicServerPlaceholder dynPlaceholder;
								if (syncMode == SyncMode.Full && remotePlaceholder != null)
								{
									dynPlaceholder = (DynamicServerPlaceholder)remotePlaceholder;
								}
								else
								{
									dynPlaceholder = new DynamicServerPlaceholder(GetRelativePath(fullFilePath), localPlaceholder.IsDirectory, _syncContext);
								}

								Log.Debug($"dynPlaceholder: {dynPlaceholder}");
							}

							if (!localPlaceholder.PlaceholderState.HasFlag(CldApi.CF_PLACEHOLDER_STATE.CF_PLACEHOLDER_STATE_IN_SYNC) || !localPlaceholder.PlaceholderState.HasFlag(CldApi.CF_PLACEHOLDER_STATE.CF_PLACEHOLDER_STATE_PARTIAL) || localPlaceholder.PlaceholderInfoStandard.OnDiskDataSize > 0)
							{
								anyFileHydrated = true;
							}
						}

						ctx.ThrowIfCancellationRequested();
						fileFound = Kernel32.FindNextFile(findHandle, out findData);
					}

					foreach (var lpl in localPlaceholders)
					{
						if (!lpl.PlaceholderState.HasFlag(CldApi.CF_PLACEHOLDER_STATE.CF_PLACEHOLDER_STATE_IN_SYNC) || !lpl.PlaceholderState.HasFlag(CldApi.CF_PLACEHOLDER_STATE.CF_PLACEHOLDER_STATE_PARTIAL) || lpl.PlaceholderInfoStandard.OnDiskDataSize > 0)
						{
							anyFileHydrated = true;
							break;
						}
					}

					/* Add missing local Placeholders */
					foreach (var remotePlaceholder in remotePlaceholderes)
					{
						var fullFilePath = folder + "\\" + remotePlaceholder.RelativeFileName;

						if ((from a in localPlaceholders where string.Equals(a.FullPath, fullFilePath, StringComparison.CurrentCultureIgnoreCase) select a).Any() == false)
						{
							var (placeholderInfo, fileIdentity) = CloudFilterAPI.CreatePlaceholderInfo(remotePlaceholder);
							var ret = CldApi.CfCreatePlaceholders(folder, [placeholderInfo], 1, CldApi.CF_CREATE_FLAGS.CF_CREATE_FLAG_NONE, out var EntriesProcessed);
							fileIdentity?.Dispose();
						}
					}

					if (syncMode == SyncMode.Full)
					{
						localFolderPlaceholder.SetInSyncState(CldApi.CF_IN_SYNC_STATE.CF_IN_SYNC_STATE_IN_SYNC);

						if (!anyFileHydrated && !isExcludedFile && localFolderPlaceholder.PlaceholderInfoStandard.PinState != CldApi.CF_PIN_STATE.CF_PIN_STATE_PINNED)
						{
							localFolderPlaceholder.EnableOnDemandPopulation();
						}
						else
						{
							localFolderPlaceholder.DisableOnDemandPopulation();
						}
					}

					return anyFileHydrated;
				}
			}
		}
	}


	/* handle downsync API */
	/// <summary>
	///     Initiates a loop to retrieve and process file logs from the API until cancellation is requested.
	/// </summary>
	/// <param name="cancellationToken">
	///     A token that can be used to signal the cancellation of the operation.
	/// </param>
	/// <returns>
	///     A task that represents the asynchronous operation of the log retrieval and processing.
	/// </returns>
	private async Task StartApiFileLogLoop(CancellationToken cancellationToken)
	{
		var teamDriveService = new TeamDriveService();
#if DEBUG
		var delay = 1;
#else
		var delay = 3;
#endif
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				var fromDate = _logTimeOffset.ToUnixTimeMilliseconds();
				var toDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
				var fileLogResponse = await teamDriveService.GetFileLogs(fromDate, toDate, cancellationToken);

				if (fileLogResponse?.Logs.Length > 0)
				{
					foreach (var fileLog in fileLogResponse.Logs)
					{
						switch (fileLog.Action)
						{
							case "create":
								SyncFileCreate(fileLog);
								break;
							case "delete":
								SyncFileDeletion(fileLog);
								break;
							case "copy":
								SyncFileCreate(fileLog);
								break;
							case "move":
								SyncFileMove(fileLog);
								break;
							case "rename":
								SyncFileMove(fileLog);
								break;
							case "restore":
								SyncFileCreate(fileLog);
								break;
						}
					}
				}

				_logTimeOffset = DateTimeOffset.FromUnixTimeMilliseconds(toDate);
				/* Wait for 3 minutes or until cancellation is requested */
				await Task.Delay(TimeSpan.FromMinutes(delay), cancellationToken);
			}
			catch (TaskCanceledException)
			{
				/* Task was canceled, exit the loop gracefully */
				Log.Info(@"API call loop canceled.");
				break;
			}
			catch (Exception ex)
			{
				/* Handle other exceptions */
				Log.Error($@"An error occurred: {ex.Message}");
				await Task.Delay(TimeSpan.FromMinutes(delay), cancellationToken);
			}
		}
	}


	/// <summary>
	///     Excludes a file or directory from being synced and deletes it from the local file system.
	/// </summary>
	/// <param name="filePath">The full path of the file or directory to be excluded and deleted.</param>
	/// <remarks>
	///     If the specified path exists, the method checks whether it refers to a file or a directory.
	///     If it is a placeholder file or directory, its pin state is set to excluded before being deleted.
	///     This operation ensures the file or directory is no longer managed by the synchronization provider.
	/// </remarks>
	private static void ExcludeAndDeleteFile(string filePath)
	{
		if (FileOrDirectoryExists(filePath))
		{
			var isDirectory = File.GetAttributes(filePath).HasFlag(FileAttributes.Directory);
			using (var placeholder = new CloudFilterAPI.ExtendedPlaceholderState(filePath))
			{
				if (placeholder.IsPlaceholder)
				{
					if (!placeholder.SetPinState(CldApi.CF_PIN_STATE.CF_PIN_STATE_EXCLUDED, isDirectory))
					{
						throw new InvalidOperationException("Failed to set pin state");
					}
				}
			}

			if (isDirectory)
			{
				Directory.Delete(filePath, true);
			}
			else
			{
				File.Delete(filePath);
			}
		}
	}


	/// <summary>
	///     Determines whether a given file is new based on its local path and version details.
	/// </summary>
	/// <param name="file">The <see cref="FileItem" /> object representing the file to evaluate.</param>
	/// <returns>
	///     Returns <c>true</c> if the file is new or cannot be reliably identified as an existing file;
	///     otherwise, returns <c>false</c>.
	/// </returns>
	/// <remarks>
	///     A file is considered new if it doesn't exist locally, or if it exists but either:
	///     (1) it is not a placeholder, or
	///     (2) its version identifier does not match the provided file's version identifier.
	/// </remarks>
	private static bool IsNewFile(FileItem file)
	{
		var localPath = file.LocalPath;
		if (FileOrDirectoryExists(localPath))
		{
			try
			{
				using var placeholder = new CloudFilterAPI.ExtendedPlaceholderState(localPath);
				if (placeholder.IsPlaceholder)
				{
					var fileIdentity = CloudFilterAPI.GetCloudFileIdentity(localPath);
					return fileIdentity?.VersionID != file.VersionID;
				}

				return true;
			}
			catch
			{
				return false;
			}
		}

		return true;
	}


	/// <summary>
	///     Creates a placeholder for a specified file item in the local file system.
	/// </summary>
	/// <param name="item">The <see cref="FileItem" /> instance representing the file for which the placeholder is to be created.</param>
	/// <returns>
	///     A <see cref="HRESULT" /> value indicating the result of the operation. A successful result is denoted by
	///     <see cref="HRESULT.S_OK" />.
	/// </returns>
	/// <remarks>
	///     This method generates placeholder metadata from the given file item and attempts to create the placeholder in the
	///     local file system. It uses the <see cref="CldApi.CfCreatePlaceholders" /> API, and any generated file identity resources
	///     are disposed of after operation.
	/// </remarks>
	private static HRESULT? CreatePlaceHolder(FileItem item, string parentPath)
	{
		var remotePlaceholder = new Placeholder(item, parentPath);
		var (placeholderInfo, fileIdentity) = CloudFilterAPI.CreatePlaceholderInfo(remotePlaceholder);
		var localPath = item.LocalPath;
		var dir = Path.GetDirectoryName(localPath);

		if (dir != null)
		{
			var res = CldApi.CfCreatePlaceholders(dir, [placeholderInfo], 1, CldApi.CF_CREATE_FLAGS.CF_CREATE_FLAG_NONE, out var EntriesProcessed);
			fileIdentity?.Dispose();
			return res;
		}

		fileIdentity?.Dispose();
		return null;
	}


	/// <summary>
	///     Handles the creation of a new file during synchronization based on file log data.
	/// </summary>
	/// <param name="item">
	///     The log entry containing information about the file to be created. This parameter must not be null.
	/// </param>
	/// <remarks>
	///     This method verifies if the file is new. If the file is new, it removes any excluded files at the specified path
	///     and creates a placeholder for the file.
	/// </remarks>
	private static void SyncFileCreate(FileLog item)
	{
		var file = item.File;
		if (file == null)
		{
			return;
		}

		if (IsNewFile(file))
		{
			ExcludeAndDeleteFile(file.LocalPath);
			CreatePlaceHolder(file, StringHelper.GetParentPath(file.Path));
		}
	}


	/// <summary>
	///     Handles the deletion of a file based on the specified file log information.
	/// </summary>
	/// <param name="item">The file log information containing details about the file to be deleted, such as its local path.</param>
	private static void SyncFileDeletion(FileLog item)
	{
		var filePath = item.LocalPath;
		if (filePath == null)
		{
			return;
		}

		ExcludeAndDeleteFile(filePath);
	}


	/// <summary>
	///     Handles the move operation for a synced file by excluding and deleting the file's old path,
	///     and ensuring the creation of a placeholder for the new file if required.
	/// </summary>
	/// <param name="item">
	///     The <see cref="FileLog" /> instance containing details about the file move operation, including old and new
	///     paths.
	/// </param>
	private static void SyncFileMove(FileLog item)
	{
		var oldPath = item.OldLocalPath;
		if (oldPath == null)
		{
			return;
		}

		var file = item.File;
		if (file == null)
		{
			return;
		}

		ExcludeAndDeleteFile(oldPath);

		if (IsNewFile(file))
		{
			ExcludeAndDeleteFile(file.LocalPath);
			CreatePlaceHolder(file, StringHelper.GetParentPath(file.Path));
		}
	}


	/// <summary>
	///     Initializes the file system watcher to monitor changes in the local directory.
	/// </summary>
	/// <remarks>
	///     This method configures a <see cref="FileSystemWatcher" /> to detect changes such as file creation, modification,
	///     renaming, and size changes within the specified local root folder.
	///     Additionally, it sets up cancellation tokens and associated tasks to handle local and remote change queues.
	/// </remarks>
	private void InitWatcher()
	{
		Log.Debug("InitWatcher");

		StopWatcher();

		_watcher = new FileSystemWatcher { Path = _syncContext.LocalRootFolder, IncludeSubdirectories = true, NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName | NotifyFilters.Attributes | NotifyFilters.LastWrite | NotifyFilters.Size, Filter = "*" };
		_watcher.Error += OnFileSystemWatcherError;
		_watcher.Changed += OnFileSystemWatcherChanged;
		_watcher.Created += OnFileSystemWatcherCreated;
		_watcher.EnableRaisingEvents = true;
	}


	/// <summary>
	///     Stops the active <see cref="FileSystemWatcher" /> instance and cancels any ongoing operations related to data change
	///     tracking.
	/// </summary>
	/// <remarks>
	///     This method cancels the <see cref="CancellationTokenSource" /> associated with the data change operations,
	///     disables and disposes of the <see cref="FileSystemWatcher" />, and ensures that the watcher is no longer active.
	///     It is typically called to clean up resources and stop file system monitoring when the sync process is terminated.
	/// </remarks>
	private void StopWatcher()
	{
		if (_watcher != null)
		{
			Log.Debug("StopWatcher");
			_watcher.EnableRaisingEvents = false;
			_watcher.Dispose();
		}
	}


	/// <summary>
	///     Adds a local change action to the processing queue using the specified file information,
	///     synchronization mode, and cancellation token.
	/// </summary>
	/// <param name="fileInfo">
	///     The local file change metadata containing information such as change type, full path, and old full path.
	/// </param>
	/// <param name="syncMode">
	///     The mode of synchronization to be applied to the file (e.g., Local, Full, or FullQueue).
	/// </param>
	/// <param name="ctx">
	///     The cancellation token used to observe cancellation requests for the task.
	/// </param>
	/// <returns>
	///     A task representing the asynchronous operation of adding the change action to the queue.
	/// </returns>
	private Task AddLocalChangeActionToQueue(LocalChangedData fileInfo, SyncMode syncMode, CancellationToken ctx)
	{
		return _changedLocalDataQueue.SendAsync(new ProcessChangedDataArgs { ChangeType = fileInfo.ChangeType, SyncMode = syncMode, FullPath = fileInfo.FullPath, OldFullPath = fileInfo.OldFullPath });
	}


	/// <summary>
	///     Processes changes to local data based on the type of change, updates necessary
	///     placeholders, and interacts with remote services as needed.
	/// </summary>
	/// <param name="changeType">The type of change that occurred (e.g., created, deleted, renamed, etc.).</param>
	/// <param name="fullPath">The full path of the affected local file or directory.</param>
	/// <param name="oldFullpath">The previous full path of the file or directory, used when a rename or move operation has occurred.</param>
	/// <param name="localPlaceHolder">The placeholder that represents the affected local resource.</param>
	/// <param name="remotePlaceholder">The dynamic placeholder used to manage or retrieve the corresponding remote resource.</param>
	/// <param name="syncMode">The sync mode indicating how synchronization is to be handled (e.g., local-only, full sync, etc.).</param>
	/// <param name="ctx">A cancellation token to monitor for and respond to operation cancellation requests.</param>
	/// <returns>A task that represents the asynchronous operation.</returns>
	private Task ProcessChangedLocalDataAsync(ExtendedWatcherChangeTypes changeType, string fullPath, string? oldFullpath, CloudFilterAPI.ExtendedPlaceholderState? localPlaceHolder, DynamicServerPlaceholder? remotePlaceholder, SyncMode? syncMode, CancellationToken ctx)
	{
		/* deprecated */
		return Task.CompletedTask;
	}


	/// <summary>
	///     Converts the provided full path to a relative path based on the local root folder of the current synchronization context.
	/// </summary>
	/// <param name="fullPath">
	///     The absolute path to be converted to a relative path, starting from the synchronization root folder.
	/// </param>
	/// <returns>
	///     A string representing the relative path if the full path is within the root folder;
	///     otherwise, throws a <see cref="NotSupportedException" /> if the path is not supported.
	/// </returns>
	/// <exception cref="NotSupportedException">
	///     Thrown if the provided path is not a subpath of the local root folder.
	/// </exception>
	internal string GetRelativePath(string fullPath)
	{
		if (fullPath.Equals(_syncContext.LocalRootFolder, StringComparison.CurrentCultureIgnoreCase))
		{
			return string.Empty;
		}

		if (fullPath.StartsWith(_syncContext.LocalRootFolder, StringComparison.CurrentCultureIgnoreCase))
		{
			return fullPath.Remove(0, _syncContext.LocalRootFolder.Length + 1);
		}

		return fullPath;
	}


	/// <summary>
	///     Retrieves the relative path from a full path using local root folder information.
	/// </summary>
	/// <param name="callbackInfo">
	///     The callback information containing the normalized path that will be processed to derive the relative path.
	/// </param>
	/// <returns>
	///     A string representing the relative path derived by removing the local root folder prefix from the full path.
	/// </returns>
	internal string GetRelativePath(in CldApi.CF_CALLBACK_INFO callbackInfo)
	{
		if (callbackInfo.NormalizedPath.StartsWith(_syncContext.LocalRootFolderNormalized, StringComparison.CurrentCultureIgnoreCase))
		{
			var relativePath = callbackInfo.NormalizedPath.Remove(0, _syncContext.LocalRootFolderNormalized.Length);
			return relativePath.TrimStart(char.Parse("\\"));
		}

		return callbackInfo.NormalizedPath;
	}


	/// <summary>
	///     Converts a full path to a relative path based on the local root folder of the sync context.
	/// </summary>
	/// <param name="callbackInfo">
	///     The callback information containing the target path to be converted.
	/// </param>
	/// <returns>
	///     A string representing the relative path derived from the full path, or the target path if it does not match
	///     the local root folder.
	/// </returns>
	internal string GetRelativePath(in CldApi.CF_CALLBACK_PARAMETERS.RENAME callbackInfo)
	{
		if (callbackInfo.TargetPath.StartsWith(_syncContext.LocalRootFolderNormalized, StringComparison.CurrentCultureIgnoreCase))
		{
			var relativePath = callbackInfo.TargetPath.Remove(0, _syncContext.LocalRootFolderNormalized.Length);
			return relativePath.TrimStart(char.Parse("\\"));
		}

		return callbackInfo.TargetPath;
	}


	/// <summary>
	///     Retrieves the local full path corresponding to the relative path derived from the provided callback information.
	/// </summary>
	/// <param name="callbackInfo">
	///     The callback information that contains details about the current request or operation,
	///     including the normalized path from which the relative path is generated.
	/// </param>
	/// <returns>
	///     The full local path composed by combining the local root folder with the relative path.
	/// </returns>
	internal string GetLocalFullPath(in CldApi.CF_CALLBACK_INFO callbackInfo)
	{
		var relativePath = GetRelativePath(callbackInfo);
		return Path.Combine(_syncContext.LocalRootFolder, relativePath);
	}


	/// <summary>
	///     Constructs the full local path for a given relative path by combining it with the local root folder.
	/// </summary>
	/// <param name="relativePath">The relative path to be resolved to a full local path.</param>
	/// <returns>The full local path constructed by combining the local root folder and the given relative path.</returns>
	internal string GetLocalFullPath(string relativePath)
	{
		return Path.Combine(_syncContext.LocalRootFolder, relativePath);
	}


	/// <summary>
	///     Converts a relative path or rename callback information into a full local path
	///     based on the sync context's root folder.
	/// </summary>
	/// <param name="renameInfo">
	///     The callback information containing the relative path to be resolved to a full local path.
	/// </param>
	/// <returns>
	///     A string representing the full local path derived from the provided relative path.
	/// </returns>
	internal string GetLocalFullPath(in CldApi.CF_CALLBACK_PARAMETERS.RENAME renameInfo)
	{
		var relativePath = GetRelativePath(renameInfo);
		return Path.Combine(_syncContext.LocalRootFolder, relativePath);
	}


	/// <summary>
	///     Determines the appropriate chunk size for file upload or download operations.
	/// </summary>
	/// <remarks>
	///     This method calculates the chunk size by constraining the default chunk size
	///     within the minimum and maximum bounds defined in the server provider's preferred settings.
	/// </remarks>
	/// <returns>
	///     The calculated chunk size for file transfer operations.
	/// </returns>
	internal int GetChunkSize()
	{
		var currentChunkSize = Math.Min(_chunkSize, _syncContext.ServerProvider.PreferredServerProviderSettings.MaxChunkSize);
		currentChunkSize = Math.Max(currentChunkSize, _syncContext.ServerProvider.PreferredServerProviderSettings.MinChunkSize);
		return currentChunkSize;
	}


	/// <summary>
	///     Creates and initializes a new instance of the <see cref="CldApi.CF_OPERATION_INFO" /> structure
	///     for a specified operation type and callback information.
	/// </summary>
	/// <param name="callbackInfo">
	///     A reference to the <see cref="CldApi.CF_CALLBACK_INFO" /> structure that provides information
	///     about the specific callback triggering this operation.
	/// </param>
	/// <param name="operationType">
	///     A <see cref="CldApi.CF_OPERATION_TYPE" /> value specifying the operation to be performed.
	/// </param>
	/// <returns>
	///     A fully initialized <see cref="CldApi.CF_OPERATION_INFO" /> structure ready for use in Cloud
	///     Files API operations.
	/// </returns>
	private CldApi.CF_OPERATION_INFO CreateOperationInfo(in CldApi.CF_CALLBACK_INFO callbackInfo, CldApi.CF_OPERATION_TYPE operationType)
	{
		CldApi.CF_OPERATION_INFO opInfo = new()
		{
			Type              = operationType,
			ConnectionKey     = callbackInfo.ConnectionKey,
			TransferKey       = callbackInfo.TransferKey,
			CorrelationVector = callbackInfo.CorrelationVector,
			RequestKey        = callbackInfo.RequestKey
		};

		opInfo.StructSize = (uint)Marshal.SizeOf(opInfo);
		return opInfo;
	}


	/// <summary>
	///     Retrieves the identity of a cloud file from the provided callback information.
	/// </summary>
	/// <param name="callbackInfo">
	///     Data structure containing the callback information for a cloud file operation,
	///     including the file identity pointer.
	/// </param>
	/// <returns>
	///     An instance of <see cref="CloudFileIdentity" /> representing the file identity
	///     if deserialization is successful, or <c>null</c> if the file identity is not set
	///     or deserialization fails.
	/// </returns>
	private CloudFileIdentity? GetFileIdentity(in CldApi.CF_CALLBACK_INFO callbackInfo)
	{
		if (callbackInfo.FileIdentity != IntPtr.Zero)
		{
			try
			{
				return CloudFileIdentity.Deserialize(callbackInfo.FileIdentity);
			}
			catch
			{
				return null;
			}
		}

		return null;
	}


	/// <summary>
	///     Retrieves the cloud path associated with the specified callback information.
	/// </summary>
	/// <param name="callbackInfo">
	///     The callback information containing details about the requested file or directory.
	/// </param>
	/// <returns>
	///     The cloud path as a string if the file identity exists; otherwise, attempts
	///     to retrieve the cloud path based on the normalized path.
	/// </returns>
	private string GetCloudPath(in CldApi.CF_CALLBACK_INFO callbackInfo)
	{
		return GetFileIdentity(callbackInfo)?.Path ?? GetCloudPath(callbackInfo, callbackInfo.NormalizedPath);
	}


	/// <summary>
	///     Converts a normalized local path in the sync root to its cloud equivalent path.
	/// </summary>
	/// <param name="callbackInfo">
	///     The callback information that provides the local context, including volume and sync root data.
	/// </param>
	/// <param name="normalizedPath">
	///     The normalized path representing the local file or folder within the sync root.
	/// </param>
	/// <returns>
	///     The computed cloud path corresponding to the given local path, using the sync root context.
	/// </returns>
	private string GetCloudPath(in CldApi.CF_CALLBACK_INFO callbackInfo, string normalizedPath)
	{
		var fullClientPath = callbackInfo.VolumeDosName + normalizedPath;
		var rootPath = _syncContext.LocalRootFolder + "\\";
		var path = fullClientPath.Substring(rootPath.Length).Replace('\\', '/');
		return path;
	}


	/// <summary>
	///     Generates a unique identifier for a fetch operation based on the provided callback information and parameters.
	/// </summary>
	/// <param name="callbackInfo">
	///     The callback information containing details about the current operation and file context.
	/// </param>
	/// <param name="callbackParameters">
	///     The parameters associated with the fetch operation, providing detailed data about the required file range.
	/// </param>
	/// <returns>
	///     A string representing the unique identifier for the fetch operation, constructed using the normalized path,
	///     required file offset, and required length.
	/// </returns>
	private string GetFetchID(in CldApi.CF_CALLBACK_INFO callbackInfo, in CldApi.CF_CALLBACK_PARAMETERS callbackParameters)
	{
		return callbackInfo.NormalizedPath + '|' + callbackParameters.FetchData.RequiredFileOffset + '|' + callbackParameters.FetchData.RequiredLength;
	}


	/// <summary>
	///     Retrieves the access level for a given local file path.
	/// </summary>
	/// <param name="localFilePath">
	///     The full path of the local file for which the access level is to be determined.
	/// </param>
	/// <returns>
	///     A task that represents the asynchronous operation. The task result contains the corresponding
	///     <see cref="AccessLevel" /> for the specified file, or null if the file path is not valid or does not match
	///     the expected criteria.
	/// </returns>
	private async Task<AccessLevel?> GetAccessLevel(string localFilePath)
	{
		var rootPath = _syncContext.LocalRootFolder + '\\';
		if (localFilePath.StartsWith(rootPath) && localFilePath.Length > rootPath.Length)
		{
			var start = rootPath.Length;
			var index = localFilePath.IndexOf('\\', start);

			if (index > start)
			{
				var baseFolder = localFilePath.Substring(start, index - start + 1).Replace('\\', '/');
				return await _fileService.GetFileAccessLevel(TokenStorage.TeamID, baseFolder);
			}
		}

		return null;
	}


	/// <summary>
	///     Adds a download URL to the internal dictionary to associate a file path with its respective download location.
	/// </summary>
	/// <param name="path">The file path for which the download URL is being added.</param>
	/// <param name="url">The download URL to be associated with the given file path.</param>
	/// <remarks>
	///     This method ensures that the provided file path and its corresponding download
	///     URL are stored in a thread-safe manner using the internal dictionary.
	///     The stored URLs can later be used for downloading files.
	/// </remarks>
	private void AddDownloadUrl(string path, string url)
	{
		_downloadURLs.TryAdd(path, url);
	}


	/// <summary>
	///     Removes a download URL associated with the specified path.
	/// </summary>
	/// <param name="path">
	///     The path for which the associated download URL should be removed.
	/// </param>
	private void RemoveDownloadUrl(string path)
	{
		_downloadURLs.TryRemove(path, out var removedURL);
	}


	/// <summary>
	///     Initiates a fetch operation with the specified identifier.
	/// </summary>
	/// <param name="id">The unique identifier associated with the fetch operation.</param>
	/// <remarks>
	///     This method adds the specified identifier to the fetch cancellation dictionary,
	///     and initializes a new <see cref="CancellationTokenSource" /> for managing the
	///     lifecycle of the fetch operation.
	/// </remarks>
	private void StartFetch(string id)
	{
		_fetchCancellations.TryAdd(id, new CancellationTokenSource());
	}


	/// <summary>
	///     Ends the fetch operation for the specified identifier by removing the associated
	///     <see cref="CancellationTokenSource" /> from the active fetch operations.
	/// </summary>
	/// <param name="id">
	///     The unique identifier of the fetch operation to be ended.
	/// </param>
	private void EndFetch(string id)
	{
		_fetchCancellations.TryRemove(id, out var cancellationTokenSource);
	}


	/// <summary>
	///     Cancels an ongoing fetch operation identified by the specified ID.
	/// </summary>
	/// <param name="id">
	///     The unique identifier of the fetch operation to cancel.
	/// </param>
	/// <remarks>
	///     This method attempts to retrieve the cancellation token associated with the given ID from the
	///     fetch cancellation dictionary and, if found, triggers the cancellation.
	/// </remarks>
	private void CancelFetch(string id)
	{
		if (_fetchCancellations.TryGetValue(id, out var cancellationTokenSource))
		{
			cancellationTokenSource.Cancel();
		}
	}


	/// <summary>
	///     Cancels all ongoing fetch operations by iterating through the active fetch cancellation tokens
	///     and signaling cancellation for each of them.
	/// </summary>
	/// <remarks>
	///     This method is primarily called during shutdown or when a sync operation needs to be interrupted.
	///     It ensures that any in-progress fetch tasks are terminated immediately by triggering their associated
	///     cancellation tokens.
	///     All cancellation tokens managed in the internal dictionary <c>_fetchCancellations</c> are processed
	///     to ensure proper interruption of pending fetch operations.
	///     This is useful to clean up resources and stop background tasks in scenarios where operations
	///     need to halt abruptly.
	/// </remarks>
	private void CancelFetch()
	{
		foreach (var cancellationTokenSource in _fetchCancellations.Values)
		{
			cancellationTokenSource.Cancel();
		}
	}


	/// <summary>
	///     Attempts to update the provider's status to a specified value.
	/// </summary>
	/// <param name="key">
	///     The unique connection key that represents the synchronization context with the cloud provider.
	/// </param>
	/// <param name="name">
	///     The status name to set for the provider. This should match a value in the <c>CF_SYNC_PROVIDER_STATUS</c> enumeration.
	/// </param>
	/// <remarks>
	///     This method uses the given status name to update the provider's status through the Cloud Files API.
	///     It ensures the status is parsed to the corresponding enumeration and applies it to the provider.
	///     If an invalid status is provided or an error occurs, the exception will be caught and logged.
	/// </remarks>
	private static void TrySetProviderStatus(CldApi.CF_CONNECTION_KEY key, string name)
	{
		try
		{
			if (Enum.TryParse(typeof(CldApi.CF_SYNC_PROVIDER_STATUS), name, out var status))
			{
				CldApi.CfUpdateSyncProviderStatus(key, (CldApi.CF_SYNC_PROVIDER_STATUS)status);
			}
		}
		catch (Exception ex)
		{
			Log.Debug($"Unable to set provider status {name}: {ex.Message}");
		}
	}


	/// <summary>
	///     Transfers data to the cloud file sync platform using the specified connection key, transfer key, buffer, offset, length, and
	///     completion status.
	/// </summary>
	/// <param name="connectionKey">The connection key that identifies the sync provider's connection to the platform.</param>
	/// <param name="transferKey">The transfer key associated with the specific file operation.</param>
	/// <param name="buffer">A pointer to the buffer containing the data to be transferred.</param>
	/// <param name="offset">The offset, in bytes, from the start of the file where the data transfer begins.</param>
	/// <param name="length">The length, in bytes, of the data to be transferred.</param>
	/// <param name="completionStatus">The status indicating the outcome of the data transfer operation.</param>
	/// <returns>Returns an <see cref="HRESULT" /> indicating the success or failure of the operation.</returns>
	private static HRESULT TransferData(CldApi.CF_CONNECTION_KEY connectionKey, CldApi.CF_TRANSFER_KEY transferKey, IntPtr buffer, long offset, long length, NTStatus completionStatus)
	{
		CldApi.CF_OPERATION_INFO opInfo = new() { Type = CldApi.CF_OPERATION_TYPE.CF_OPERATION_TYPE_TRANSFER_DATA, ConnectionKey = connectionKey, TransferKey = transferKey };

		opInfo.StructSize = (uint)Marshal.SizeOf(opInfo);
		CldApi.CF_OPERATION_PARAMETERS.TRANSFERDATA transferData = new() { CompletionStatus = completionStatus, Buffer = buffer, Offset = offset, Length = length };

		var opParam = CldApi.CF_OPERATION_PARAMETERS.Create(transferData);
		return CldApi.CfExecute(opInfo, ref opParam);
	}


	/// <summary>
	///     Downloads a specific range of data from a file located at the given URL and transfers it using the specified connection and
	///     transfer keys.
	/// </summary>
	/// <param name="connectionKey">The key identifying the cloud file connection to be used for transferring data.</param>
	/// <param name="transferKey">The key identifying the specific data transfer operation.</param>
	/// <param name="url">The URL of the file to be partially downloaded.</param>
	/// <param name="fileSize">The total size of the file in bytes.</param>
	/// <param name="offset">The byte offset at which to begin downloading data.</param>
	/// <param name="length">The length of the data to download, in bytes, starting from the specified offset.</param>
	/// <param name="cancellationToken">A token that can be used to cancel the download operation.</param>
	/// <returns>
	///     A task representing the asynchronous operation. The task result contains a boolean indicating whether the operation was
	///     successful.
	/// </returns>
	/// <exception cref="InvalidOperationException">
	///     Thrown if the server does not support partial content or if the specified range is
	///     invalid.
	/// </exception>
	/// <exception cref="OperationCanceledException">Thrown if the operation is canceled via the provided cancellation token.</exception>
	private async Task<bool> DownloadPartialData(CldApi.CF_CONNECTION_KEY connectionKey, CldApi.CF_TRANSFER_KEY transferKey, string url, long fileSize, long offset, long length, CancellationToken cancellationToken)
	{
		bool isSucessful;
		using (var client = new HttpClient())
		{
			var isPartialContent = false;
			if (offset > 0 || length < fileSize)
			{
				isPartialContent = true;
				client.DefaultRequestHeaders.Range = new RangeHeaderValue(offset, offset + length - 1);
			}

			var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

			/* Ensure the response is successful and supports partial content */
			if (isPartialContent && response.StatusCode != HttpStatusCode.PartialContent)
			{
				throw new InvalidOperationException("The server does not support partial content or the range is invalid.");
			}

			using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
			{
				var bytes = new byte[4096];
				var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
				var buffer = handle.AddrOfPinnedObject();
				long totalRead = 0;
				var count = (int)Math.Min(length, bytes.Length);

				try
				{
					while (totalRead < length)
					{
						try
						{
							await contentStream.ReadExactlyAsync(bytes, 0, count, cancellationToken);
							if (cancellationToken.IsCancellationRequested)
							{
								throw new OperationCanceledException();
							}
						}
						catch (Exception e)
						{
							NTStatus status = e is OperationCanceledException ? NTStatus.STATUS_CANCELLED : NTStatus.STATUS_UNSUCCESSFUL;
							TransferData(connectionKey, transferKey, IntPtr.Zero, offset + totalRead, count, status);
							break;
						}

						if (TransferData(connectionKey, transferKey, buffer, offset + totalRead, count, NTStatus.STATUS_SUCCESS).Failed)
						{
							break;
						}

						totalRead += count;
						if (totalRead + count > length)
						{
							count = (int)(length - totalRead);
						}
					}

					isSucessful = totalRead == length;
				}
				finally
				{
					handle.Free();
				}
			}
		}

		return isSucessful;
	}


	/// <summary>
	///     Fetches a partial or complete file from the server and transfers it to the local system based on the specified parameters.
	/// </summary>
	/// <param name="id">The unique identifier of the download task.</param>
	/// <param name="connectionKey">The cloud file connection key associated with the sync provider session.</param>
	/// <param name="transferKey">The transfer key uniquely identifying the transfer of the file within the provider session.</param>
	/// <param name="filePath">The relative or absolute path of the file to be fetched.</param>
	/// <param name="fileSize">The total size of the file in bytes.</param>
	/// <param name="versionId">The version identifier of the file being requested.</param>
	/// <param name="offset">The offset in bytes from the beginning of the file, indicating where to start fetching data.</param>
	/// <param name="length">The length in bytes of the data to fetch, starting from the specified offset.</param>
	/// <returns>A <see cref="Task" /> representing the asynchronous operation, where the file is fetched and transferred.</returns>
	/// <remarks>
	///     During the operation, this method ensures file fetch and transfer consistency by managing cancellation tokens,
	///     validating server connectivity, generating download URLs when necessary, and handling potential errors.
	/// </remarks>
	private async Task FetchFile(string id, CldApi.CF_CONNECTION_KEY connectionKey, CldApi.CF_TRANSFER_KEY transferKey, string filePath, long fileSize, string? versionId, long offset, long length)
	{
		try
		{
			if (_syncContext.ServerProvider.Status != ServerProviderStatus.Connected || versionId == null)
			{
				throw new InvalidOperationException();
			}

			StartFetch(id);

			if (!_fetchCancellations.TryGetValue(id, out var cancellationTokenSource))
			{
				throw new InvalidOperationException();
			}

			string? url;
			var cancellationToken = cancellationTokenSource.Token;
			var teamID = TokenStorage.TeamID;

			if (!_downloadURLs.TryGetValue(filePath, out url) && teamID != null)
			{
				var fileParam = new FileParam { Path = filePath, Size = fileSize, VersionID = versionId };
				url = await _fileService.DownloadFile(teamID, fileParam, cancellationToken);
				if (url != null)
				{
					AddDownloadUrl(filePath, url);
				}
			}

			if (url == null)
			{
				throw new InvalidOperationException();
			}

			var isSuccessful = await DownloadPartialData(connectionKey, transferKey, url, fileSize, offset, length, cancellationToken);
			if (!isSuccessful || offset + length >= fileSize)
			{
				RemoveDownloadUrl(filePath);
			}
		}
		catch
		{
			RemoveDownloadUrl(filePath);
			TransferData(connectionKey, transferKey, IntPtr.Zero, offset, length, NTStatus.STATUS_UNSUCCESSFUL);
		}
		finally
		{
			EndFetch(id);
		}
	}


	/// <summary>
	///     Completes a file or directory rename operation in the cloud files API.
	/// </summary>
	/// <param name="connectionKey">
	///     The connection key that identifies the current cloud files session.
	/// </param>
	/// <param name="transferKey">
	///     The transfer key that uniquely identifies the operation within the connection.
	/// </param>
	/// <param name="completionStatus">
	///     The completion status to be reported for the rename operation.
	/// </param>
	/// <returns>
	///     A <see cref="HRESULT" /> that represents the result of the file rename operation.
	/// </returns>
	/// <remarks>
	///     This method calls the Cloud Files API to finalize the rename operation by providing
	///     the operation details and the completion status. It is used internally to handle
	///     the outcomes of synchronous or asynchronous renaming operations.
	/// </remarks>
	private static HRESULT CompleteRename(CldApi.CF_CONNECTION_KEY connectionKey, CldApi.CF_TRANSFER_KEY transferKey, NTStatus completionStatus)
	{
		CldApi.CF_OPERATION_INFO opInfo = new() { Type = CldApi.CF_OPERATION_TYPE.CF_OPERATION_TYPE_ACK_RENAME, ConnectionKey = connectionKey, TransferKey = transferKey };

		opInfo.StructSize = (uint)Marshal.SizeOf(opInfo);
		CldApi.CF_OPERATION_PARAMETERS.ACKRENAME parameters = new() { CompletionStatus = completionStatus };

		var opParam = CldApi.CF_OPERATION_PARAMETERS.Create(parameters);
		return CldApi.CfExecute(opInfo, ref opParam);
	}


	/// <summary>
	///     Transfers a file from the remote server to the local folder.
	/// </summary>
	/// <param name="connectionKey">The connection key representing the active provider connection.</param>
	/// <param name="transferKey">The transfer key associated with the current file operation.</param>
	/// <param name="path">The path of the file to be moved from the remote server to the local folder.</param>
	/// <remarks>
	///     This method makes an asynchronous request to retrieve the file information from the server
	///     and attempts to move the file to the local folder. Upon successful completion, the rename
	///     operation is finalized. If an error occurs during the process, the appropriate status is
	///     returned to indicate failure.
	/// </remarks>
	private async Task MoveFileToLocalFolder(CldApi.CF_CONNECTION_KEY connectionKey, CldApi.CF_TRANSFER_KEY transferKey, string path)
	{
		try
		{
			await _fileOperations!.DeleteFile(path);
			CompleteRename(connectionKey, transferKey, NTStatus.STATUS_SUCCESS);
		}
		catch (HttpRequestException ex)
		{
			if (ex.StatusCode == HttpStatusCode.Forbidden)
			{
				var message = string.Format(Strings.error_no_permission_message_move, StringHelper.GetLastPartOfPath(path));
				ShowMessage(Strings.error_general_title_move, message);
			}

			CompleteRename(connectionKey, transferKey, NTStatus.STATUS_UNSUCCESSFUL);
		}
		catch
		{
			CompleteRename(connectionKey, transferKey, NTStatus.STATUS_UNSUCCESSFUL);
		}
	}


	/// <summary>
	///     Moves a file or folder from a specified source path to a target path asynchronously.
	/// </summary>
	/// <param name="connectionKey">
	///     The connection key associated with the operation, provided by the cloud files API.
	/// </param>
	/// <param name="transferKey">
	///     The transfer key used to uniquely identify the file transfer operation.
	/// </param>
	/// <param name="path">
	///     The source path of the file or folder to be moved.
	/// </param>
	/// <param name="targetPath">
	///     The target path where the file or folder should be moved to.
	/// </param>
	/// <remarks>
	///     This method interacts with file or folder services to move a file or folder within the cloud storage system.
	///     It ensures the success of the move operation or handles errors when the operation fails.
	///     Completion status is communicated back to the cloud provider.
	/// </remarks>
	private async Task MoveFile(CldApi.CF_CONNECTION_KEY connectionKey, CldApi.CF_TRANSFER_KEY transferKey, string path, string targetPath)
	{
		try
		{
			await _fileOperations!.MoveFile(path, targetPath);
			CompleteRename(connectionKey, transferKey, NTStatus.STATUS_SUCCESS);
		}
		catch
		{
			CompleteRename(connectionKey, transferKey, NTStatus.STATUS_UNSUCCESSFUL);
		}
	}


	/// <summary>
	///     Renames a file or folder on the Team Drive by communicating with the remote file services and updates the sync state
	///     accordingly.
	/// </summary>
	/// <param name="connectionKey">
	///     Represents the connection key to the sync provider callback, which is used to coordinate operations.
	/// </param>
	/// <param name="transferKey">
	///     Identifies the transfer key associated with the file operation, ensuring the tracking of the operation's lifecycle.
	/// </param>
	/// <param name="path">
	///     The source file or folder path that needs to be renamed.
	/// </param>
	/// <param name="targetPath">
	///     The target path to which the file or folder will be renamed.
	/// </param>
	/// <returns>
	///     A <see cref="Task" /> that represents the asynchronous operation of renaming the file or folder.
	/// </returns>
	private async Task RenameFile(CldApi.CF_CONNECTION_KEY connectionKey, CldApi.CF_TRANSFER_KEY transferKey, string path, string targetPath)
	{
		try
		{
			await _fileOperations!.RenameFile(path, targetPath);
			CompleteRename(connectionKey, transferKey, NTStatus.STATUS_SUCCESS);
		}
		catch
		{
			CompleteRename(connectionKey, transferKey, NTStatus.STATUS_UNSUCCESSFUL);
		}
	}


	/// <summary>
	///     Finalizes the delete operation initiated by the sync provider for a file or folder.
	/// </summary>
	/// <param name="connectionKey">The connection key associated with the current sync session.</param>
	/// <param name="transferKey">The transfer key identifying the item being deleted.</param>
	/// <param name="completionStatus">The status indicating the result of the delete operation.</param>
	/// <returns>A <see cref="HRESULT" /> value indicating the success or failure of the operation.</returns>
	private static HRESULT CompleteDelete(CldApi.CF_CONNECTION_KEY connectionKey, CldApi.CF_TRANSFER_KEY transferKey, NTStatus completionStatus)
	{
		CldApi.CF_OPERATION_INFO opInfo = new() { Type = CldApi.CF_OPERATION_TYPE.CF_OPERATION_TYPE_ACK_DELETE, ConnectionKey = connectionKey, TransferKey = transferKey };

		opInfo.StructSize = (uint)Marshal.SizeOf(opInfo);
		CldApi.CF_OPERATION_PARAMETERS.ACKDELETE parameters = new() { CompletionStatus = completionStatus };

		var opParam = CldApi.CF_OPERATION_PARAMETERS.Create(parameters);
		return CldApi.CfExecute(opInfo, ref opParam);
	}


	/// <summary>
	///     Deletes a specified file from the server and completes the delete operation with
	///     the appropriate status, handling any exceptions that occur during the process.
	/// </summary>
	/// <param name="connectionKey">A unique key representing the current cloud connection.</param>
	/// <param name="transferKey">A unique key representing the file transfer operation.</param>
	/// <param name="filePath">The path to the file to be deleted.</param>
	/// <returns>A Task that represents the asynchronous operation.</returns>
	private async Task DeleteFile(CldApi.CF_CONNECTION_KEY connectionKey, CldApi.CF_TRANSFER_KEY transferKey, string filePath)
	{
		try
		{
			await _fileOperations!.DeleteFile(filePath);
			CompleteDelete(connectionKey, transferKey, NTStatus.STATUS_SUCCESS);
		}
		catch (HttpRequestException ex)
		{
			if (ex.StatusCode == HttpStatusCode.Forbidden)
			{
				/* return STATUS_SUCCESS temporarily. this file will be restored. */
				var res = CompleteDelete(connectionKey, transferKey, NTStatus.STATUS_SUCCESS);
				if (res.Succeeded)
				{
					_filesToRestore.Add(filePath);
				}
			}
			else
			{
				CompleteDelete(connectionKey, transferKey, NTStatus.STATUS_UNSUCCESSFUL);
			}
		}
		catch
		{
			CompleteDelete(connectionKey, transferKey, NTStatus.STATUS_UNSUCCESSFUL);
		}
	}


	/// <summary>
	///     Releases all resources used by the <see cref="SyncProvider" /> instance.
	/// </summary>
	/// <remarks>
	///     This method calls <see cref="Dispose(bool)" /> with the disposing parameter set to true
	///     and suppresses finalization for the object using <see cref="GC.SuppressFinalize" />.
	///     Override <see cref="Dispose(bool)" /> to include additional cleanup logic if needed.
	/// </remarks>
	private void Dispose(bool disposing)
	{
		if (!_disposedValue)
		{
			_disposedValue = true;
		}
	}

	#endregion


	// ----------------------------------------------------------------------------------------
	// TYPES
	// ----------------------------------------------------------------------------------------
	#region TYPES

	/// <summary>
	///     Represents the parameters required for a data synchronization operation in the <see cref="SyncProvider" /> class.
	/// </summary>
	public class SyncDataParam
	{
		public CancellationToken Ctx;
		public required string Folder;
		public SyncMode SyncMode;
	}


	/// <summary>
	///     Represents the details of a file or directory change detected locally within the <see cref="SyncProvider" /> class.
	/// </summary>
	public class LocalChangedData
	{
		public ExtendedWatcherChangeTypes ChangeType { get; set; }
		public required string FullPath { get; set; }
		public string? OldFullPath { get; set; }
	}


	/// <summary>
	///     Represents the arguments required to process changes in local data during sync operations in the <see cref="SyncProvider" />
	///     class.
	/// </summary>
	public class ProcessChangedDataArgs
	{
		public ExtendedWatcherChangeTypes ChangeType;
		public required string FullPath;
		public CloudFilterAPI.ExtendedPlaceholderState? LocalPlaceHolder;
		public required string? OldFullPath;
		public DynamicServerPlaceholder? RemotePlaceholder;
		public SyncMode SyncMode;
	}

	#endregion


	#region Safe deletion helpers (Explorer race-proof)

	[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateFileW")]
	private static extern SafeFileHandle CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);


	[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool MoveFileEx(string lpExistingFileName, string? lpNewFileName, uint dwFlags);


	private const uint GENERIC_READ = 0x80000000;
	private const uint FILE_SHARE_READ = 0x00000001;
	private const uint FILE_SHARE_WRITE = 0x00000002;
	private const uint FILE_SHARE_DELETE = 0x00000004;
	private const uint OPEN_EXISTING = 3;
	private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
	private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
	private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
	private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
	private const uint MOVEFILE_DELAY_UNTIL_REBOOT = 0x00000004;
	private const int ERROR_SHARING_VIOLATION = 32;


	/// <summary>
	///     Try to delete a directory recursively with a bounded exponential back-off. If it remains busy,
	///     move it to a temp tombstone and schedule removal on reboot.
	/// </summary>
	/// <param name="path">Full path of directory to remove.</param>
	/// <param name="rootID">Root ID for tombstone naming.</param>
	/// <param name="maxAttempts">Max retry attempts.</param>
	/// <returns>true if deleted or tombstoned; false if it still exists at original path.</returns>
	private bool TryDeleteDirectoryWithBackoff(string path, string rootID, int maxAttempts = 6)
	{
		if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
		{
			return true;
		}

		var delayMs = 250;
		for (var attempt = 1; attempt <= maxAttempts; attempt++)
		{
			try
			{
				if (IsDirectoryInUse(path, out var lastErr))
				{
					Log.Warn($"[Unregister] Delete attempt {attempt}: directory in use (Win32={lastErr}).");
				}
				else
				{
					Directory.Delete(path, true);
					Log.Info($"[Unregister] Deleted sync-root folder on attempt {attempt}: {path}");
					return true;
				}
			}
			catch (UnauthorizedAccessException uae)
			{
				Log.Warn($"[Unregister] Delete attempt {attempt}: unauthorized - {uae.Message}");
			}
			catch (IOException ioex)
			{
				Log.Warn($"[Unregister] Delete attempt {attempt}: IO - {ioex.Message}");
			}

			Thread.Sleep(delayMs);
			delayMs = Math.Min(delayMs * 2, 5000);
		}

		/* Could not remove - move to tombstone under %TEMP%\RakutenDrive.PendingDelete\<rootID>_<timestamp>. */
		try
		{
			var tombstoneRoot = Path.Combine(Path.GetTempPath(), "RakutenDrive.PendingDelete");
			Directory.CreateDirectory(tombstoneRoot);
			var tombstone = Path.Combine(tombstoneRoot, $"{rootID}_{DateTime.Now:yyyyMMdd_HHmmssfff}");

			Directory.Move(path, tombstone);
			Log.Warn($"[Unregister] Moved busy folder to tombstone: {tombstone}");

			/* Best-effort immediate delete of tombstone. */
			try
			{
				Directory.Delete(tombstone, true);
				Log.Info($"[Unregister] Tombstone removed immediately: {tombstone}");
				return true;
			}
			catch
			{
				/* Schedule tombstone for removal at next reboot. */
				if (!MoveFileEx(tombstone, null, MOVEFILE_DELAY_UNTIL_REBOOT))
				{
					var err = Marshal.GetLastWin32Error();
					Log.Error($"[Unregister] Failed to schedule tombstone for delete on reboot (Win32={err}): {tombstone}");
				}
				else
				{
					Log.Warn($"[Unregister] Scheduled tombstone for delete on reboot: {tombstone}");
				}

				/* Also try to schedule original folder, in case move failed partially. */
				MoveFileEx(path, null, MOVEFILE_DELAY_UNTIL_REBOOT);
			}

			/* Still exists (tombstoned). */
			return false;
		}
		catch (Exception ex)
		{
			Log.Error($"[Unregister] Failed to tombstone busy folder '{path}': {ex.Message}");

			/* Try schedule original for delete on reboot. */
			MoveFileEx(path, null, MOVEFILE_DELAY_UNTIL_REBOOT);
			return false;
		}
	}


	/// <summary>
	///     Attempts to open an exclusive directory handle; returns true if in use (sharing violation).
	/// </summary>
	private static bool IsDirectoryInUse(string path, out int lastWin32Error)
	{
		lastWin32Error = 0;
		/* Try to open with no share. If other handles exist, CreateFileW will fail with ERROR_SHARING_VIOLATION. */
		using var h = CreateFileW(@"\\?\" + path, 0 /* desired access */, 0 /* share none */, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);

		if (h.IsInvalid)
		{
			lastWin32Error = Marshal.GetLastWin32Error();
			return lastWin32Error == ERROR_SHARING_VIOLATION;
		}

		/* We obtained an exclusive handle -> not in use. */
		return false;
	}


	/// <summary>
	///     Cleans up any %TEMP%\\RakutenDrive.PendingDelete tombstones from a previous run.
	/// </summary>
	public static void CleanupPendingDeletesOnStartup()
	{
		try
		{
			var tombstoneRoot = Path.Combine(Path.GetTempPath(), "RakutenDrive.PendingDelete");
			if (!Directory.Exists(tombstoneRoot))
			{
				return;
			}

			foreach (var dir in Directory.EnumerateDirectories(tombstoneRoot))
			{
				try
				{
					Directory.Delete(dir, true);
					Log.Info($"[Startup] Cleaned pending-delete folder: {dir}");
				}
				catch (Exception ex)
				{
					Log.Warn($"[Startup] Failed to clean pending-delete folder '{dir}': {ex.Message}");
				}
			}
		}
		catch (Exception ex)
		{
			Log.Warn($"[Startup] CleanupPendingDeletesOnStartup error: {ex.Message}");
		}
	}

	#endregion


	// --------------------------------------------------------------------------------------------
	// CFAPI callback gate helpers
	// --------------------------------------------------------------------------------------------
	#region CFAPI callback gate helpers

	/// <summary>
	///     Attempts to enter the callback processing state, ensuring control over concurrent callback handling.
	/// </summary>
	/// <remarks>
	///     This method manages the state of callback invocations, preventing new callbacks from being processed
	///     if the system is no longer accepting them. It increments the count of inflight callbacks and provides
	///     a mechanism to ensure proper cleanup in case callbacks are disallowed during processing.
	/// </remarks>
	/// <returns>
	///     Returns <c>true</c> if the callback processing is successfully entered; otherwise, <c>false</c> if
	///     callbacks are not being accepted or if the attempt to enter fails due to system constraints.
	/// </returns>
	private bool TryEnterCallback()
	{
		if (!_acceptNewCallbacks)
		{
			return false;
		}

		Interlocked.Increment(ref _inflightCallbackCount);
		if (!_acceptNewCallbacks)
		{
			Interlocked.Decrement(ref _inflightCallbackCount);
			return false;
		}

		return true;
	}


	/// <summary>
	///     Handles the decrement operation of the inflight callback count for tracking active callbacks.
	/// </summary>
	/// <remarks>
	///     This method is invoked in several scenarios where callback operations complete,
	///     ensuring that the inflight callback count is correctly managed.
	///     The inflight callback count is decremented in a thread-safe manner using
	///     <see cref="Interlocked.Decrement(ref int)" /> to avoid concurrency issues.
	///     Proper management of the inflight callback count is critical for resource
	///     cleanup and monitoring the state of ongoing background operations.
	/// </remarks>
	private void ExitCallback()
	{
		Interlocked.Decrement(ref _inflightCallbackCount);
	}


	/// <summary>
	///     Prevents new cloud file API callbacks from being accepted and waits for ongoing callbacks to complete within a specified timeout.
	/// </summary>
	/// <param name="timeout">The maximum amount of time to wait for currently processing callbacks to finish.</param>
	/// <returns>A task that represents the asynchronous operation. The task completes when no callbacks remain or the timeout is reached.</returns>
	private async Task BlockCallbacksAndDrainAsync(TimeSpan timeout)
	{
		_acceptNewCallbacks = false;
		var sw = Stopwatch.StartNew();
		while (Volatile.Read(ref _inflightCallbackCount) > 0 && sw.Elapsed < timeout)
		{
			await Task.Delay(50).ConfigureAwait(false);
		}

		if (Volatile.Read(ref _inflightCallbackCount) > 0)
		{
			Log.Debug($"Timeout while draining CFAPI callbacks - remaining: {Volatile.Read(ref _inflightCallbackCount)}");
		}
	}

	#endregion
}
