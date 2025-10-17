using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RakutenDrive.Controllers.CloudFilterAPIControllers;
using RakutenDrive.Models;
using RakutenDrive.Services;
using RakutenDrive.Utils;
using RakutenDrive.Utils.CredentialManagement;


namespace RakutenDrive.Controllers.Providers.ServerProvider;

/// <summary>
///     Represents a provider for managing server-based file interactions within the application.
/// </summary>
[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
public sealed partial class ServerProvider : IServerFileProvider
{
	// --------------------------------------------------------------------------------------------
	// INIT
	// --------------------------------------------------------------------------------------------
	#region INIT

	/// Provides methods and settings for managing synchronization with a server.
	/// This class is responsible for configuring, initializing, and handling server provider
	/// parameters and settings required for syncing files with a remote server.
	/// Implements the IServerFileProvider interface for interaction with server-based storage.
	public ServerProvider(string ServerPath)
	{
		_parameter = new ServerProviderParams { ServerPath = ServerPath, UseRecycleBin = true, UseRecycleBinForChangedFiles = true, UseTempFilesForUpload = false };
		_preferredServerProviderSettings = new PreferredSettings { AllowPartialUpdate = true, MaxChunkSize = int.MaxValue, MinChunkSize = 4096, PreferFullDirSync = true };
		_connectionTimer = new Timer(ConnectionTimerCallback, null, Timeout.Infinite, Timeout.Infinite);
		_fullResyncTimer = new Timer(FullResyncTimerCallback, null, Timeout.Infinite, Timeout.Infinite);
	}

	#endregion

	// --------------------------------------------------------------------------------------------
	// NESTED CLASSES
	// --------------------------------------------------------------------------------------------


	/// <summary>
	///     Encapsulates the parameters required for configuring and initializing a server provider.
	/// </summary>
	public class ServerProviderParams
	{
		public string? ServerPath;
		public bool UseRecycleBin;
		public bool UseRecycleBinForChangedFiles;
		public bool UseTempFilesForUpload;
	}


	/// <summary>
	///     Represents an internal implementation for asynchronously reading files from a server-based file system.
	/// </summary>
	/// <remarks>
	///     This class is responsible for handling file reading operations such as opening, reading from, and closing files asynchronously.
	/// </remarks>
	internal sealed class ReadFileAsyncInternal : IReadFileAsync
	{
		private readonly ServerProvider _provider;
		private bool _disposedValue;
		private FileStream? _fileStream;
		private bool _isClosed;
		private OpenAsyncParams? _openAsyncParams;


		/// Represents an internal implementation for asynchronously reading files from a server-based file system.
		/// This class manages asynchronous operations for opening, reading, and closing files, providing
		/// efficient file access and ensuring proper resource management during file operations.
		public ReadFileAsyncInternal(ServerProvider provider)
		{
			_provider = provider;
		}


		/// Opens an asynchronous read operation for a specified file with the provided parameters.
		/// This method attempts to locate and open the specified file for reading based on the
		/// relative file path provided in the parameters. If the file does not exist or the server
		/// is unavailable, it returns a result indicating the issue.
		/// <param name="e">An object containing parameters used to specify the relative path of the file to be opened.</param>
		/// <returns>
		///     A task representing the asynchronous operation, containing the result of the file open process,
		///     which includes information about success, failure, or exceptions encountered.
		/// </returns>
		public Task<ReadFileOpenResult> OpenAsync(OpenAsyncParams e)
		{
			if (!_provider.CheckProviderStatus())
			{
				return Task.FromResult(new ReadFileOpenResult(CloudFilterAPI.NtStatus.STATUS_CLOUD_FILE_NETWORK_UNAVAILABLE));
			}

			_openAsyncParams = e;
			ReadFileOpenResult openResult = new();

			if (_provider._parameter.ServerPath == null || e.RelativeFileName == null)
			{
				throw new FileNotFoundException(e.RelativeFileName);
			}

			var fullPath = Path.Combine(_provider._parameter.ServerPath, e.RelativeFileName);

			// Simulate "Offline" if Serverfolder not found.
			if (!Directory.Exists(_provider._parameter.ServerPath))
			{
				openResult.SetException(CloudExceptions.Offline);
				goto skip;
			}

			try
			{
				if (!File.Exists(fullPath))
				{
					throw new FileNotFoundException(e.RelativeFileName);
				}

				_fileStream = File.OpenRead(fullPath);
				openResult.Placeholder = new Placeholder(fullPath);
			}
			catch (Exception ex)
			{
				openResult.SetException(ex);
			}

			skip:
			return Task.FromResult(openResult);
		}


		/// Asynchronously reads data from a file at a specified offset and writes it into a buffer.
		/// This method ensures that the file is read in a thread-safe manner and handles potential
		/// exceptions during the file reading process.
		/// <param name="buffer">The buffer into which the data will be read.</param>
		/// <param name="offsetBuffer">The byte offset in the buffer at which to begin writing the data.</param>
		/// <param name="offset">The position in the file from which the reading starts.</param>
		/// <param name="count">The maximum number of bytes to read from the file.</param>
		/// <returns>
		///     A task that represents the asynchronous read operation. The task result contains
		///     a <see cref="ReadFileReadResult" /> object with details about the read operation and the number of bytes read.
		/// </returns>
		public async Task<ReadFileReadResult> ReadAsync(byte[] buffer, int offsetBuffer, long offset, int count)
		{
			if (!_provider.CheckProviderStatus())
			{
				return new ReadFileReadResult(CloudFilterAPI.NtStatus.STATUS_CLOUD_FILE_NETWORK_UNAVAILABLE);
			}

			ReadFileReadResult readResult = new();

			if (_fileStream == null || _openAsyncParams == null)
			{
				Log.Warn("FileStream or OpenAsyncParams is null.");
				return readResult;
			}

			try
			{
				_fileStream.Position = offset;
				readResult.BytesRead = await _fileStream.ReadAsync(buffer, offsetBuffer, count, _openAsyncParams.CancellationToken);
			}
			catch (Exception ex)
			{
				readResult.SetException(ex);
			}

			return readResult;
		}


		/// Asynchronously closes a file that was previously opened for reading from a server-based file system.
		/// Ensures proper resource cleanup and sets the result of the file close operation.
		/// <return>
		///     Returns a Task containing a ReadFileCloseResult object, which encapsulates the result of the close operation,
		///     including status or error details in case of failure.
		/// </return>
		public Task<ReadFileCloseResult> CloseAsync()
		{
			ReadFileCloseResult closeResult = new();

			if (_fileStream == null)
			{
				Log.Warn("FileStream is null.");
				return Task.FromResult(closeResult);
			}
			
			try
			{
				_fileStream.Close();
				_isClosed = true;
			}
			catch (Exception ex)
			{
				closeResult.SetException(ex);
			}

			return Task.FromResult(closeResult);
		}


		// // TODO: Finalizer nur überschreiben, wenn "Dispose(bool disposing)" Code für die Freigabe nicht verwalteter Ressourcen enthält
		// ~FetchServerFileAsync()
		// {
		//     // Ändern Sie diesen Code nicht. Fügen Sie Bereinigungscode in der Methode "Dispose(bool disposing)" ein.
		//     Dispose(disposing: false);
		// }


		/// Releases all resources used by the current instance of the class.
		/// This method is called to clean up both managed and unmanaged resources.
		/// It implements the IDisposable interface and ensures proper disposal of internal fields
		/// while also preventing the finalizer from being called by invoking GC.SuppressFinalize.
		public void Dispose()
		{
			// Ändern Sie diesen Code nicht. Fügen Sie Bereinigungscode in der Methode "Dispose(bool disposing)" ein.
			Dispose(true);
			GC.SuppressFinalize(this);
		}


		public async ValueTask DisposeAsync()
		{
			await DisposeAsyncCore();

			Dispose(false);
			GC.SuppressFinalize(this);
		}


		/// Releases the resources used by the current instance of the class.
		/// This method implements the IDisposable interface and ensures proper
		/// cleanup of unmanaged resources or any internal fields.
		private void Dispose(bool disposing)
		{
			if (!_disposedValue)
			{
				if (disposing)
				{
					try
					{
						if (!_isClosed)
						{
							_isClosed = true;
							_fileStream?.Flush();
							_fileStream?.Close();
						}
					}
					finally
					{
						_fileStream?.Dispose();
					}
				}

				_disposedValue = true;
			}
		}


		/// Performs the core logic to asynchronously release resources used by the object.
		/// Ensures proper disposal of unmanaged resources and handles file system clean-up tasks.
		/// This method is invoked as part of the asynchronous disposal pattern to support
		/// safe and effective memory and resource management.
		private async ValueTask DisposeAsyncCore()
		{
			try
			{
				if (_fileStream != null && !_isClosed)
				{
					_isClosed = true;
					await _fileStream.FlushAsync();
					_fileStream?.Close();
				}
			}
			finally
			{
				_fileStream?.Dispose();
			}
		}
	}


	/// <summary>
	///     Provides an internal implementation of the IWriteFileAsync interface to handle asynchronous
	///     file operations, such as opening, writing, and closing files, for the ServerProvider.
	/// </summary>
	/// <remarks>
	///     This class facilitates file upload operations by supporting various upload modes,
	///     including full file uploads and partial updates. It also manages cleanup and disposal
	///     operations to ensure resource management during asynchronous interactions.
	/// </remarks>
	internal sealed class WriteFileAsyncInternal : IWriteFileAsync
	{
		private readonly ServerProvider _provider;
		private bool _disposedValue;
		private FileStream _fileStream;
		private string _fullPath;
		private bool _isClosed;
		private OpenAsyncParams _param;
		private string _tempFile;


		/// Provides an internal implementation of the IWriteFileAsync interface for handling asynchronous
		/// file write operations for the ServerProvider. This class is used to manage the process of writing
		/// files to the server, including opening, writing data buffers, and closing files.
		/// Designed to handle various upload modes (e.g., full file uploads and partial updates) and ensure
		/// proper resource cleanup during asynchronous tasks.
		public WriteFileAsyncInternal(ServerProvider provider)
		{
			_provider = provider;
		}


		public UploadMode SupportedUploadModes =>
			// Resume currently not implemented (Verification of file integrity not implemented)
			UploadMode.FullFile | UploadMode.PartialUpdate;


		/// Asynchronously opens a file for writing using the provided parameters.
		/// This method is used to initialize the writing process, prepare the target file,
		/// and set up necessary resources for subsequent file operations.
		/// <param name="e">Parameters required for opening a file, including upload mode and other details.</param>
		/// <returns>A task that represents the asynchronous operation, containing the result of the file open operation.</returns>
		public Task<WriteFileOpenResult> OpenAsync(OpenAsyncParams e)
		{
			if (!_provider.CheckProviderStatus())
			{
				return Task.FromResult(new WriteFileOpenResult(CloudFilterAPI.NtStatus.STATUS_CLOUD_FILE_NETWORK_UNAVAILABLE));
			}

			_param = e;

			WriteFileOpenResult openResult = new();

			// PartialUpdate is done In-Place without temp file.
			if (e.mode == UploadMode.PartialUpdate)
			{
				_provider._parameter.UseTempFilesForUpload = false;
			}

			try
			{
				_fullPath = Path.Combine(_provider._parameter.ServerPath, _param.RelativeFileName);

				if (!Directory.Exists(Path.GetDirectoryName(_fullPath)))
				{
					Directory.CreateDirectory(Path.GetDirectoryName(_fullPath));
				}

				_tempFile = Path.GetDirectoryName(_fullPath) + @"\$_" + Path.GetFileName(_fullPath);

				var fileMode = _param.mode switch
				{
					UploadMode.FullFile => FileMode.Create,
					UploadMode.Resume => FileMode.Open,
					UploadMode.PartialUpdate => FileMode.Open,
					_ => FileMode.OpenOrCreate
				};

				// Resume currently not implemented (Verification of file integrity not implemented)

				if (_provider._parameter.UseTempFilesForUpload)
				{
					_fileStream = new FileStream(_tempFile, fileMode, FileAccess.Write, FileShare.None);
				}
				else
				{
					_fileStream = new FileStream(_fullPath, fileMode, FileAccess.Write, FileShare.None);
				}


				_fileStream.SetLength(e.FileInfo.FileSize);
				if (File.Exists(_fullPath))
				{
					openResult.Placeholder = new Placeholder(_fullPath);
				}
			}
			catch (Exception ex)
			{
				openResult.SetException(ex);
			}

			return Task.FromResult(openResult);
		}


		/// Writes data asynchronously to a file at the specified position and offset within the buffer.
		/// This method handles writing operations while allowing cancellation via the provided token in the parameters.
		/// <param name="buffer">The byte array containing the data to be written to the file.</param>
		/// <param name="offsetBuffer">The zero-based byte offset in the buffer at which to begin copying bytes to the file.</param>
		/// <param name="offset">The position in the file at which writing begins.</param>
		/// <param name="count">The number of bytes to write to the file.</param>
		/// <returns>A task that represents the asynchronous write operation. The task result contains a <see cref="WriteFileWriteResult" /> object representing the result of the operation, including any exceptions if encountered.</returns>
		public async Task<WriteFileWriteResult> WriteAsync(byte[] buffer, int offsetBuffer, long offset, int count)
		{
			WriteFileWriteResult writeResult = new();

			try
			{
				_fileStream.Position = offset;
				await _fileStream.WriteAsync(buffer, offsetBuffer, count, _param.CancellationToken);
			}
			catch (Exception ex)
			{
				writeResult.SetException(ex);
			}

			return writeResult;
		}


		/// Asynchronously closes the file write operation, finalizes file streaming, and commits any pending changes
		/// to the target upload storage. Additionally, updates file attributes and metadata depending on the operation
		/// parameters and ensures resources are properly disposed.
		/// This method processes finalization steps like clearing temporary files, setting custom file properties,
		/// and handling exceptions during the closing operation.
		/// <param name="isCompleted">
		///     Indicates whether the write operation is completed successfully. If true, takes actions
		///     such as finalizing the file or committing changes to the storage.
		/// </param>
		/// <return>
		///     Returns a WriteFileCloseResult object representing the outcome of the closing operation, including
		///     any resulting placeholders or exceptions encountered.
		/// </return>
		public async Task<WriteFileCloseResult> CloseAsync(bool isCompleted)
		{
			WriteFileCloseResult closeResult = new();

			try
			{
				await _fileStream.FlushAsync();
				_fileStream.Close();
				_isClosed = true;
				_fileStream.Dispose();

				var pFile = _fullPath;
				if (_provider._parameter.UseTempFilesForUpload)
				{
					pFile = _tempFile;
				}

				try
				{
					var att = _param.FileInfo.FileAttributes;
					att &= ~FileAttributes.ReadOnly;

					if (_param.FileInfo.FileAttributes > 0)
					{
						File.SetAttributes(pFile, att);
					}

					if (_param.FileInfo.CreationTime > DateTime.MinValue)
					{
						File.SetCreationTime(pFile, _param.FileInfo.CreationTime);
					}

					if (_param.FileInfo.LastAccessTime > DateTime.MinValue)
					{
						File.SetLastAccessTime(pFile, _param.FileInfo.LastAccessTime);
					}

					if (_param.FileInfo.LastWriteTime > DateTime.MinValue)
					{
						File.SetLastWriteTime(pFile, _param.FileInfo.LastWriteTime);
					}
				}
				catch (Exception ex)
				{
					Log.Error(ex.Message);
				}

				if (isCompleted)
				{
					if (_provider._parameter.UseTempFilesForUpload)
					{
						if (File.Exists(_fullPath))
						{
							if (_provider._parameter.UseRecycleBinForChangedFiles)
							{
								_provider.MoveToRecycleBin(_param.RelativeFileName, false);
							}
							else
							{
								File.Delete(_fullPath);
							}
						}

						File.Move(_tempFile, _fullPath);
					}

					closeResult.Placeholder = new Placeholder(_fullPath);
				}
			}
			catch (Exception ex)
			{
				closeResult.SetException(ex);
			}

			return closeResult;
		}


		// // TODO: Finalizer nur überschreiben, wenn "Dispose(bool disposing)" Code für die Freigabe nicht verwalteter Ressourcen enthält
		// ~FetchServerFileAsync()
		// {
		//     // Ändern Sie diesen Code nicht. Fügen Sie Bereinigungscode in der Methode "Dispose(bool disposing)" ein.
		//     Dispose(disposing: false);
		// }


		/// Releases the resources used by the ServerProvider.WriteFileAsyncInternal class.
		/// This includes both unmanaged and managed resources that the class may hold.
		/// Calling this method ensures cleanup of all resources, prevents memory leaks,
		/// and prepares the object for garbage collection. Once disposed, the object
		/// should not be used further.
		public void Dispose()
		{
			// Ändern Sie diesen Code nicht. Fügen Sie Bereinigungscode in der Methode "Dispose(bool disposing)" ein.
			Dispose(true);
			GC.SuppressFinalize(this);
		}


		/// Performs asynchronous cleanup actions to release unmanaged resources used during file operations.
		/// This method ensures proper cleanup and disposal of resources in an asynchronous manner, preventing
		/// resource leaks and maintaining efficient resource management during the lifecycle of file operations.
		/// <returns>A ValueTask that represents the asynchronous operation of cleaning up resources.</returns>
		public async ValueTask DisposeAsync()
		{
			await DisposeAsyncCore();

			Dispose(false);
			GC.SuppressFinalize(this);
		}


		/// Releases all resources used by the current instance of the class.
		/// Ensures cleanup operations for managed and unmanaged resources, preventing resource leaks.
		/// Overrides the IDisposable.Dispose method to execute necessary cleanup tasks before the object is disposed.
		/// <param name="disposing">A boolean value indicating whether the method is invoked directly or indirectly by a user's code or by the garbage collector. If true, managed resources are disposed. If false, only unmanaged resources are released.</param>
		private void Dispose(bool disposing)
		{
			if (!_disposedValue)
			{
				if (disposing)
				{
					try
					{
						if (!_isClosed)
						{
							_isClosed = true;

							_fileStream?.Flush();
							_fileStream?.Close();
						}
					}
					finally
					{
						_fileStream?.Dispose();
					}
				}

				_disposedValue = true;
			}
		}


		/// Provides an asynchronous cleanup mechanism for releasing unmanaged resources used during file operations.
		/// This method ensures that resources such as file streams are properly flushed, closed, and disposed
		/// in an asynchronous manner to prevent resource leaks during or after usage.
		/// <returns>A ValueTask representing the asynchronous dispose operation.</returns>
		private async ValueTask DisposeAsyncCore()
		{
			try
			{
				if (!_isClosed)
				{
					_isClosed = true;
					if (_fileStream != null)
					{
						await _fileStream.FlushAsync();
						_fileStream.Close();
					}
				}
			}
			finally
			{
				_fileStream?.Dispose();
			}
		}
	}


	/// <summary>
	///     Represents an internal asynchronous implementation for managing file listing operations
	///     within a server-based environment.
	/// </summary>
	internal sealed class FileListAsyncInternal : IFileListAsync
	{
		private readonly CancellationTokenSource _ctx = new();
		private readonly GenericResult _finalStatus = new();
		private readonly BlockingCollection<Placeholder> _infoList = new();
		private readonly ServerProvider _provider;
		private bool _closed;
		private bool _disposedValue;


		/// Represents an internal asynchronous implementation for handling file listing operations in a server-based environment.
		/// This class is used internally by the ServerProvider to interact with the server and manage file enumeration.
		/// It provides methods for opening a file list, retrieving file metadata sequentially, and closing the enumeration process.
		public FileListAsyncInternal(ServerProvider provider)
		{
			_provider = provider;
		}


		/// Opens a cloud file asynchronously, providing access to a file within the server provider's context.
		/// This method validates the provider's status, constructs the file path, and manages the opening of the file,
		/// ensuring that cancellation tokens and asynchronous operations are handled correctly.
		/// <param name="relativeFileName">The relative path of the file to be opened within the server's directory structure.</param>
		/// <param name="cancellationToken">A token to observe for cancellation requests, enabling the operation to be canceled if needed.</param>
		/// <returns>A task representing the asynchronous operation, containing a GenericResult indicating the success or failure of the operation.</returns>
		public Task<GenericResult> OpenAsync(string relativeFileName, CancellationToken cancellationToken)
		{
			if (!_provider.CheckProviderStatus())
			{
				return Task.FromResult(new GenericResult(CloudFilterAPI.NtStatus.STATUS_CLOUD_FILE_NETWORK_UNAVAILABLE));
			}

			var fullPath = Path.Combine(_provider._parameter.ServerPath, relativeFileName);
			var fileIdentity = CloudFilterAPI.GetCloudFileIdentity(fullPath.ConvertToLocalPath());
			var cloudPath = fileIdentity?.Path ?? (fullPath == string.Empty ? string.Empty : fullPath.Replace("\\", "/") + "/");

			cancellationToken.Register(() => { _ctx.Cancel(); });
			var tctx = _ctx.Token;

			/* DirectoryInfo directory = new(fullPath);

			if (!directory.Exists)
			{
				return Task.FromResult(new GenericResult(NtStatus.STATUS_NOT_A_CLOUD_FILE));
			} */

			Task.Run(async () =>
			{
				try
				{
					var payload = new
					{
						path = string.Empty,
						from = 0,
						to = 35,
						sort_type = "path",
						reverse = false,
						thumbnail_size = 130,
						hidden_shared_folder = false
					};
					
					var allTeamFiles = new List<FileItem>();
					var lastPage = false;
					var from = 0;
					var to = 35;
					var teamDriveSerives = new TeamDriveService();
					var jwtToken = TokenStorage.GetAccessToken();
					var jsonPayload = JWTParser.Parse(jwtToken);
					var teamId = jsonPayload.TeamID;
					var isNotFound = false;
					
					while (!lastPage)
					{
						payload = new
						{
							path = cloudPath,
							from,
							to,
							sort_type = "path",
							reverse = false,
							thumbnail_size = 130,
							hidden_shared_folder = false
						};
						
						var fileListResponse = await teamDriveSerives.GetTeamDriveFileList(teamId, payload);
						if (fileListResponse != null)
						{
							allTeamFiles.AddRange(fileListResponse.File);
							lastPage = fileListResponse.LastPage;
							from += 36; // Adjust the range for the next request
							to += 36;
						}
						else
						{
							lastPage = true;
							isNotFound = true;
						}
					}

					// mapping FileList
					if (!isNotFound)
					{
						foreach (var fileItem in allTeamFiles)
						{
							tctx.ThrowIfCancellationRequested();
							if (!fileItem.Path.StartsWith(@"$"))
							{
								_infoList.Add(new Placeholder(fileItem, cloudPath));
							}
						}
					}
				}
				catch (Exception ex)
				{
					_finalStatus.SetException(ex);
				}
				finally
				{
					_infoList.CompleteAdding();
				}
			}, _ctx.Token);


			// Open completed.... Itterating is running in Background.
			return Task.FromResult(new GenericResult());
		}


		/// Retrieves the next result asynchronously during a file listing operation.
		/// This method is responsible for fetching the next available item in the sequence
		/// and encapsulating the operation's status and any associated data in a GetNextResult object.
		/// Implements error handling and status verification to ensure reliable processing.
		public Task<GetNextResult> GetNextAsync()
		{
			return Task.Run(GetNextResult () =>
			{
				GetNextResult getNextResult = new();

				try
				{
					if (!_provider.CheckProviderStatus())
					{
						return new GetNextResult(CloudFilterAPI.NtStatus.STATUS_CLOUD_FILE_NETWORK_UNAVAILABLE);
					}

					if (_infoList.TryTake(out var item, -1, _ctx.Token))
					{
						// STATUS_SUCCESS = Data found
						getNextResult.Status = CloudFilterAPI.NtStatus.STATUS_SUCCESS;
						getNextResult.Placeholder = item;
					}
					else
					{
						// STATUS_UNSUCCESSFUL = No more Data available.
						getNextResult.Status = CloudFilterAPI.NtStatus.STATUS_UNSUCCESSFUL;
					}
				}
				catch (Exception ex)
				{
					getNextResult.SetException(ex);
					_finalStatus.SetException(ex);
				}

				return getNextResult;
			});
		}


		/// Asynchronously closes the ongoing operation, ensuring cancellation of tasks and completion
		/// of resource cleanup within the file listing context.
		/// Updates the final status to reflect the operation's state and marks the internal collection
		/// as complete, preventing further additions.
		/// <returns>
		///     A task representing the asynchronous close operation. The result contains the updated
		///     status of the operation encapsulated in a GenericResult instance.
		/// </returns>
		public Task<GenericResult> CloseAsync()
		{
			_ctx.Cancel();
			if (!_infoList.IsAddingCompleted)
			{
				_infoList.CompleteAdding();
				_finalStatus.Status = CloudFilterAPI.NtStatus.STATUS_CLOUD_FILE_REQUEST_ABORTED;
			}

			_closed = true;

			return Task.FromResult(_finalStatus);
		}


		// // TODO: Finalizer nur überschreiben, wenn "Dispose(bool disposing)" Code für die Freigabe nicht verwalteter Ressourcen enthält
		// ~FetchServerFileAsync()
		// {
		//     // Ändern Sie diesen Code nicht. Fügen Sie Bereinigungscode in der Methode "Dispose(bool disposing)" ein.
		//     Dispose(disposing: false);
		// }


		/// Releases all resources used by the current instance of the object.
		/// Invokes the disposal process to free both managed and unmanaged resources, preventing resource leaks.
		/// This method should be called when the object is no longer needed to ensure proper cleanup and resource management.
		public void Dispose()
		{
			// Ändern Sie diesen Code nicht. Fügen Sie Bereinigungscode in der Methode "Dispose(bool disposing)" ein.
			Dispose(true);
			GC.SuppressFinalize(this);
		}


		/// Asynchronously releases all resources used by the object.
		/// Executes necessary cleanup operations for managed and unmanaged resources
		/// associated with the object. Ensures proper cleanup to prevent resource leaks.
		public async ValueTask DisposeAsync()
		{
			await DisposeAsyncCore();

			Dispose(false);
			GC.SuppressFinalize(this);
		}


		/// Releases the resources used by the object.
		/// Cleans up both managed and unmanaged resources, ensuring proper disposal and preventing resource leaks.
		/// Should be called when the object is no longer required.
		/// <param name="disposing">
		///     If set to true, releases managed and unmanaged resources. If set to false, releases only unmanaged resources.
		/// </param>
		private void Dispose(bool disposing)
		{
			if (!_disposedValue)
			{
				if (disposing)
				{
					_ctx?.Cancel();
					_infoList?.Dispose();
				}

				_disposedValue = true;
			}
		}


		/// Handles the asynchronous disposal of internal resources used within file listing operations.
		/// Ensures that open server file enumerations are closed properly and associated resources are cleaned up.
		/// This method is called internally during the disposal process to release unmanaged and managed resources.
		private async ValueTask DisposeAsyncCore()
		{
			if (!_closed)
			{
				await CloseAsync();
			}

			_infoList?.Dispose();
		}
	}


	// --------------------------------------------------------------------------------------------
	// PROPERTIES
	// --------------------------------------------------------------------------------------------
	#region PROPERTIES

	public event EventHandler<ServerProviderStateChangedEventArgs> ServerProviderStateChanged;
	public event EventHandler<FileChangedEventArgs> FileChanged;

	public SyncContext SyncContext { get; set; }
	public PreferredSettings PreferredServerProviderSettings => _preferredServerProviderSettings;
	public ServerProviderStatus Status => _status;

	private readonly Timer _connectionTimer;
	private readonly Timer _fullResyncTimer;
	private readonly ServerProviderParams _parameter;
	private readonly PreferredSettings _preferredServerProviderSettings;
	private ServerProviderStatus _status = ServerProviderStatus.Disconnected;
	private ServerCallback _serverCallback;

	#endregion

	// --------------------------------------------------------------------------------------------
	// PUBLIC METHODS
	// --------------------------------------------------------------------------------------------
	#region PUBLIC METHODS

	/// Establishes a connection to the server using the specified parameters.
	/// This method handles the initialization and status updates required to
	/// connect to the server. It checks the current provider status and updates
	/// it to reflect the connection process.
	/// <return>
	///     Returns a GenericResult instance indicating the success or failure
	///     of the connection attempt. The result contains status information, which
	///     may indicate network unavailability or other issues during connection.
	/// </return>
	public GenericResult Connect()
	{
		GenericResult genericResult = new();

		// 2 ways
		/* if (serverCallback == null)
			serverCallback = new(this); */

		SetProviderStatus(ServerProviderStatus.Connecting);

		/*try
		{
			if (!Directory.Exists(Parameter.ServerPath)) { Directory.CreateDirectory(Parameter.ServerPath); };
		}
		catch (Exception) { } */

		if (!CheckProviderStatus())
		{
			genericResult.Status = CloudFilterAPI.NtStatus.STATUS_CLOUD_FILE_NETWORK_UNAVAILABLE;
		}

		return genericResult;
	}


	/// Attempts to disconnect the server provider, updating its status to Disabled.
	/// This method finalizes any ongoing operations related to the server connection
	/// and ensures the server provider is no longer considered active within the system.
	/// <returns>A GenericResult indicating the success or failure of the disconnect operation.</returns>
	public GenericResult Disconnect()
	{
		GenericResult genericResult = new();
		SetProviderStatus(ServerProviderStatus.Disabled);
		return genericResult;
	}


	/// Creates a new instance of a file reader for server-based interactions.
	/// This method provides an implementation for initializing and obtaining a
	/// server-integrated file reader, enabling asynchronous file read operations.
	/// The returned instance implements the `IReadFileAsync` interface to execute
	/// necessary read operations in the server storage.
	public IReadFileAsync GetNewReadFile()
	{
		return new ReadFileAsyncInternal(this);
	}


	/// Creates a new instance of a write file operation for server-based interaction.
	/// This method initializes and returns an object implementing the IWriteFileAsync interface,
	/// which facilitates asynchronous write operations to a server-based storage provider.
	/// <returns>
	///     An implementation of the IWriteFileAsync interface to manage asynchronous file
	///     writing operations in the server storage.
	public IWriteFileAsync GetNewWriteFile()
	{
		return new WriteFileAsyncInternal(this);
	}


	/// Creates a new instance of an asynchronous file list interface.
	/// This method initializes and returns an object that implements the IFileListAsync interface,
	/// enabling asynchronous operations for managing file lists from the server.
	/// It is typically used to enumerate and interact with files in a server-managed storage system.
	public IFileListAsync GetNewFileList()
	{
		return new FileListAsyncInternal(this);
	}


	/// Deletes a file or directory asynchronously at the specified relative path.
	/// This method attempts to remove the file or directory and handles exceptions resulting from missing files or directories.
	/// If an exception occurs, the result contains the corresponding error message or exception details.
	/// The deletion can either directly delete the target or move it to a recycle bin based on internal logic.
	/// <param name="RelativeFileName">The relative path of the file or directory to be deleted.</param>
	/// <param name="isDirectory">A boolean indicating whether the target is a directory (true) or a file (false).</param>
	/// <return>A task that represents the asynchronous operation, containing the result of the deletion as a <see cref="DeleteFileResult" />.</return>
	public Task<DeleteFileResult> DeleteFileAsync(string RelativeFileName, bool isDirectory)
	{
		DeleteFileResult deleteFileResult = new();

		try
		{
			DeleteOrMoveToRecycleBin(RelativeFileName, isDirectory);
		}
		catch (DirectoryNotFoundException ex)
		{
			// Directory already deleted?
			deleteFileResult.Message = ex.Message;
		}
		catch (FileNotFoundException ex)
		{
			// File already deleted?
			deleteFileResult.Message = ex.Message;
		}
		catch (Exception ex)
		{
			deleteFileResult.SetException(ex);
		}

		return Task.FromResult(deleteFileResult);
	}


	/// Moves a file or directory from one relative path to another within the server provider's managed storage.
	/// This method validates paths, ensures destination directories exist, and handles the operation for both files and directories.
	/// <param name="RelativeFileName">The relative path of the file or directory to be moved.</param>
	/// <param name="RelativeDestination">The relative path of the destination where the file or directory should be moved.</param>
	/// <param name="isDirectory">A boolean indicating whether the item to be moved is a directory (true) or a file (false).</param>
	/// <return>A task representing the asynchronous operation, containing the result of the move operation.</return>
	public Task<MoveFileResult> MoveFileAsync(string RelativeFileName, string RelativeDestination, bool isDirectory)
	{
		var fullPath = Path.Combine(_parameter.ServerPath, RelativeFileName);
		var fullPathDestination = Path.Combine(_parameter.ServerPath, RelativeDestination);

		if (!Directory.Exists(Path.GetDirectoryName(fullPathDestination)))
		{
			Directory.CreateDirectory(Path.GetDirectoryName(fullPathDestination));
		}

		MoveFileResult moveFileResult = new();

		try
		{
			if (isDirectory)
			{
				Directory.Move(fullPath, fullPathDestination);
			}
			else
			{
				File.Move(fullPath, fullPathDestination);
			}
		}
		catch (Exception ex)
		{
			moveFileResult.SetException(ex);
		}

		return Task.FromResult(moveFileResult);
	}


	/// Retrieves information about a specified file or directory from the server.
	/// This method checks for the existence of the specified file or directory on the server
	/// and creates a result containing the file or directory details if present. If the file
	/// or directory does not exist, the result contains an appropriate error status.
	/// <param name="RelativeFileName">The relative path of the file or directory from the server root.</param>
	/// <param name="isDirectory">A boolean value indicating whether the specified path refers to a directory (true) or a file (false).</param>
	/// <return>A task that represents the asynchronous operation. The task result contains a <see cref="GetFileInfoResult" /> object holding the file or directory information or an error status.</return>
	public Task<GetFileInfoResult> GetFileInfo(string RelativeFileName, bool isDirectory)
	{
		GetFileInfoResult getFileInfoResult = new();

		var fullPath = Path.Combine(_parameter.ServerPath, RelativeFileName);

		try
		{
			if (isDirectory)
			{
				if (!Directory.Exists(fullPath))
				{
					return Task.FromResult(new GetFileInfoResult(CloudFilterAPI.NtStatus.STATUS_NOT_A_CLOUD_FILE));
				}
			}
			else
			{
				if (!File.Exists(fullPath))
				{
					return Task.FromResult(new GetFileInfoResult(CloudFilterAPI.NtStatus.STATUS_NOT_A_CLOUD_FILE));
				}
			}

			getFileInfoResult.Placeholder = new Placeholder(fullPath);
		}
		catch (Exception ex)
		{
			getFileInfoResult.SetException(ex);
		}

		return Task.FromResult(getFileInfoResult);
	}


	/// Asynchronously creates a new file or directory on the server at the specified relative path.
	/// This operation determines whether to create a file or a directory based on the provided parameters.
	/// <param name="RelativeFileName">The relative path where the file or directory should be created on the server.</param>
	/// <param name="isDirectory">A boolean indicating whether to create a directory (true) or a file (false).</param>
	/// <returns>
	///     A task representing the asynchronous operation. The result contains an instance of <see cref="CreateFileResult" />,
	///     indicating the success or failure of the operation, along with additional result details.
	/// </returns>
	public Task<CreateFileResult> CreateFileAsync(string RelativeFileName, bool isDirectory)
	{
		CreateFileResult createFileResult = new();

		var fullPath = Path.Combine(_parameter.ServerPath, RelativeFileName);

		try
		{
			if (isDirectory)
			{
				if (!Directory.Exists(fullPath))
				{
					Directory.CreateDirectory(fullPath);
				}
			}
			else
			{
				if (File.Exists(fullPath))
				{
					createFileResult.Status = CloudFilterAPI.NtStatus.STATUS_CLOUD_FILE_IN_USE;
					createFileResult.Message = "Datei existiert bereits";
					createFileResult.Succeeded = false;
				}
				else
				{
					using var strm = File.Create(fullPath);
					strm.Close();
				}
			}

			createFileResult.Placeholder = new Placeholder(fullPath);
		}
		catch (Exception ex)
		{
			createFileResult.SetException(ex);
		}

		return Task.FromResult(createFileResult);
	}


	/// Handles periodic timer callbacks to manage server connection state and perform status checks.
	/// This method is invoked by the timer to ensure the server provider is functioning properly.
	/// <param name="state">An object containing state information passed to the timer callback.</param>
	public void ConnectionTimerCallback(object state)
	{
		CheckProviderStatus();
	}


	/// Handles the callback for the full resynchronization timer.
	/// This method is triggered when the full resync timer elapses, initiating a file synchronization
	/// process with the server to ensure all files and directories are up-to-date.
	/// <param name="state">
	///     The state object passed to the timer's callback, typically containing context or information
	///     for the synchronization process.
	/// </param>
	public void FullResyncTimerCallback(object state)
	{
		//  TODO: Sync on reconnect.
		RaiseFileChanged(new FileChangedEventArgs { ChangeType = WatcherChangeTypes.All, ResyncSubDirectories = true });
	}

	#endregion

	// --------------------------------------------------------------------------------------------
	// INTERNAL METHODS
	// --------------------------------------------------------------------------------------------
	#region INTERNAL METHODS

	/// Moves the specified file or directory to the recycle bin.
	/// This method handles transferring the file or directory from its original
	/// location to a designated recycle bin directory, appending a timestamp to
	/// avoid name conflicts if files or folders with the same name already exist.
	/// <param name="relativePath">The relative path of the file or directory to be moved. This path is relative to the server's root directory.</param>
	/// <param name="isDirectory">Specifies whether the item to be moved is a directory.</param>
	internal void MoveToRecycleBin(string relativePath, bool isDirectory)
	{
		var recyclePath = _parameter.ServerPath + @"\$Recycle.bin\" + relativePath;
		var fullPath = _parameter.ServerPath + @"\" + relativePath;

		recyclePath = Path.GetDirectoryName(recyclePath) + @"\(" + DateTime.Now.ToString("s").Replace(":", "_") + ") " + Path.GetFileName(recyclePath);

		if (!Directory.Exists(Path.GetDirectoryName(recyclePath)))
		{
			Directory.CreateDirectory(Path.GetDirectoryName(recyclePath));
		}

		if (isDirectory)
		{
			if (Directory.Exists(fullPath))
			{
				Directory.Move(fullPath, recyclePath);
			}
		}
		else
		{
			if (File.Exists(fullPath))
			{
				File.Move(fullPath, recyclePath);
			}
		}
	}


	/// Handles the deletion of files or directories by either moving them to the recycle bin or permanently removing them.
	/// The behavior is determined by the provider's configuration settings.
	/// <param name="relativePath">The relative path of the file or directory to be removed.</param>
	/// <param name="isDirectory">Indicates whether the path represents a directory (true) or a file (false).</param>
	internal void DeleteOrMoveToRecycleBin(string relativePath, bool isDirectory)
	{
		if (_parameter.UseRecycleBin)
		{
			MoveToRecycleBin(relativePath, isDirectory);
		}
		else
		{
			var fullPath = Path.Combine(_parameter.ServerPath, relativePath);
			if (isDirectory)
			{
				Directory.Delete(fullPath, false);
			}
			else
			{
				File.Delete(fullPath);
			}
		}
	}


	/// Checks the current status of the server provider and updates it accordingly.
	/// This method evaluates if the provider is operational and sets the status to
	/// either Connected or Disconnected based on the outcome of the check.
	/// Returns false if the provider status is Disabled or if an error occurs during the check.
	/// Otherwise, updates the status and returns true.
	/// <return>Returns true if the provider is online and operational; otherwise, false.</return>
	internal bool CheckProviderStatus()
	{
		if (Status == ServerProviderStatus.Disabled)
		{
			return false;
		}

		try
		{
			// Emulate "Disconnected / Offline" if ServerPath not found
			var isOnline = true;

			SetProviderStatus(isOnline ? ServerProviderStatus.Connected : ServerProviderStatus.Disconnected);

			return isOnline;
		}
		catch (Exception ex)
		{
			//Styletronix.Debug.LogException(ex);
			return false;
		}
	}


	/// Updates the current status of the server provider.
	/// Adjusts associated timers and event watchers based on the new status.
	/// <param name="status">The new status to set for the server provider.</param>
	internal void SetProviderStatus(ServerProviderStatus status)
	{
		if (_status != status)
		{
			_status = status;
			if (status == ServerProviderStatus.Connected)
			{
				// Full sync after reconnect, then every 2 hour
				_fullResyncTimer.Change(TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(120));

				// Check existing connection every 60 Seconds
				_connectionTimer.Change(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));

				if (_status == ServerProviderStatus.Connected)
				{
					if (_serverCallback != null)
					{
						_serverCallback.fileSystemWatcher.EnableRaisingEvents = true;
					}
				}
			}
			else
			{
				// Disable full resyncs if not connected
				_fullResyncTimer.Change(Timeout.Infinite, Timeout.Infinite);

				if (_serverCallback != null)
				{
					_serverCallback.fileSystemWatcher.EnableRaisingEvents = false;
				}
			}

			if (status == ServerProviderStatus.Disabled)
			{
				_connectionTimer.Change(Timeout.Infinite, Timeout.Infinite);
			}
			/* 2 ways */
			// RaiseServerProviderStateChanged(new ServerProviderStateChangedEventArgs(status));
		}
	}


	/// Converts a full file path into a relative path based on the provider's server path.
	/// The method validates that the full path starts with the server path and then removes the server path prefix
	/// to construct a relative path. An exception is thrown if the full path does not belong to the sync root.
	/// <param name="fullPath">The full file path to be converted into a relative path.</param>
	/// <returns>A string representing the relative path derived from the full path.</returns>
	/// <exception cref="Exception">Thrown when the provided full path does not belong to the configured server path.</exception>
	internal string GetRelativePath(string fullPath)
	{
		if (!fullPath.StartsWith(_parameter.ServerPath, StringComparison.CurrentCultureIgnoreCase))
		{
			throw new Exception("File not part of Sync Root");
		}

		if (fullPath.Length == _parameter.ServerPath.Length)
		{
			return string.Empty;
		}

		return fullPath.Remove(0, _parameter.ServerPath.Length + 1);
	}

	#endregion

	// --------------------------------------------------------------------------------------------
	// PRIVATE METHODS
	// --------------------------------------------------------------------------------------------
	#region PRIVATE METHODS

	/// Triggers the ServerProviderStateChanged event to notify subscribers about changes in the server provider's state.
	/// This method is used internally to propagate state change information.
	/// <param name="e">An instance of ServerProviderStateChangedEventArgs containing details of the state change.</param>
	private void RaiseServerProviderStateChanged(ServerProviderStateChangedEventArgs e)
	{
		ServerProviderStateChanged?.Invoke(this, e);
	}


	private void RaiseFileChanged(FileChangedEventArgs e)
	{
		try
		{
			FileChanged?.Invoke(this, e);
		}
		catch (Exception ex)
		{
			//Styletronix.Debug.LogException(ex);
		}
	}

	#endregion
}
