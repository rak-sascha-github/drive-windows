using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RakutenDrive.Controllers.CloudFilterAPIControllers;
using RakutenDrive.Models;
using RakutenDrive.Utils;
using RakutenDrive.Utils.CredentialManagement;
using Vanara.PInvoke;


namespace RakutenDrive.Controllers.Providers.SyncProvider;

/// <summary>
///     Represents a provider for synchronizing data between a local machine and a remote server.
/// </summary>
/// <remarks>
///     This class initializes and manages the synchronization context, settings, and operations required
///     to keep local and remote data in sync. It handles event subscriptions, processing workflows,
///     and timers that monitor both local and remote changes.
///     The SyncProvider class is intended to simplify data synchronization tasks by abstracting
///     the underlying operations such as file change detection, data fetching,
///     and failure handling in a structured manner.
/// </remarks>
public sealed partial class SyncProvider
{
	// --------------------------------------------------------------------------------------------
	// NESTED CLASSES
	// --------------------------------------------------------------------------------------------
	#region NESTED CLASSES

	/// <summary>
	///     Represents a disposable object that executes a specific action upon disposal.
	/// </summary>
	/// <typeparam name="T">
	///     The type of the value associated with the disposable object.
	/// </typeparam>
	public class DisposableObject<T> : IDisposable
	{
		public T Value => value;

		private readonly Action<T> disposeAction;
		private readonly T value;
		private bool disposedValue;

		/// <summary>
		///     Represents a disposable object that associates a value with an action to be executed upon disposal.
		/// </summary>
		/// <typeparam name="T">
		///     The type of the value managed by the disposable object.
		/// </typeparam>
		public DisposableObject(Action<T> disposeAction, T value)
		{
			this.value = value;
			this.disposeAction = disposeAction;
		}


		/// <summary>
		///     Releases the resources used by the object. This includes both managed and unmanaged resources.
		///     Ensures proper cleanup and prevents resource leaks by implementing the disposal pattern.
		/// </summary>
		public void Dispose()
		{
			/* Ändern Sie diesen Code nicht. Fügen Sie Bereinigungscode in der Methode "Dispose(bool disposing)" ein. */
			Dispose(true);
			GC.SuppressFinalize(this);
		}


		/// <summary>
		///     Releases all resources used by the object and ensures proper cleanup.
		///     This method should be called when the object is no longer needed to free up resources.
		/// </summary>
		/// <param name="disposing">
		///     Indicates whether the method is called explicitly (true) or by the finalizer (false).
		///     If true, managed resources should be freed by invoking their Dispose methods;
		///     otherwise, only unmanaged resources are released.
		/// </param>
		protected virtual void Dispose(bool disposing)
		{
			if (!disposedValue)
			{
				if (disposing)
				{
					disposeAction.Invoke(value);
				}

				disposedValue = true;
			}
		}

		#endregion
	}


	// Helper class to hold the placeholder list and identity references.
	private sealed class InFlightPlaceholderTransfer : IDisposable
	{
		private CloudFilterAPI.SafeHandlers.SafePlaceHolderList _placeholders { get; }
		private List<StringPtr> _identityList { get; }
		

		public InFlightPlaceholderTransfer(CloudFilterAPI.SafeHandlers.SafePlaceHolderList placeholders, List<StringPtr> identityList)
		{
			_placeholders = placeholders;
			_identityList = identityList;
		}


		public void Dispose()
		{
			/* SafePlaceHolderList.Dispose frees the unmanaged FileIdentity pointers. */
			_placeholders.Dispose();
			
			/* Clear the identity list to release managed references. */
			_identityList.Clear();
		}
	}
	

	// --------------------------------------------------------------------------------------------
	// PROPERTIES
	// --------------------------------------------------------------------------------------------
	#region PROPERTIES

	/// <summary>
	///     Represents an event that is triggered to report the progress of a file-related operation within the synchronization process.
	/// </summary>
	public event EventHandler<FileProgressEventArgs>? FileProgressEvent;

	//public readonly ActionBlock<FileChangedEventArgs> fileChangedActionBlock;
	//private readonly Task _fetchDataWorkerThread;
	//private DateTime _clipboardTime;
	//private readonly ConcurrentDictionary<Guid, DataActions> _fetchDataRunningQueue;

	private readonly string[] ExcludedProcessesForFetchPlaceholders =
	[
		@".*\\SearchProtocolHost\.exe.*", // This process tries to index folders which are just a few seconds before marked as "ENABLE_ON_DEMAND_POPULATION" which results in unwanted repopulation.
		@".*\\svchost\.exe.*StorSvc"      // This process cleans old data. Fetching of placeholders is not required for this process.
	];

	private readonly ConcurrentDictionary<string, CancellationTokenSource> _fetchPlaceholdersCancellationTokens = new();
	private CldApi.CF_CALLBACK_REGISTRATION[]? _callbackMappings;
	private FileSystemWatcher? _watcher;
	
	/* A static dictionary to keep placeholder lists alive until safe to dispose. */
	private static readonly ConcurrentDictionary<string, InFlightPlaceholderTransfer> _inFlightPlaceholders = new();

	/* Adjust this timeout as needed.  Five minutes is often sufficient. */
	private static readonly TimeSpan PlaceholderRetentionTimeout = TimeSpan.FromMinutes(5);

	#endregion

	// --------------------------------------------------------------------------------------------
	// PUBLIC METHODS
	// --------------------------------------------------------------------------------------------
	#region PUBLIC METHODS

	/// <summary>
	///     Handles the "Fetch Placeholders" callback operation triggered by the cloud filter driver. This manages
	///     the process of transferring placeholders from the cloud to the local filesystem, with support for
	///     cancellation, filtering, and error handling.
	/// </summary>
	/// <param name="CallbackInfo">
	///     Information about the callback, such as the triggering process and context.
	/// </param>
	/// <param name="CallbackParameters">
	///     Parameters for the operation, including the pattern for placeholders to be fetched.
	/// </param>
	public void FETCH_PLACEHOLDERS(in CldApi.CF_CALLBACK_INFO CallbackInfo, in CldApi.CF_CALLBACK_PARAMETERS CallbackParameters)
	{
		if (!TryEnterCallback())
		{
			var opInfo = CreateOperationInfo(CallbackInfo, CldApi.CF_OPERATION_TYPE.CF_OPERATION_TYPE_TRANSFER_PLACEHOLDERS);
			var tp = new CldApi.CF_OPERATION_PARAMETERS.TRANSFERPLACEHOLDERS
			{
				PlaceholderArray      = IntPtr.Zero,
				PlaceholderCount      = 0,
				PlaceholderTotalCount = 0,
				Flags                 = CldApi.CF_OPERATION_TRANSFER_PLACEHOLDERS_FLAGS.CF_OPERATION_TRANSFER_PLACEHOLDERS_FLAG_NONE,
				CompletionStatus      = NTStatus.STATUS_CANCELLED
			};
			
			var p = CldApi.CF_OPERATION_PARAMETERS.Create(tp);
			CldApi.CfExecute(opInfo, ref p);
			
			return;
		}

		try
		{
			Log.Info($"Fetching placeholders for path \"{CallbackInfo.NormalizedPath}\" ...");

			var opInfo      = CreateOperationInfo(CallbackInfo, CldApi.CF_OPERATION_TYPE.CF_OPERATION_TYPE_TRANSFER_PLACEHOLDERS);
			var cancelFetch = _syncContext.ServerProvider.Status != ServerProviderStatus.Connected;

			Log.Debug($"opInfo.SyncStatus: {opInfo.SyncStatus.ToString()}");

			if (!cancelFetch)
			{
				var processInfo = Marshal.PtrToStructure<CldApi.CF_PROCESS_INFO>(CallbackInfo.ProcessInfo);
				foreach (var process in ExcludedProcessesForFetchPlaceholders)
				{
					if (Regex.IsMatch(processInfo.CommandLine, process))
					{
						Log.Debug($"Fetch placeholders triggered by excluded App: {processInfo.ImagePath}");
						cancelFetch = true;
						break;
					}
				}
			}

			if (cancelFetch)
			{
				CldApi.CF_OPERATION_PARAMETERS.TRANSFERPLACEHOLDERS tpParam = new()
				{
					PlaceholderArray      = IntPtr.Zero,
					Flags                 = CldApi.CF_OPERATION_TRANSFER_PLACEHOLDERS_FLAGS.CF_OPERATION_TRANSFER_PLACEHOLDERS_FLAG_NONE,
					PlaceholderCount      = 0,
					PlaceholderTotalCount = 0,
					CompletionStatus      = new NTStatus((uint)CloudFilterAPI.NtStatus.STATUS_CLOUD_FILE_NETWORK_UNAVAILABLE)
				};

				var opParams      = CldApi.CF_OPERATION_PARAMETERS.Create(tpParam);
				var executeResult = CldApi.CfExecute(opInfo, ref opParams);

				Log.Debug($"Fetch execution result: {executeResult.ToString()}");

				return;
			}

			var relativePath = GetRelativePath(CallbackInfo);
			//var fullPath = GetLocalFullPath(CallbackInfo);

			CancellationTokenSource ctx = new();

			_fetchPlaceholdersCancellationTokens.AddOrUpdate(relativePath, ctx, (k, v) =>
			{
				v.Cancel();
				return ctx;
			});

			FetchPlaceholdersInternal(relativePath, opInfo, CallbackParameters.FetchPlaceholders.Pattern, ctx.Token);
		}
		catch (Exception ex)
		{
			Log.Error($"Exception caught: {ex.Message}");
		}
		finally
		{
			ExitCallback();
		}
	}
	
	
	/// <summary>
	///     Transfers placeholders for the specified directory or file from the cloud storage provider to the local system.
	///     This method ensures placeholders are created locally based on the server content and manages cancellation
	///     and error handling during the operation.
	/// </summary>
	/// <param name="relativePath">
	///     The relative path within the local filesystem that corresponds to the directory or file being processed.
	/// </param>
	/// <param name="opInfo">
	///     Information about the ongoing operation, such as the operation type and context required by the cloud filter API.
	/// </param>
	/// <param name="pattern">
	///     A string pattern used to filter placeholders listed and fetched from the server.
	/// </param>
	/// <param name="cancellationToken">
	///     A token to monitor for cancellation requests, ensuring the operation can be gracefully terminated if necessary.
	/// </param>
	private async void FetchPlaceholdersInternal(string relativePath, CldApi.CF_OPERATION_INFO opInfo, string pattern, CancellationToken cancellationToken)
	{
		try
		{
			Log.Debug($"Fetching placeholders for path \"{relativePath}\" ...");

			/* Allocate a placeholder list and a list to hold identity pointers. */
			//var fullPath         = GetLocalFullPath(relativePath);
			var infos            = new CloudFilterAPI.SafeHandlers.SafePlaceHolderList();
			var identityList     = new List<StringPtr>();
			var completionStatus = CloudFilterAPI.NtStatus.STATUS_SUCCESS;

			/* Get the server file list. */
			var getServerFileListResult = await GetServerFileListAsync(relativePath, cancellationToken);
			if (!getServerFileListResult.Succeeded)
			{
				completionStatus = getServerFileListResult.Status;
			}
			else
			{
				if (getServerFileListResult.Data != null)
				{
					/* Build placeholder info and collect StringPtr identities. */
					foreach (var placeholder in getServerFileListResult.Data)
					{
						var (placeholderInfo, identity) = CloudFilterAPI.CreatePlaceholderInfo(placeholder);
						if (identity != null)
						{
							identityList.Add(identity);
						}

						infos.Add(placeholderInfo);
						if (cancellationToken.IsCancellationRequested)
						{
							return;
						}
					}
				}
			}

			/* Directories that are not cloud files should not throw an exception. */
			if (completionStatus == CloudFilterAPI.NtStatus.STATUS_NOT_A_CLOUD_FILE)
			{
				completionStatus = CloudFilterAPI.NtStatus.STATUS_SUCCESS;
			}

			var total = (long)infos.Count;
			
			Log.Debug($"[CF_OPERATION_INFO ConnectionKey={opInfo.ConnectionKey}, CorrelationVector={opInfo.CorrelationVector}, RequestKey={opInfo.RequestKey}, StructSize={opInfo.StructSize}, SyncStatus={opInfo.SyncStatus}, TransferKey={opInfo.TransferKey}, Type={opInfo.Type}]");
			Log.Debug($"infos.Count: {total}");

			if (total == 0)
			{
				/* If no placeholders, call CfExecute with a NULL array. */
				var tp = new CldApi.CF_OPERATION_PARAMETERS.TRANSFERPLACEHOLDERS
				{
					PlaceholderArray      = IntPtr.Zero,
					Flags                 = CldApi.CF_OPERATION_TRANSFER_PLACEHOLDERS_FLAGS.CF_OPERATION_TRANSFER_PLACEHOLDERS_FLAG_DISABLE_ON_DEMAND_POPULATION,
					PlaceholderCount      = 0,
					PlaceholderTotalCount = 0,
					CompletionStatus      = new NTStatus((uint)completionStatus)
				};

				var opParams = CldApi.CF_OPERATION_PARAMETERS.Create(tp);
				var result   = CldApi.CfExecute(opInfo, ref opParams);

				Log.Debug($"CfExecute result: {result}");

				/* Ensure identities aren’t collected before CfExecute finishes. */
				GC.KeepAlive(identityList);

				/* We’re not storing this list, so just clear the managed references. */
				identityList.Clear();
				infos.Dispose();

				_fetchPlaceholdersCancellationTokens.TryRemove(relativePath, out _);
			}
			else
			{
				/* Otherwise, call CfExecute with the populated placeholder list. */
				var tp = new CldApi.CF_OPERATION_PARAMETERS.TRANSFERPLACEHOLDERS
				{
					PlaceholderArray      = infos,
					Flags                 = CldApi.CF_OPERATION_TRANSFER_PLACEHOLDERS_FLAGS.CF_OPERATION_TRANSFER_PLACEHOLDERS_FLAG_DISABLE_ON_DEMAND_POPULATION,
					PlaceholderCount      = (uint)total,
					PlaceholderTotalCount = total,
					CompletionStatus      = new NTStatus((uint)completionStatus)
				};

				var opParams      = CldApi.CF_OPERATION_PARAMETERS.Create(tp);
				var executeResult = CldApi.CfExecute(opInfo, ref opParams);

				Log.Debug($"Fetch execution result: {executeResult.ToString()}");

				/* Ensure identities aren’t collected before CfExecute finishes. */
				GC.KeepAlive(identityList);

				/* Dispose any previous in-flight placeholder transfer for this path. */
				if (_inFlightPlaceholders.TryRemove(relativePath, out var oldTransfer))
				{
					oldTransfer.Dispose();
				}

				/* Store the current placeholder list and identity references so they stay alive. */
				var newTransfer = new InFlightPlaceholderTransfer(infos, identityList);
				_inFlightPlaceholders[relativePath] = newTransfer;

				/* Schedule the transfer to be disposed after a timeout. */
				_ = Task.Run(async () =>
				{
					await Task.Delay(PlaceholderRetentionTimeout, cancellationToken);
					if (_inFlightPlaceholders.TryRemove(relativePath, out var transfer))
					{
						transfer.Dispose();
					}
				}, cancellationToken);

				/* Remove the cancellation token entry now that the operation has finished. */
				_fetchPlaceholdersCancellationTokens.TryRemove(relativePath, out _);
			}
		}
		catch (Exception e)
		{
			Log.Error($"Exception caught: {e.Message}");
		}
	}


	/// <summary>
	///     Handles the cancellation of an ongoing "Fetch Placeholders" operation. This allows the process of
	///     transferring placeholders from the cloud to the local filesystem to be interrupted and properly terminated.
	/// </summary>
	/// <param name="CallbackInfo">
	///     Information about the cancellation callback, including details on the triggering process and context.
	/// </param>
	/// <param name="CallbackParameters">
	///     Parameters provided for the cancellation operation, potentially influencing the scope and behavior
	///     of the cancellation.
	/// </param>
	public void CANCEL_FETCH_PLACEHOLDERS(in CldApi.CF_CALLBACK_INFO CallbackInfo, in CldApi.CF_CALLBACK_PARAMETERS CallbackParameters)
	{
		if (!TryEnterCallback())
		{
			return;
		}

		try
		{
			Log.Debug($"Canceling fetching placeholders for path \"{CallbackInfo.NormalizedPath}\" ...");

			if (_fetchPlaceholdersCancellationTokens.TryRemove(GetRelativePath(CallbackInfo), out var ctx))
			{
				ctx.Cancel();
			}

			Log.Debug($"Placeholder fetching cancelled for path \"{CallbackInfo.NormalizedPath}\" ...");
		}
		catch (Exception ex)
		{
			Log.Error($"Exception caught: {ex.Message}");
		}
		finally
		{
			ExitCallback();
		}
	}


	/// <summary>
	///     Handles the "Fetch Data" callback operation triggered by the cloud filter driver. This operation
	///     manages the process of transferring specific file data from the cloud storage to the local filesystem
	///     based on the request parameters, with support for asynchronous execution and conditions such as offsets
	///     and required data lengths.
	/// </summary>
	/// <param name="CallbackInfo">
	///     Provides contextual information about the callback, including the connection key, transfer key,
	///     file size, and the identity of the file being accessed.
	/// </param>
	/// <param name="CallbackParameters">
	///     Includes specific parameters for the data fetch operation, such as the required file offset
	///     and the length of data to be retrieved.
	/// </param>
	public void FETCH_DATA(in CldApi.CF_CALLBACK_INFO CallbackInfo, in CldApi.CF_CALLBACK_PARAMETERS CallbackParameters)
	{
		if (!TryEnterCallback())
		{
			/* Provider is shutting down - explicitly cancel this request. */
			var off = CallbackParameters.FetchData.RequiredFileOffset;
			TransferData(CallbackInfo.ConnectionKey, CallbackInfo.TransferKey, IntPtr.Zero, off, 0, NTStatus.STATUS_CANCELLED);
			return;
		}

		try
		{
			Log.Debug($"Fetching data for path \"{CallbackInfo.NormalizedPath}\" ...");

			var length        = CallbackParameters.FetchData.RequiredLength;
			var offset        = CallbackParameters.FetchData.RequiredFileOffset;
			var connectionKey = CallbackInfo.ConnectionKey;
			var transferKey   = CallbackInfo.TransferKey;
			var path          = GetCloudPath(CallbackInfo);
			var size          = CallbackInfo.FileSize;
			var id            = GetFetchID(CallbackInfo, CallbackParameters);
			var fileIdentity  = CloudFileIdentity.Deserialize(CallbackInfo.FileIdentity);
			var versionId     = fileIdentity?.VersionInfoID ?? fileIdentity?.VersionID;
			
			Task.Run(async () => { await FetchFile(id, connectionKey, transferKey, path, size, versionId, offset, length); });
		}
		catch (Exception ex)
		{
			Log.Error($"Exception caught: {ex.Message}");
		}
		finally
		{
			ExitCallback();
		}
	}


	/// <summary>
	///     Handles the "Cancel Fetch Data" callback operation triggered by the cloud filter driver.
	///     This operation cancels an ongoing fetch operation identified by a specific identifier.
	/// </summary>
	/// <param name="CallbackInfo">
	///     Information about the callback, such as the triggering process and context.
	/// </param>
	/// <param name="CallbackParameters">
	///     Parameters containing information used to locate and identify the ongoing fetch operation to be canceled.
	/// </param>
	public void CANCEL_FETCH_DATA(in CldApi.CF_CALLBACK_INFO CallbackInfo, in CldApi.CF_CALLBACK_PARAMETERS CallbackParameters)
	{
		if (!TryEnterCallback())
		{
			return;
		}

		try
		{
			Log.Debug($"Canceling fetch data for path \"{CallbackInfo.NormalizedPath}\" ...");
			var id = GetFetchID(CallbackInfo, CallbackParameters);
			CancelFetch(id);
		}
		catch (Exception ex)
		{
			Log.Error($"Exception caught: {ex.Message}");
		}
		finally
		{
			ExitCallback();
		}
	}


	/// <summary>
	///     Handles the "Notify File Open Completion" callback operation triggered by the cloud filter driver.
	///     This operation processes notifications when a file open request has completed, allowing the system
	///     to react to changes or state associated with the file.
	/// </summary>
	/// <param name="CallbackInfo">
	///     Information about the callback, such as details of the file and the context of the operation.
	/// </param>
	/// <param name="CallbackParameters">
	///     Parameters specifying additional details about the file open completion, including any relevant state or flags.
	/// </param>
	public void NOTIFY_FILE_OPEN_COMPLETION(in CldApi.CF_CALLBACK_INFO CallbackInfo, in CldApi.CF_CALLBACK_PARAMETERS CallbackParameters)
	{
		if (!TryEnterCallback())
		{
			return;
		}

		try
		{
			Log.Debug($"Notifying file open completion for path \"{CallbackInfo.NormalizedPath}\" ...");
		}
		catch (Exception ex)
		{
			Log.Error($"Exception caught: {ex.Message}");
		}
		finally
		{
			ExitCallback();
		}
	}


	/// <summary>
	///     Handles the "Notify File Close Completion" callback operation triggered by the cloud filter driver.
	///     This operation is invoked to notify that a file close operation has been completed, enabling the synchronization process to
	///     handle this event.
	/// </summary>
	/// <param name="CallbackInfo">
	///     Contains metadata about the callback, including the normalized path and triggering process context.
	/// </param>
	/// <param name="CallbackParameters">
	///     Provides additional parameters for the event, detailing information about the file close operation.
	/// </param>
	public void NOTIFY_FILE_CLOSE_COMPLETION(in CldApi.CF_CALLBACK_INFO CallbackInfo, in CldApi.CF_CALLBACK_PARAMETERS CallbackParameters)
	{
		if (!TryEnterCallback())
		{
			return;
		}

		try
		{
			Log.Debug($"Notifying file close completion for path \"{CallbackInfo.NormalizedPath}\" ...");
		}
		catch (Exception ex)
		{
			Log.Error($"Exception caught: {ex.Message}");
		}
		finally
		{
			ExitCallback();
		}
	}


	/// <summary>
	///     Handles the "Notify Delete" callback operation triggered by the cloud filter driver. This manages
	///     the deletion process for files or directories in the cloud or local filesystem, handling both
	///     standard deletions and undelete requests.
	/// </summary>
	/// <param name="CallbackInfo">
	///     Information about the callback, including the connection and transfer keys, and other associated metadata.
	/// </param>
	/// <param name="CallbackParameters">
	///     Parameters for the delete operation, such as whether the target is a directory or an undelete request.
	/// </param>
	public void NOTIFY_DELETE(in CldApi.CF_CALLBACK_INFO CallbackInfo, in CldApi.CF_CALLBACK_PARAMETERS CallbackParameters)
	{
		if (!TryEnterCallback())
		{
			return;
		}

		Log.Debug($"Notifying delete for path \"{CallbackInfo.NormalizedPath}\" ...");
		
		try
		{
			var connectionKey = CallbackInfo.ConnectionKey;
			var transferKey = CallbackInfo.TransferKey;
			uint CF_CALLBACK_DELETE_FLAG_IS_UNDELETE = 2;

			if (((uint)CallbackParameters.Delete.Flags & CF_CALLBACK_DELETE_FLAG_IS_UNDELETE) != 0)
			{
				CompleteDelete(connectionKey, transferKey, NTStatus.STATUS_SUCCESS);
				return;
			}

			var path = GetLocalFullPath(CallbackInfo);
			Task.Run(async () => { await DeleteFile(connectionKey, transferKey, path); });
		}
		catch (Exception ex)
		{
			Log.Error($"Exception caught: {ex.Message}");
		}
		finally
		{
			ExitCallback();
		}
	}


	/// <summary>
	///     Handles the completion notification when a file or folder deletion operation is performed. This operation ensures
	///     that files requiring restoration are properly restored if there are no permissions for deletion.
	/// </summary>
	/// <param name="CallbackInfo">
	///     Contains information about the operation's context and the triggering process related to the deletion event.
	/// </param>
	/// <param name="CallbackParameters">
	///     Provides parameters related to the delete completion, including details of the affected files or folders.
	/// </param>
	public void NOTIFY_DELETE_COMPLETION(in CldApi.CF_CALLBACK_INFO CallbackInfo, in CldApi.CF_CALLBACK_PARAMETERS CallbackParameters)
	{
		if (!TryEnterCallback())
		{
			return;
		}

		Log.Debug($"Notifying delete completion for path \"{CallbackInfo.NormalizedPath}\" ...");
		
		try
		{
			var path = GetCloudPath(CallbackInfo);
			if (!_filesToRestore.Contains(path))
			{
				return;
			}

			_filesToRestore.Remove(path);

			/* Restore the deleted file if there is no delete permission. */
			Task.Run(async () =>
			{
				try
				{
					var fileInfo = await _fileService.GetFileOrFolderInfo(TokenStorage.TeamID, path) ?? throw new InvalidOperationException();
					CreatePlaceHolder(fileInfo.File, StringHelper.GetParentPath(path));
				}
				catch
				{
					/* ignore exception */
				}
			});
		}
		catch (Exception ex)
		{
			Log.Error($"Exception caught: {ex.Message}");
		}
		finally
		{
			ExitCallback();
		}
	}


	/// <summary>
	///     Handles the "Notify Rename" callback operation triggered by the cloud filter driver. This manages the
	///     rename or move operation for cloud-managed files and directories, ensuring synchronization between
	///     local and cloud storage.
	/// </summary>
	/// <param name="CallbackInfo">
	///     Information about the callback, including the connection and transfer details, as well as the
	///     normalized path of the source item.
	/// </param>
	/// <param name="CallbackParameters">
	///     Parameters for the operation, encompassing details about the rename or move, such as the target path
	///     and whether the source is a directory.
	/// </param>
	public void NOTIFY_RENAME(in CldApi.CF_CALLBACK_INFO CallbackInfo, in CldApi.CF_CALLBACK_PARAMETERS CallbackParameters)
	{
		if (!TryEnterCallback())
		{
			return;
		}

		Log.Debug($"Notifying rename for path \"{CallbackInfo.NormalizedPath}\" ...");
		
		try
		{
			var connectionKey = CallbackInfo.ConnectionKey;
			var transferKey   = CallbackInfo.TransferKey;
			var path          = GetLocalFullPath(CallbackInfo);
			var isLocalTarget = !CallbackParameters.Rename.TargetPath.StartsWith(_syncContext.LocalRootFolderNormalized, StringComparison.CurrentCultureIgnoreCase);

			if (isLocalTarget)
			{
				Task.Run(async () => { await MoveFileToLocalFolder(connectionKey, transferKey, path); });
				return;
			}

			var targetPath = GetLocalFullPath(CallbackParameters.Rename);
			var isRename = Path.GetDirectoryName(path) == Path.GetDirectoryName(targetPath);

			Task.Run(async () =>
			{
				if (isRename)
				{
					await RenameFile(connectionKey, transferKey, path, targetPath);
				}
				else
				{
					await MoveFile(connectionKey, transferKey, path, targetPath);
				}
			});
		}
		catch (Exception ex)
		{
			Log.Error($"Exception caught: {ex.Message}");
		}
		finally
		{
			ExitCallback();
		}
	}


	/// <summary>
	///     Handles the "Notify Rename Completion" callback operation invoked by the cloud filter driver.
	///     This operation occurs after a successful rename operation, enabling synchronization
	///     between the local file system and the cloud storage.
	/// </summary>
	/// <param name="CallbackInfo">
	///     Information about the callback, including details about the context and initiating process.
	/// </param>
	/// <param name="CallbackParameters">
	///     Parameters for the rename operation, including the source and target paths,
	///     and additional flags associated with the rename action.
	/// </param>
	public void NOTIFY_RENAME_COMPLETION(in CldApi.CF_CALLBACK_INFO CallbackInfo, in CldApi.CF_CALLBACK_PARAMETERS CallbackParameters)
	{
		if (!TryEnterCallback())
		{
			return;
		}

		Log.Debug($"Notifying rename completion for path \"{CallbackInfo.NormalizedPath}\" ...");
		
		try
		{
			var isLocalTarget = !CallbackParameters.Rename.TargetPath.StartsWith(_syncContext.LocalRootFolderNormalized, StringComparison.CurrentCultureIgnoreCase);
			if (isLocalTarget)
			{
				return;
			}

			bool isDirectory;
			var localPath = GetLocalFullPath(CallbackInfo);

			try
			{
				isDirectory = Directory.Exists(localPath);
			}
			catch (Exception)
			{
				isDirectory = false;
			}

			var path = GetCloudPath(CallbackInfo, CallbackInfo.NormalizedPath);
			if (isDirectory && !path.EndsWith('/'))
			{
				path += '/';
			}

			Task.Run(async () =>
			{
				try
				{
					var fileInfo         = await _fileService.GetFileOrFolderInfo(TokenStorage.TeamID, path) ?? throw new InvalidOperationException();
					var placeHolder      = new Placeholder(fileInfo.File, StringHelper.GetParentPath(path));
					var localPlaceholder = new CloudFilterAPI.ExtendedPlaceholderState(localPath);
					
					localPlaceholder.UpdatePlaceholder(placeHolder, CldApi.CF_UPDATE_FLAGS.CF_UPDATE_FLAG_MARK_IN_SYNC);
				}
				catch
				{
					/* ignore exception */
				}
			});
		}
		catch (Exception ex)
		{
			Log.Error($"Exception caught: {ex.Message}");
		}
		finally
		{
			ExitCallback();
		}
	}

	#endregion

	// --------------------------------------------------------------------------------------------
	// PRIVATE METHODS
	// --------------------------------------------------------------------------------------------
	#region PRIVATE METHODS

	/// <summary>
	///     Determines whether a given file system event is considered unwanted based on specific conditions,
	///     such as being triggered by directory access or file attributes.
	/// </summary>
	/// <param name="e">
	///     The file system event arguments containing details of the event, such as the path and type of change.
	/// </param>
	/// <returns>
	///     True if the event is deemed unwanted based on the criteria, otherwise false.
	/// </returns>
	private bool IsUnwantedEvent(FileSystemEventArgs e)
	{
		/* Check if the event is triggered by a directory access. */
		/* Additional checks can be added here if needed. */
		if (Directory.Exists(e.FullPath))
		{
			return true;
		}

		/* Check if the event is triggered by a file attribute change. */
		var attributes = File.GetAttributes(e.FullPath);
		if ((attributes & FileAttributes.Hidden) == FileAttributes.Hidden)
		{
			return true;
		}

		/* Add more conditions as needed to filter out unwanted events. */

		return false;
	}


	/// <summary>
	///     Determines whether the specified directory is empty by checking for the presence of any files or subdirectories.
	/// </summary>
	/// <param name="path">
	///     The path of the directory to be checked.
	/// </param>
	/// <returns>
	///     A boolean value indicating whether the directory is empty. Returns true if the directory contains no files or subdirectories,
	///     otherwise false.
	/// </returns>
	private static bool IsDirectoryEmpty(string path)
	{
		// Check if the directory is empty
		return !Directory.EnumerateFileSystemEntries(path).Any();
	}


	/// <summary>
	///     Compares the folder structures of two directories to determine if they are equivalent.
	///     This method checks if all files and subdirectories in the source folder match those
	///     in the target folder both in structure and content.
	/// </summary>
	/// <param name="sourceFolder">
	///     The path to the source folder whose structure is used for comparison.
	/// </param>
	/// <param name="currentCreatedFolder">
	///     The path to the current folder that is being compared to the source folder.
	/// </param>
	/// <returns>
	///     Returns true if the folder structures are identical, otherwise false.
	/// </returns>
	private static bool CompareFolderStructures(string sourceFolder, string currentCreatedFolder)
	{
		var folder1Structure = GetFolderStructure(sourceFolder);
		var folder2Structure = GetFolderStructure(currentCreatedFolder);

		return CompareStructures(folder1Structure, folder2Structure);
	}


	/// <summary>
	///     Retrieves the folder structure of a given directory by recursively scanning for all files and subdirectories.
	///     The result represents relative paths of files and directories within the base folder.
	/// </summary>
	/// <param name="folderPath">
	///     The path of the folder for which the structure should be fetched.
	/// </param>
	/// <returns>
	///     A hash set containing relative paths of all files and directories within the specified folder.
	/// </returns>
	private static HashSet<string> GetFolderStructure(string folderPath)
	{
		var structure = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var file in Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories))
		{
			structure.Add(file.Substring(folderPath.Length).TrimStart(Path.DirectorySeparatorChar));
		}

		foreach (var dir in Directory.GetDirectories(folderPath, "*", SearchOption.AllDirectories))
		{
			structure.Add(dir.Substring(folderPath.Length).TrimStart(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
		}

		return structure;
	}


	/// <summary>
	///     Compares two folder structures represented as sets of strings to determine if they are identical.
	///     The comparison checks for equality in both the number and content of the folders/files within the structures.
	/// </summary>
	/// <param name="structure1">
	///     The first folder structure to compare, represented as a set of strings.
	/// </param>
	/// <param name="structure2">
	///     The second folder structure to compare, represented as a set of strings.
	/// </param>
	/// <returns>
	///     True if both folder structures are identical; otherwise, false.
	/// </returns>
	private static bool CompareStructures(HashSet<string> structure1, HashSet<string> structure2)
	{
		if (structure1.Count != structure2.Count)
		{
			return false;
		}

		foreach (var item in structure1)
		{
			if (!structure2.Contains(item))
			{
				return false;
			}
		}

		return true;
	}
	
	#endregion
	
	// --------------------------------------------------------------------------------------------
	// HANDLERS
	// --------------------------------------------------------------------------------------------
	#region HANDLERS

	/// <summary>
	///     Handles the "Error" event triggered by the FileSystemWatcher. This method manages
	///     the error recovery process by introducing a delay and initiating synchronization
	///     to recover from potential errors during file monitoring.
	/// </summary>
	/// <param name="sender">
	///     The source of the event, typically the FileSystemWatcher instance that encountered the error.
	/// </param>
	/// <param name="e">
	///     Provides data for the error event, including details about the exception that occurred.
	/// </param>
	private async void OnFileSystemWatcherError(object sender, ErrorEventArgs e)
	{
		Log.Warn("FileSystemWatcher Error: " + e.GetException().Message);

		await Task.Delay(2000, _globalShutDownToken).ConfigureAwait(false);
		SyncDataAsync(SyncMode.Local, _globalShutDownToken).FireAndForget("SyncDataAsync(Local)");
	}
	
	
	/// <summary>
	///     Handles the "Created" event from a FileSystemWatcher, which is triggered when a new file or directory is detected in the
	///     monitored folder.
	///     Analyzes the created entity to determine whether it is a new file or a moved placeholder and processes it accordingly.
	/// </summary>
	/// <param name="sender">
	///     The source of the event, typically the FileSystemWatcher instance.
	/// </param>
	/// <param name="e">
	///     Provides data about the created file or directory, including its path and change type.
	/// </param>
	private void OnFileSystemWatcherCreated(object sender, FileSystemEventArgs e)
	{
		try
		{
			using CloudFilterAPI.ExtendedPlaceholderState localPlaceHolder = new(e.FullPath);
			var isMovedFile = localPlaceHolder.IsPlaceholder;
			
			/* It's a new file. */
			if (!isMovedFile)
			{
				Task.Run(async () =>
				{
					try
					{
						var isDirectory = File.GetAttributes(e.FullPath).HasFlag(FileAttributes.Directory);
						if (isDirectory)
						{
							await _fileOperations!.CreateFolder(e.FullPath);
						}
						else
						{
							await _fileOperations!.UploadFile(e.FullPath);
						}
					}
					catch (Exception ex)
					{
						Log.Error($"Exception caught: {e.FullPath}: {ex.Message}");
					}
				});
			}
		}
		catch (Exception ex)
		{
			/* Handle exceptions (e.g., file not found, access denied, etc.) */
			Log.Error($"Exception caught: Error checking path: {ex.Message}");
		}
	}
	
	
	/// <summary>
	///     Handles "Changed" events raised by the <see cref="FileSystemWatcher" />. Filters out unwanted events
	///     and adds valid file change notifications to the local change queue for synchronization processing.
	/// </summary>
	/// <param name="sender">
	///     The source of the event, typically the <see cref="FileSystemWatcher" /> that detected the file system change.
	/// </param>
	/// <param name="e">
	///     The event data, including information about the change, such as the full path of the affected file or directory.
	/// </param>
	private void OnFileSystemWatcherChanged(object sender, FileSystemEventArgs e)
	{
	}

	#endregion
}
