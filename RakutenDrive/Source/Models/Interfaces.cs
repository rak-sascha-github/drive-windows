using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RakutenDrive.Controllers.CloudFilterAPIControllers;
using RakutenDrive.Controllers.Providers.SyncProvider;
using RakutenDrive.Models;
using Vanara.PInvoke;


namespace RakutenDrive.Utils;

/// <summary>
///     Defines the parameters required to perform asynchronous file operations.
///     These parameters include metadata and control options for file handling processes.
/// </summary>
public class OpenAsyncParams
{
	public CancellationToken CancellationToken;
	public string? ETag;
	public Placeholder? FileInfo;
	public UploadMode? mode;
	public string? RelativeFileName;
}


/// <summary>
///     Represents the result of a file open operation in a read file operation context.
///     This class encapsulates details such as the associated placeholder or the status of the operation.
/// </summary>
public class ReadFileOpenResult : GenericResult
{
	public Placeholder? Placeholder;


	public ReadFileOpenResult()
	{
	}


	public ReadFileOpenResult(Placeholder placeholder)
	{
		Placeholder = placeholder;
	}


	public ReadFileOpenResult(CloudFilterAPI.NtStatus status)
	{
		Status = status;
	}
}


/// <summary>
///     Represents the result of a file read operation, including the number of bytes read
///     and the status of the operation.
/// </summary>
public class ReadFileReadResult : GenericResult
{
	public int BytesRead;


	public ReadFileReadResult()
	{
	}


	public ReadFileReadResult(int bytesRead)
	{
		BytesRead = bytesRead;
	}


	public ReadFileReadResult(CloudFilterAPI.NtStatus status)
	{
		Status = status;
	}
}


/// <summary>
///     Represents the result of a file close operation performed during a read file process.
///     Provides status information and potentially additional details regarding the success
///     or failure of the file close operation.
/// </summary>
public class ReadFileCloseResult : GenericResult
{
}


/// <summary>
///     Represents the result of attempting to open a file for writing.
///     Encapsulates status information and placeholder metadata associated with the operation.
/// </summary>
public class WriteFileOpenResult : GenericResult
{
	public Placeholder? Placeholder;


	public WriteFileOpenResult()
	{
	}


	public WriteFileOpenResult(Placeholder placeholder)
	{
		Placeholder = placeholder;
	}


	public WriteFileOpenResult(CloudFilterAPI.NtStatus status)
	{
		Status = status;
	}
}


/// <summary>
///     Represents the result of a write operation performed on a file.
///     The result encapsulates the status and any relevant information or outcomes from the write process.
/// </summary>
public class WriteFileWriteResult : GenericResult
{
}


/// <summary>
///     Represents the result of closing a write file operation.
///     This class provides details and outcomes of closing the file after a write operation,
///     including any associated status or metadata.
/// </summary>
public class WriteFileCloseResult : GenericResult
{
	public Placeholder? Placeholder;
}


/// <summary>
///     Represents the result of a file information retrieval operation.
///     This class encapsulates the status of the operation and additional metadata,
///     such as a placeholder for file details.
/// </summary>
public class GetFileInfoResult : GenericResult
{
	public Placeholder? Placeholder;


	public GetFileInfoResult()
	{
	}


	public GetFileInfoResult(CloudFilterAPI.NtStatus status)
	{
		Status = status;
	}
}


/// <summary>
///     Preferred Settings are used by the Sync Provider to change parmeters depending on the used ServerProvider.
/// </summary>
public class PreferredSettings
{
	/// <summary>
	///     Allows the SyncProvider to update parts of an existing file instead replacing the entire file.
	///     ETag is used to validate the file consistence before starting partial updates.
	/// </summary>
	public bool AllowPartialUpdate = true;

	/// <summary>
	///     Maximum chunk Size for file Up-/Download
	/// </summary>
	public int MaxChunkSize = int.MaxValue;

	/// <summary>
	///     Minimum chunk size for file Up-/Download
	/// </summary>
	public int MinChunkSize = 4096;

	public bool PreferFullDirSync = false;
}


/// <summary>
///     Defines an interface for server-side file management operations.
///     This interface provides methods and properties for connecting to a server,
///     managing files or directories, and handling real-time updates from the server.
///     It facilitates key operations such as file creation, deletion, movement,
///     and retrieving file information, along with connection management capabilities.
/// </summary>
public interface IServerFileProvider
{
	public SyncContext SyncContext { get; set; }
	public PreferredSettings PreferredServerProviderSettings { get; }
	public ServerProviderStatus Status { get; }
	public event EventHandler<ServerProviderStateChangedEventArgs> ServerProviderStateChanged;
	public event EventHandler<FileChangedEventArgs> FileChanged;


	/// <summary>
	///     Establish a connection to the Server to check Authentication and for receiving realtime Updates.
	///     ServerProvider is responsible for authentication, reconnect, timeout handling....
	/// </summary>
	/// <returns>
	///     Status if Connection was successful. If not successfull, Connect will not be called again and the ServerProvider is
	///     responsible to report  later successfull connect.
	/// </returns>
	public GenericResult Connect();


	/// <summary>
	///     Disconnect from Server and stop receiving realtime Updates.
	/// </summary>
	/// <returns></returns>
	public GenericResult Disconnect();


	public IReadFileAsync GetNewReadFile();
	public IWriteFileAsync GetNewWriteFile();
	public IFileListAsync GetNewFileList();


	/// <summary>
	///     Delete a File or Directory
	/// </summary>
	/// <param name="RelativeFileName"></param>
	/// <param name="isDirectory"></param>
	/// <returns></returns>
	public Task<DeleteFileResult> DeleteFileAsync(string RelativeFileName, bool isDirectory);


	/// <summary>
	///     Move or Rename a File or Directory
	/// </summary>
	/// <param name="RelativeFileName"></param>
	/// <param name="RelativeDestination"></param>
	/// <param name="isDirectory"></param>
	/// <returns></returns>
	public Task<MoveFileResult> MoveFileAsync(string RelativeFileName, string RelativeDestination, bool isDirectory);


	/// <summary>
	///     Get Placeholder-Data for a File or Directory
	/// </summary>
	/// <param name="RelativeFileName"></param>
	/// <param name="isDirectory"></param>
	/// <returns></returns>
	public Task<GetFileInfoResult> GetFileInfo(string RelativeFileName, bool isDirectory);


	/// <summary>
	///     Create a new File or Directory. It is likely that CreateFileAsync is not called to create new files.
	///     Files should created by calling IWriteFileAsync.OpenAsync();
	/// </summary>
	/// <param name="RelativeFileName"></param>
	/// <param name="isDirectory"></param>
	/// <returns></returns>
	public Task<CreateFileResult> CreateFileAsync(string RelativeFileName, bool isDirectory);
}


/// <summary>
///     Defines an interface to perform asynchronous file reading operations.
///     This interface provides methods to open, read, and close a file asynchronously,
///     and is designed to manage resources efficiently by supporting both synchronous
///     and asynchronous disposal patterns.
/// </summary>
public interface IReadFileAsync : IDisposable, IAsyncDisposable
{
	/// <summary>
	///     This is called at the beginning of a file Transfer, just after instance creation and bevore the first call to ReadAsync()
	/// </summary>
	/// <param name="RelativeFileName"></param>
	/// <param name="srcFolder"></param>
	/// <param name="ctx"></param>
	/// <returns></returns>
	public Task<ReadFileOpenResult> OpenAsync(OpenAsyncParams e);


	/// <summary>
	///     Read a maximum of <paramref name="count" /> bytes of the file, starting at byte <paramref name="offset" /> and write the data
	///     to the supplied <paramref name="buffer" />
	/// </summary>
	/// <param name="buffer">The Buffer, where the data should be stored</param>
	/// <param name="offsetBuffer">Offset of the <paramref name="buffer" /> where to start writing to</param>
	/// <param name="offset">Offset of the File to start reading</param>
	/// <param name="count">Maximum Bytes to read</param>
	/// <returns>Bytes read and written to the <paramref name="buffer" /></returns>
	public Task<ReadFileReadResult> ReadAsync(byte[] buffer, int offsetBuffer, long offset, int count);


	public Task<ReadFileCloseResult> CloseAsync();
}


/// <summary>
///     Represents an interface for performing asynchronous file write operations.
///     This interface provides methods to open, write, and close a file with asynchronous behavior,
///     enabling efficient and controlled file write operations.
/// </summary>
public interface IWriteFileAsync : IDisposable, IAsyncDisposable
{
	public UploadMode SupportedUploadModes { get; }
	public Task<WriteFileOpenResult> OpenAsync(OpenAsyncParams e);
	public Task<WriteFileWriteResult> WriteAsync(byte[] buffer, int offsetBuffer, long offset, int count);
	public Task<WriteFileCloseResult> CloseAsync(bool completed);
}


/// <summary>
///     Defines an interface for asynchronous operations on a list of files.
///     Provides methods to open the file list, retrieve files from the list,
///     and close the file list asynchronously. Implements support for both
///     synchronous and asynchronous disposal patterns.
/// </summary>
public interface IFileListAsync : IDisposable, IAsyncDisposable
{
	public Task<GenericResult> OpenAsync(string RelativeFileName, CancellationToken ctx);
	public Task<GetNextResult> GetNextAsync();
	public Task<GenericResult> CloseAsync();
}


/// <summary>
///     Specifies the upload modes that can be used for transferring files.
///     These modes define the methodology for uploading, such as full file upload,
///     resuming interrupted uploads, or applying partial updates to a file.
/// </summary>
[Flags]
public enum UploadMode : short
{
	FullFile      = 0,
	Resume        = 1,
	PartialUpdate = 2
}


/// <summary>
///     Represents the various operational states of a server provider within the file synchronization system.
///     These states indicate the current connectivity or authentication status of the server provider.
/// </summary>
public enum ServerProviderStatus
{
	Disabled     = 0, // Connection is disabled. No retry possible.
	AuthenticationRequired = 1, // Authentication is required. Retry based on ServerProvider decision
	Failed       = 10, // Retry based on ServerProvider decision
	Disconnected = 11, // Retry required by ServerProvider.
	Connecting   = 12, // ServerProvider tries to connect.
	Connected    = 13 // ServerProvider connected to cloud
}


/// <summary>
///     Represents the type of a change that has occurred to a file.
///     Used to indicate whether a file has been created or deleted.
/// </summary>
public enum FileChangedType
{
	Created,
	Deleted
}


/// <summary>
///     Represents predefined exceptions that can occur during cloud operations.
///     These exceptions cover scenarios such as network unavailability, missing resources, and permission issues.
/// </summary>
public enum CloudExceptions
{
	Offline = 1,
	FileOrDirectoryNotFound = 2,
	AccessDenied = 3
}


/// <summary>
///     Specifies the synchronization modes used to manage file or data synchronization processes.
///     This enumeration defines distinct levels of synchronization behavior, enabling targeted or comprehensive updates.
/// </summary>
public enum SyncMode
{
	Local = 0,
	Full = 1,
	FullQueue = 2
}


/// <summary>
///     Represents a dynamic server placeholder that provides functionality to retrieve or supply a placeholder file or directory on
///     demand.
///     This avoids unnecessary downloads of remote data and allows efficient synchronization.
///     The Dynamic Placeholder provides a way to supply a already downloaded remote placeholder or to get the remote placeholder on
///     demand instead of always downloading remote date even if it may not be required.
/// </summary>
public class DynamicServerPlaceholder
{
	private readonly bool _isDirectory;
	private readonly string? _relativePath;
	private readonly SyncContext? _syncContext;
	private Placeholder? _placeholder;


	public DynamicServerPlaceholder(string relativePath, bool isDirectory, SyncContext syncContext)
	{
		_relativePath = relativePath;
		_syncContext = syncContext;
		_isDirectory = isDirectory;
	}


	public DynamicServerPlaceholder(Placeholder placeholder)
	{
		_placeholder = placeholder;
	}


	public async Task<Placeholder> GetPlaceholder()
	{
		if (_placeholder == null && !string.IsNullOrWhiteSpace(_relativePath))
		{
			if (!_syncContext.SyncProvider.IsExcludedFile(_relativePath))
			{
				var getFileResult = await _syncContext.ServerProvider.GetFileInfo(_relativePath, _isDirectory);
				_placeholder = getFileResult.Placeholder;

				// Handle new local file or file on server deleted
				if (getFileResult.Status == CloudFilterAPI.NtStatus.STATUS_NOT_A_CLOUD_FILE)
					// File not found on Server.... New local file or File deleted on Server.
					// Do not raise any exception and continue processing
				{
					_placeholder = null;
				}
				else
				{
					getFileResult.ThrowOnFailure();
				}
			}
		}

		return _placeholder;
	}


	public static explicit operator DynamicServerPlaceholder(Placeholder placeholder)
	{
		return new DynamicServerPlaceholder(placeholder);
	}
}


/// <summary>
///     Represents a detailed abstraction of a file or folder, encapsulating its properties, metadata, and identity information.
///     This class provides constructors for initializing file data based on various input sources such as file paths,
///     cloud storage metadata, or local filesystem information.
/// </summary>
public class Placeholder : BasicFileInfo
{
	public string ETag;
	public CloudFileIdentity FileIdentity;
	public long FileSize;


	public string RelativeFileName;


	public Placeholder(FileItem fileInfo, string parentPath)
	{
		RelativeFileName = StringHelper.GetLastPartOfPath(fileInfo.NormalizedPath.Replace("//", "/"));
		FileSize = fileInfo.IsFolder ? 0 : fileInfo.Size;
		FileAttributes = fileInfo.IsFolder ? FileAttributes.Directory : FileAttributes.Normal;
		CreationTime = fileInfo.IsFolder ? fileInfo.LastModified : fileInfo.Version != null ? fileInfo.Version.Created : fileInfo.LastModified;
		LastWriteTime = fileInfo.LastModified;
		LastAccessTime = fileInfo.LastModified;
		ChangeTime = fileInfo.LastModified;
		ETag = "_" + fileInfo.LastModified.ToUniversalTime().Ticks + "_" + FileSize;
		FileIdentity = CloudFileIdentity.FromCloudFile(fileInfo, parentPath);
	}


	public Placeholder(FileSystemInfo fileInfo)
	{
		RelativeFileName = fileInfo.Name;
		FileSize = fileInfo.Attributes.HasFlag(FileAttributes.Directory) ? 0 : ((FileInfo)fileInfo).Length;
		FileAttributes = fileInfo.Attributes;
		CreationTime = fileInfo.CreationTime;
		LastWriteTime = fileInfo.LastWriteTime;
		LastAccessTime = fileInfo.LastAccessTime;
		ChangeTime = fileInfo.LastWriteTime;
		ETag = "_" + fileInfo.LastWriteTime.ToUniversalTime().Ticks + "_" + FileSize;
		FileIdentity = CloudFileIdentity.FromLocalPath(fileInfo.FullName);
	}


	public Placeholder(string fullPath)
	{
		FileInfo fileInfo = new(fullPath);

		RelativeFileName = fileInfo.Name;
		FileSize = fileInfo.Attributes.HasFlag(FileAttributes.Directory) ? 0 : fileInfo.Length;
		FileAttributes = fileInfo.Attributes;
		CreationTime = fileInfo.CreationTime;
		LastWriteTime = fileInfo.LastWriteTime;
		LastAccessTime = fileInfo.LastAccessTime;
		ChangeTime = fileInfo.LastWriteTime;
		ETag = "_" + fileInfo.LastWriteTime.ToUniversalTime().Ticks + "_" + FileSize;
		FileIdentity = CloudFileIdentity.FromLocalPath(fullPath);
	}


	public Placeholder(string fullPath, string relativeFileName)
	{
		FileInfo fileInfo = new(fullPath);
		var isDirectory = fileInfo.Attributes.HasFlag(FileAttributes.Directory);

		RelativeFileName = relativeFileName;
		FileSize = isDirectory ? 0 : fileInfo.Length;
		FileAttributes = fileInfo.Attributes;
		CreationTime = fileInfo.CreationTime;
		LastWriteTime = fileInfo.LastWriteTime;
		LastAccessTime = fileInfo.LastAccessTime;
		ChangeTime = fileInfo.LastWriteTime;
		ETag = "_" + fileInfo.LastWriteTime.ToUniversalTime().Ticks + "_" + FileSize;
		FileIdentity = new CloudFileIdentity();
	}


	public Placeholder(string relativeFileName, bool isDirectory)
	{
		RelativeFileName = relativeFileName;
		FileAttributes = isDirectory ? FileAttributes.Directory : FileAttributes.Normal;
		FileIdentity = new CloudFileIdentity();
	}
}


/// <summary>
///     Represents basic metadata information for a file, including its creation, modification, access times, and attributes.
///     This class encapsulates fundamental file properties used for file management or information retrieval operations.
/// </summary>
public class BasicFileInfo
{
	public DateTime ChangeTime;
	public DateTime CreationTime;
	public FileAttributes FileAttributes;
	public DateTime LastAccessTime;
	public DateTime LastWriteTime;
}


/// <summary>
///     Represents the result of a file or directory deletion operation.
///     Encapsulates information regarding the success or failure of the delete process.
/// </summary>
public class DeleteFileResult : GenericResult
{
}


/// <summary>
///     Represents the result of a file or directory move operation.
///     This class encapsulates the outcome of attempting to relocate
///     a file or directory from one path to another within the system.
/// </summary>
public class MoveFileResult : GenericResult
{
}


/// <summary>
///     Represents the result of a file creation operation.
///     This class encapsulates the outcome of attempting to create a file or directory,
///     including related metadata, status, or any placeholder information.
/// </summary>
public class CreateFileResult : GenericResult
{
	public Placeholder Placeholder;
}


/// <summary>
///     Represents a generic result object that encapsulates the outcome of an operation.
///     This class provides details such as whether the operation succeeded, a status code, and an informational message.
///     It also supports various constructors to initialize the result based on specific scenarios, such as exceptions or custom
///     status codes.
/// </summary>
[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
public class GenericResult
{
	private CloudFilterAPI.NtStatus _status;
	public string Message;

	public bool Succeeded;


	public GenericResult()
	{
		Succeeded = true;
		Status = CloudFilterAPI.NtStatus.STATUS_SUCCESS;
		Message = Status.ToString();
	}


	public GenericResult(bool succeeded)
	{
		Succeeded = succeeded;
		Status = succeeded ? CloudFilterAPI.NtStatus.STATUS_SUCCESS : CloudFilterAPI.NtStatus.STATUS_UNSUCCESSFUL;
		Message = Status.ToString();
	}


	public GenericResult(Exception ex)
	{
		SetException(ex);
	}


	public GenericResult(CloudExceptions ex)
	{
		SetException(ex);
	}


	public GenericResult(CloudFilterAPI.NtStatus status)
	{
		Succeeded = status == CloudFilterAPI.NtStatus.STATUS_SUCCESS;
		Status = status;
		Message = Status.ToString();
	}


	public GenericResult(int ntStatus)
	{
		Status = (CloudFilterAPI.NtStatus)ntStatus;
		Succeeded = ntStatus == 0;
		Message = Status.ToString();
	}


	public GenericResult(CloudFilterAPI.NtStatus status, string message)
	{
		Succeeded = status == CloudFilterAPI.NtStatus.STATUS_SUCCESS;
		Status = status;
		Message = message;
	}


	public CloudFilterAPI.NtStatus Status
	{
		get => _status;
		set
		{
			_status = value;
			Succeeded = _status == CloudFilterAPI.NtStatus.STATUS_SUCCESS;
		}
	}


	public static implicit operator bool(GenericResult instance)
	{
		return instance.Succeeded;
	}


	public void SetException(Exception ex)
	{
		Succeeded = false;
		Message = ex.ToString();

		Status = ex switch
		{
			FileNotFoundException => CloudFilterAPI.NtStatus.STATUS_NOT_A_CLOUD_FILE,
			DirectoryNotFoundException => CloudFilterAPI.NtStatus.STATUS_NOT_A_CLOUD_FILE,
			UnauthorizedAccessException => CloudFilterAPI.NtStatus.STATUS_CLOUD_FILE_ACCESS_DENIED,
			IOException => CloudFilterAPI.NtStatus.STATUS_CLOUD_FILE_IN_USE,
			NotSupportedException => CloudFilterAPI.NtStatus.STATUS_CLOUD_FILE_NOT_SUPPORTED,
			InvalidOperationException => CloudFilterAPI.NtStatus.STATUS_CLOUD_FILE_INVALID_REQUEST,
			OperationCanceledException => CloudFilterAPI.NtStatus.STATUS_CLOUD_FILE_REQUEST_CANCELED,
			_ => CloudFilterAPI.NtStatus.STATUS_CLOUD_FILE_UNSUCCESSFUL
		};
	}


	public void SetException(CloudExceptions ex)
	{
		Succeeded = false;
		Message = ex.ToString();

		Status = ex switch
		{
			CloudExceptions.Offline => CloudFilterAPI.NtStatus.STATUS_CLOUD_FILE_NETWORK_UNAVAILABLE,
			CloudExceptions.FileOrDirectoryNotFound => CloudFilterAPI.NtStatus.STATUS_NOT_A_CLOUD_FILE,
			CloudExceptions.AccessDenied => CloudFilterAPI.NtStatus.STATUS_CLOUD_FILE_ACCESS_DENIED,
			_ => CloudFilterAPI.NtStatus.STATUS_CLOUD_FILE_UNSUCCESSFUL
		};
	}


	public void ThrowOnFailure()
	{
		if (!Succeeded)
		{
			throw new Win32Exception((int)Status, Message);
		}
	}
}


/// <summary>
///     Represents the result of an operation, encapsulating information about its success status, state,
///     and an optional message for better contextual understanding.
/// </summary>
public class GenericResult<T> : GenericResult
{
	public T Data;


	public GenericResult()
	{
		Succeeded = true;
		Status = CloudFilterAPI.NtStatus.STATUS_SUCCESS;
		Message = Status.ToString();
	}


	public GenericResult(Exception ex)
	{
		SetException(ex);
	}


	public GenericResult(CloudExceptions ex)
	{
		SetException(ex);
	}


	public GenericResult(CloudFilterAPI.NtStatus status)
	{
		Succeeded = status == CloudFilterAPI.NtStatus.STATUS_SUCCESS;
		Status = status;
		Message = Status.ToString();
	}


	public GenericResult(int ntStatus)
	{
		Status = (CloudFilterAPI.NtStatus)ntStatus;
		Succeeded = ntStatus == 0;
		Message = Status.ToString();
	}


	public GenericResult(CloudFilterAPI.NtStatus status, string message)
	{
		Succeeded = status == CloudFilterAPI.NtStatus.STATUS_SUCCESS;
		Status = status;
		Message = message;
	}
}


/// <summary>
///     Represents the result of fetching the next item in an asynchronous file listing operation.
///     This class encapsulates the retrieved placeholder item and the operation's status information.
/// </summary>
public class GetNextResult : GenericResult
{
	public Placeholder Placeholder;


	public GetNextResult()
	{
	}


	public GetNextResult(CloudFilterAPI.NtStatus status)
	{
		Status = status;
	}
}


/// <summary>
///     Represents basic information about a synchronization provider.
///     This includes the provider's unique identifier, name, and version,
///     which are essential for identifying and managing synchronization behaviors.
/// </summary>
public class BasicSyncProviderInfo
{
	public Guid ProviderId;
	public string ProviderName;
	public string ProviderVersion;
}


/// <summary>
///     Represents the parameters required to initialize and configure a synchronization provider.
///     Encapsulates details such as local file storage path, provider metadata, and server communication provider.
/// </summary>
public class SyncProviderParameters
{
	public string LocalDataPath;
	public BasicSyncProviderInfo ProviderInfo;
	public IServerFileProvider ServerProvider;
}


/// <summary>
///     Provides data for the ServerProviderStateChanged event, which signifies changes in the state of the server file provider.
///     Contains information about the current status of the server provider and an optional message describing the state.
/// </summary>
public class ServerProviderStateChangedEventArgs : EventArgs
{
	public string Message;
	public ServerProviderStatus Status;


	public ServerProviderStateChangedEventArgs()
	{
	}


	public ServerProviderStateChangedEventArgs(ServerProviderStatus status)
	{
		Status = status;
		Message = status.ToString();
	}


	public ServerProviderStateChangedEventArgs(ServerProviderStatus status, string message)
	{
		Status = status;
		Message = message;
	}
}


/// <summary>
///     Provides data for the file change event triggered by the monitored file system.
///     This class encapsulates information about changes in files or directories,
///     including change type, any previous relative file path, a placeholder object
///     for additional metadata, and whether subdirectories require resynchronization.
/// </summary>
public class FileChangedEventArgs : EventArgs
{
	public WatcherChangeTypes ChangeType;
	public string OldRelativePath;
	public Placeholder Placeholder;
	public bool ResyncSubDirectories;
}


/// <summary>
///     Represents data related to synchronization failures, tracking details of the failure and retry status.
///     Contains information such as the last encountered exception, timestamps of the last and next retry attempts,
///     retry count, and the synchronization mode associated with the failure.
/// </summary>
public class FailedData
{
	public Exception LastException;
	public DateTime LastTry;
	public DateTime NextTry;
	public int RetryCount;
	public SyncMode SyncMode;
}


/// <summary>
///     Represents the event data for a file progress update.
///     This class provides details about the ongoing progress of a file processing operation,
///     including the relative file path, completed bytes, and total bytes.
/// </summary>
public class FileProgressEventArgs : EventArgs
{
	private long _BytesCompleted;
	private long _BytesTotal;
	public short Progress;
	public string relativeFilePath;


	public FileProgressEventArgs(string relativeFilePath, long fileBytesCompleted, long fileBytesTotal)
	{
		this.relativeFilePath = relativeFilePath;
		FileBytesCompleted = fileBytesCompleted;
		FileBytesTotal = fileBytesTotal;
	}


	public long FileBytesCompleted
	{
		get => _BytesCompleted;
		set
		{
			_BytesCompleted = value;
			UpdateProgress();
		}
	}

	public long FileBytesTotal
	{
		get => _BytesTotal;
		set
		{
			_BytesTotal = value;
			UpdateProgress();
		}
	}


	private void UpdateProgress()
	{
		try
		{
			if (FileBytesTotal == 0)
			{
				Progress = 0;
			}
			else
			{
				var x = (short)(FileBytesCompleted / (float)FileBytesTotal * 100);
				if (x > 100)
				{
					x = 100;
				}

				Progress = x;
			}
		}
		catch (Exception)
		{
			Progress = 0;
		}
	}
}


/// <summary>
///     Represents the context for synchronization processes, encompassing configuration
///     and connection details to manage the flow of data between local and server systems.
///     This class is foundational for coordinating sync operations within the application.
/// </summary>
public class SyncContext
{
	public CldApi.CF_CONNECTION_KEY ConnectionKey;

	/// <summary>
	///     Absolute Path to the local Root Folder where the cached files are stored.
	/// </summary>
	public string LocalRootFolder;

	public string LocalRootFolderNormalized;
	public IServerFileProvider ServerProvider;
	public SyncProvider SyncProvider;
	public SyncProviderParameters SyncProviderParameter;
}


/// <summary>
///     Represents an operation context and associated metadata for transferring or handling file data.
///     This class includes unique identifiers, file range details, status information, and priority hints
///     to manage the data processing lifecycle effectively.
/// </summary>
public class DataActions
{
	public CancellationTokenSource CancellationTokenSource;
	public long FileOffset;
	public Guid guid = Guid.NewGuid();
	public string Id;
	public bool isCompleted;
	public long Length;
	public string NormalizedPath;
	public byte PriorityHint;
	public CldApi.CF_REQUEST_KEY RequestKey;
	public CldApi.CF_TRANSFER_KEY TransferKey;
}


/// <summary>
///     Represents a data transfer range for fetching file content associated with a particular operation.
///     This class includes information about the file path, priority, byte range, and transfer key.
/// </summary>
public class FetchRange
{
	public string NormalizedPath;
	public byte PriorityHint;
	public long RangeEnd;
	public long RangeStart;
	public CldApi.CF_TRANSFER_KEY TransferKey;


	public FetchRange()
	{
	}


	public FetchRange(DataActions data)
	{
		NormalizedPath = data.NormalizedPath;
		PriorityHint = data.PriorityHint;
		RangeStart = data.FileOffset;
		RangeEnd = data.FileOffset + data.Length;
		TransferKey = data.TransferKey;
	}
}


/// <summary>
///     Represents an action to delete a file or directory, including relevant metadata and operation details.
///     This class is used to manage information about the delete operation, such as whether the target is a directory,
///     the operation information, and the relative path of the target.
/// </summary>
public class DeleteAction
{
	public bool IsDirectory;
	public CldApi.CF_OPERATION_INFO OpInfo;
	public string RelativePath;
}


public class SyncProviderStatusEventArgs : EventArgs
{
	public long QueueLength;
	public long RetryQueueLength;
	public ServerProviderStatus ServerProviderStatus;

	// TODO: Additional status messages
}
