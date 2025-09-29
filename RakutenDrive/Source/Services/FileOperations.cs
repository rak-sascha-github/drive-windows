using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.S3;
using Amazon.S3.Transfer;
using RakutenDrive.Controllers.CloudFilterAPIControllers;
using RakutenDrive.Models;
using RakutenDrive.Resources;
using RakutenDrive.Services;
using RakutenDrive.Utils;
using RakutenDrive.Utils.CredentialManagement;


namespace RakutenDrive.Source.Services;

/// <summary>
///     Provides file operation services for managing local and cloud files and directories.
/// </summary>
[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
internal class FileOperations
{
	// --------------------------------------------------------------------------------------------
	// PROPERTIES
	// --------------------------------------------------------------------------------------------
	#region PROPERTIES

	public delegate void ShowNotification(string title, string message);


	private const int MAX_OPERATION_COUNT = 8;

	private readonly CancellationTokenSource _cancellationTokenSource = new();
	private readonly SemaphoreSlim _fileOperationPool = new(MAX_OPERATION_COUNT, MAX_OPERATION_COUNT);
	private readonly FileService _fileService = new();
	private readonly ConcurrentSet<string> _filesToDelete = new();
	private readonly ConcurrentSet<string> _filesToUpload = new();
	private readonly string _localRootpath;
	private readonly LogService _logService = new();
	private readonly ShowNotification? _onShowNotification;
	private readonly Dictionary<string, Task> _operationTasks = new();
	private readonly TeamDriveService _teamDriveService = new();
	private readonly SemaphoreSlim _uploadPool = new(MAX_OPERATION_COUNT, MAX_OPERATION_COUNT);

	#endregion

	// --------------------------------------------------------------------------------------------
	// INIT
	// --------------------------------------------------------------------------------------------
	#region INIT

	/// <summary>
	///     Provides a set of functionalities for handling file operations, such as creating folders,
	///     uploading files, renaming files, and other file-related tasks within a local directory structure.
	/// </summary>
	/// The class also supports notification callbacks for providing user feedback on operations.
	public FileOperations(string localRootpath, ShowNotification? onShowNotification)
	{
		_localRootpath = localRootpath.TrimEnd('\\');
		_onShowNotification = onShowNotification;
	}

	#endregion

	// --------------------------------------------------------------------------------------------
	// PUBLIC METHODS
	// --------------------------------------------------------------------------------------------
	#region PUBLIC METHODS

	/// <summary>
	///     Releases all resources and cancels any ongoing operations within the file operation service.
	/// </summary>
	/// This includes canceling the cancellation token, clearing any pending tasks,
	/// and disposing of internal synchronization mechanisms such as semaphore pools.
	public void Shutdown()
	{
		_cancellationTokenSource.Cancel();
		
		ClearTasks();
		
		_cancellationTokenSource.Dispose();
		_fileOperationPool.Dispose();
		_uploadPool.Dispose();
	}


	/// <summary>
	///     Creates a new folder in the local directory and synchronizes it with the cloud storage.
	/// </summary>
	/// <param name="localPath">The local path where the folder is to be created. This path must refer to a directory.</param>
	/// <returns>
	///     A task representing the asynchronous operation. Completes when the folder has been created and its state synchronized with the cloud.
	/// </returns>
	/// <exception cref="InvalidOperationException">Thrown if the specified path is not a directory.</exception>
	public async Task CreateFolder(string localPath)
	{
		var isDirectory = File.GetAttributes(localPath).HasFlag(FileAttributes.Directory);
		if (!isDirectory)
		{
			throw new InvalidOperationException("Cannot create folder, path is not a directory.");
		}

		var name       = Path.GetFileName(localPath);
		var cloudPath  = LocalPathToCloudPath(localPath, isDirectory);
		var parentPath = StringHelper.GetParentPath(cloudPath);

		try
		{
			_fileOperationPool.Wait(_cancellationTokenSource.Token);
			WaitForExistingTask(localPath);


			async Task PerformTask()
			{
				var res = await _teamDriveService.CreateTeamFolder(name, parentPath);
				if (res?.StatusCode == HttpStatusCode.NoContent || res?.StatusCode == HttpStatusCode.OK)
				{
					var cloudPath2 = parentPath + (parentPath.EndsWith("/") ? string.Empty : "/") + name + (name.EndsWith("/") ? string.Empty : "/");
					var fileInfo = await _fileService.GetFileOrFolderInfo(TokenStorage.TeamID, cloudPath2);
					var fileIdentity = CloudFileIdentity.FromCloudFile(fileInfo!.File, parentPath);
					var localFolderPlaceholder = new CloudFilterAPI.ExtendedPlaceholderState(localPath);

					localFolderPlaceholder.ConvertToPlaceholder(true, fileIdentity);
				}

				if (res?.Message != null)
				{
					ShowMessage(res.Message);
				}
			}


			await AwaitTask(localPath, PerformTask());
		}
		catch (Exception ex)
		{
			Log.Error($"CreateFolder exception thrown: {ex.Message}");
		}
		finally
		{
			_fileOperationPool.Release();
		}
	}


	/// <summary>
	///     Uploads a specified file to a target location in the cloud.
	///     Validates the file's existence and ensures it is not a directory.
	///     Handles tasks related to preparing and uploading the file using the upload service.
	/// </summary>
	/// <param name="localPath">The local file path of the file to be uploaded. It must refer to an existing file and not a directory.</param>
	/// <returns>A Task representing the asynchronous operation of uploading the file.</returns>
	/// <exception cref="InvalidOperationException">Thrown when the provided path is a directory.</exception>
	/// <exception cref="FileNotFoundException">Thrown when the specified file does not exist at the given path.</exception>
	public async Task UploadFile(string localPath)
	{
		var isDirectory = File.GetAttributes(localPath).HasFlag(FileAttributes.Directory);
		if (isDirectory)
		{
			throw new InvalidOperationException("Cannot upload file, path is a directory.");
		}

		if (_filesToUpload.Contains(localPath))
		{
			return;
		}

		try
		{
			_filesToUpload.Add(localPath);
 
			var name        = Path.GetFileName(localPath);
			var cloudPath   = LocalPathToCloudPath(localPath, isDirectory);
			var parentPath  = StringHelper.GetParentPath(cloudPath);
			var jwtToken    = TokenStorage.GetAccessToken();
			var jsonPayload = JWTParser.Parse(jwtToken);
			
			_uploadPool.Wait(_cancellationTokenSource.Token);
			WaitForExistingTask(Path.GetDirectoryName(localPath)!);

			async Task PerformTask()
			{
				if (!File.Exists(localPath))
				{
					throw new FileNotFoundException("File does not exist", localPath);
				}

				var checkFileUploadRequest = new CheckFileUploadRequest
				{
					File     = [new FileInfoCheckFile { Path = name, Size = 0 }],
					Path     = parentPath,
					Replace  = false,
					Token    = string.Empty,
					UploadID = string.Empty
				};

				if (jsonPayload.TeamID != null && jsonPayload.UserID != null)
				{
					var checkFileUploadRespose = await _fileService.CheckFileUpload(checkFileUploadRequest, jsonPayload.TeamID);
					if (checkFileUploadRespose != null)
					{
						var currentFolder = Path.GetDirectoryName(localPath);
						var folderName    = Path.GetFileName(currentFolder) ?? string.Empty;

						/* Write activity log. */
						await _logService.WriteActivityLogs(jsonPayload.TeamID, new ActivityLog.TemplatesActivityLogResponse
						{
							Templates =
							[
								new ActivityLog.TemplateActivityLog
								{
									Action   = "action-cloud-viewed-file",
									Category = "action-file-operations",
									Message  = "admin-cloud-team-viewed-file-message",
									Title    = "admin-cloud-viewed-file-title",
									UserID   = jsonPayload.UserID,
									Vars     = new ActivityLog.TemplateActivityLogVars { Name = folderName, Size = "-" }
								}
							]
						});

						var awsTempCredentials = await _fileService.GetFileLinkAWSTokens(jsonPayload.UserID);
						if (awsTempCredentials != null)
						{
							/* Convert string to RegionEndpoint. */
							var region   = RegionEndpoint.GetBySystemName(checkFileUploadRespose.Region);
							var s3Client = new AmazonS3Client(awsTempCredentials.AccessKeyID, awsTempCredentials.SecretAccessKey, awsTempCredentials.SessionToken, region);

							/* Use TransferUtility for uploading the file. */
							var transferUtility = new TransferUtility(s3Client);

							/* Upload the file. */
							if (checkFileUploadRespose is { Bucket: not null, File: not null, UploadID: not null })
							{
								_ = await AWSS3Service.UploadSingleFileAsync(transferUtility, checkFileUploadRespose.Bucket, checkFileUploadRespose.Prefix + checkFileUploadRespose.File[0].Path, localPath, _cancellationTokenSource.Token);
								_ = await _fileService.WaitUntil(checkFileUploadRespose.UploadID, _cancellationTokenSource.Token);
							}

							var cloudPath2 = parentPath + (parentPath.EndsWith("/") ? string.Empty : "/") + name;
							var fileInfo = await _fileService.GetFileOrFolderInfo(TokenStorage.TeamID, cloudPath2);
							var fileIdentity = CloudFileIdentity.FromCloudFile(fileInfo!.File, parentPath);

							/* Set the status. */
							var localFolderPlaceholder = new CloudFilterAPI.ExtendedPlaceholderState(localPath);
							localFolderPlaceholder.ConvertToPlaceholder(true, fileIdentity);
						}
					}
				}
			}

			await AwaitTask(localPath, PerformTask());
		}
		finally
		{
			_filesToUpload.Remove(localPath);
			_uploadPool.Release();
		}
	}


	/// <summary>
	///     Renames a file or directory from the specified local path to the target path.
	/// </summary>
	/// <param name="localPath">The original path of the file or directory to be renamed.</param>
	/// <param name="localTargetPath">The new path for the file or directory.</param>
	/// <returns>A task that represents the asynchronous rename operation.</returns>
	/// <exception cref="InvalidOperationException">Thrown when the file is in use, in the upload queue, or the rename operation fails.</exception>
	public async Task RenameFile(string localPath, string localTargetPath)
	{
		var isDirectory = File.GetAttributes(localPath).HasFlag(FileAttributes.Directory);
		if (!isDirectory && _filesToUpload.Contains(localPath))
		{
			throw new InvalidOperationException("Cannot rename file, file is in upload queue.");
		}

		try
		{
			_fileOperationPool.Wait(_cancellationTokenSource.Token);
			WaitForExistingTask(localPath);

			async Task PerformTask()
			{
				if (!CanChangeFile(localPath))
				{
					throw new InvalidOperationException("Cannot rename file, file is in use.");
				}
				
				var path             = LocalPathToCloudPath(localPath, isDirectory);
				var targetPath       = LocalPathToCloudPath(localTargetPath, isDirectory);
				var teamID           = TokenStorage.TeamID;
				var fileInfoRequest  = new FileOrFolderInfoRequest { HostID = teamID, Path = path, IsFolderDetailInfo = path.EndsWith('/') };
				var fileInfo         = await _fileService.GetFileOrFolderInfo(fileInfoRequest) ?? throw new InvalidOperationException();
				var fileOrFolderName = StringHelper.GetLastPartOfPath(targetPath);
				
				var renameRequest    = new RenameRequest
				{
					Name = fileOrFolderName,
					File = new FileRename
					{
						LastModified = fileInfo.File.LastModified.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
						Path         = fileInfo.File.Path,
						Size         = fileInfo.File.Size,
						VersionId    = fileInfo.File.VersionID
					}, Prefix = StringHelper.GetParentPath(path)
				};
				
				var (renameResponse, statusCode) = await _teamDriveService.RenameTheFileOrFolder(renameRequest, teamID);
				if (renameResponse != null && renameResponse.TaskID != null && statusCode == HttpStatusCode.OK)
				{
					var fileServices = new FileService();
					if (!await fileServices.WaitUntil(renameResponse.TaskID, _cancellationTokenSource.Token))
					{
						throw new InvalidOperationException();
					}
				}
				else
				{
					if (statusCode == HttpStatusCode.Forbidden)
					{
						var message = string.Format(Strings.error_no_permission_message_rename, StringHelper.GetLastPartOfPath(path));
						ShowMessage(Strings.error_general_title_rename, message);
						throw new InvalidOperationException();
					}
				}
			}

			await AwaitTask(localPath, PerformTask());
		}
		finally
		{
			_fileOperationPool.Release();
		}
	}


	/// <summary>
	///     Moves a file or directory from a specified source path to a target destination.
	/// </summary>
	/// <param name="localPath">The current path of the file or directory to be moved.</param>
	/// <param name="localTargetPath">The destination path where the file or directory should be moved.</param>
	/// <exception cref="InvalidOperationException">
	///     Thrown when attempting to move a file that is currently in the upload queue.
	/// </exception>
	/// <returns>A task representing the asynchronous operation of moving the file or directory.</returns>
	public async Task MoveFile(string localPath, string localTargetPath)
	{
		var isDirectory = File.GetAttributes(localPath).HasFlag(FileAttributes.Directory);
		if (!isDirectory && _filesToUpload.Contains(localPath))
		{
			throw new InvalidOperationException("Cannot move file, file is in upload queue.");
		}

		try
		{
			_fileOperationPool.Wait(_cancellationTokenSource.Token);
			WaitForExistingTask(localPath);

			async Task PerformTask()
			{
				if (!CanChangeFile(localPath))
				{
					throw new InvalidOperationException("Cannot move file, file is in use.");
				}

				var path = LocalPathToCloudPath(localPath, isDirectory);
				var targetPath = LocalPathToCloudPath(localTargetPath, isDirectory);
				var parentPath = StringHelper.GetParentPath(path);

				if (parentPath == string.Empty || parentPath == "/")
				{
					throw new InvalidOperationException();
				}

				var teamID = TokenStorage.TeamID;
				var fileInfoRequestMoved = new FileOrFolderInfoRequest { HostID = teamID, Path = path, IsFolderDetailInfo = path.EndsWith('/') };
				var fileInfo = await _fileService.GetFileOrFolderInfo(fileInfoRequestMoved) ?? throw new InvalidOperationException();

				var moveRequest = new MoveRequest
				{
					TargetID = teamID,
					File =
					[
						new FileRename { LastModified = fileInfo.File.LastModified.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"), Path = fileInfo.File.Path, Size = fileInfo.File.Size, VersionId = fileInfo.File.VersionID }
					],
					Prefix = parentPath,
					ToPath = StringHelper.GetParentPath(targetPath)
				};

				var (moveResponse, statusCode) = await _teamDriveService.MoveFileOrFolder(moveRequest, teamID);
				if (moveResponse != null && moveResponse.TaskID != null && statusCode == HttpStatusCode.OK)
				{
					var fileServices = new FileService();
					if (!await fileServices.WaitUntil(moveResponse.TaskID, _cancellationTokenSource.Token))
					{
						throw new InvalidOperationException();
					}
				}
				else
				{
					if (statusCode == HttpStatusCode.Forbidden)
					{
						var message = string.Format(Strings.error_no_permission_message_move, StringHelper.GetLastPartOfPath(path));
						ShowMessage(Strings.error_general_title_move, message);
					}

					throw new InvalidOperationException();
				}
			}

			await AwaitTask(localPath, PerformTask());
		}
		finally
		{
			_fileOperationPool.Release();
		}
	}


	/// <summary>
	///     Deletes the specified file or folder from the local directory. This operation adds the file to a deletion queue
	///     to ensure it is processed safely and handles related constraints, such as ensuring the file is not in use
	///     or being uploaded at the time of the request.
	/// </summary>
	/// <param name="localPath">The local file or folder path to be deleted.</param>
	/// <returns>An asynchronous task that represents the completion of the delete operation.</returns>
	/// <exception cref="InvalidOperationException">Thrown if the file is in the upload queue or currently in use.</exception>
	public async Task DeleteFile(string localPath)
	{
		var isDirectory = File.GetAttributes(localPath).HasFlag(FileAttributes.Directory);
		if (!isDirectory && _filesToUpload.Contains(localPath))
		{
			throw new InvalidOperationException("Cannot delete file, file is in upload queue.");
		}

		if (_filesToDelete.Contains(localPath))
		{
			return;
		}

		_filesToDelete.Add(localPath);
		try
		{
			_fileOperationPool.Wait(_cancellationTokenSource.Token);
			WaitForExistingTask(localPath);

			async Task PerformTask()
			{
				if (!CanChangeFile(localPath))
				{
					throw new InvalidOperationException("Cannot delete file, file is in use.");
				}

				var path = LocalPathToCloudPath(localPath, isDirectory);
				var fileInfo = await _fileService.GetFileOrFolderInfo(TokenStorage.TeamID, path) ?? throw new InvalidOperationException();
				var file = new FileParam { Path = fileInfo.File.Path, Size = fileInfo.File.Size, VersionID = fileInfo.File.VersionID, LastModified = fileInfo.File.LastModified.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") };

				await _teamDriveService.DeleteTeamFile(file, true, _cancellationTokenSource.Token);
			}

			await AwaitTask(localPath, PerformTask());
		}
		finally
		{
			_filesToDelete.Remove(localPath);
			_fileOperationPool.Release();
		}
	}

	#endregion

	// --------------------------------------------------------------------------------------------
	// PRIVATE METHODS
	// --------------------------------------------------------------------------------------------
	#region PRIVATE METHODS

	/// <summary>
	///     Asynchronously awaits the completion of a given task while managing the internal task state
	///     associated with a specific file or directory path.
	/// </summary>
	/// <param name="localPath">The file or directory path associated with the task being awaited.</param>
	/// <param name="task">The task to be awaited.</param>
	/// <return>Returns a Task representing the asynchronous operation.</return>
	private async Task AwaitTask(string localPath, Task task)
	{
		try
		{
			AddTask(localPath, task);
			await task;
		}
		finally
		{
			RemoveTask(localPath);
		}
	}


	/// <summary>
	///     Clears all ongoing and completed file operation tasks from the internal task pool.
	/// </summary>
	/// Ensures all tasks have completed execution before being removed. Catches and logs any
	/// exceptions that occur during task completion to avoid disruption to the clearing process.
	/// This method is thread-safe and uses locking to ensure safe concurrent access to the task collection.
	private void ClearTasks()
	{
		lock (_operationTasks)
		{
			foreach (var task in _operationTasks.Values)
			{
				try
				{
					task.Wait();
				}
				catch (Exception ex)
				{
					Log.Error("Error while waiting for task completion", ex);
				}
			}

			_operationTasks.Clear();
		}
	}


	/// <summary>
	///     Determines whether a file or directory can be modified based on the current ongoing operations
	///     and the specified path. Returns false if the file or directory is part of or overlaps with any
	///     ongoing tasks.
	/// </summary>
	/// <param name="localPath">The full local path of the file or directory to check.</param>
	/// <returns>True if the file or directory can be safely modified, otherwise false.</returns>
	private bool CanChangeFile(string localPath)
	{
		try
		{
			var isDirectory = File.GetAttributes(localPath).HasFlag(FileAttributes.Directory);
			lock (_operationTasks)
			{
				foreach (var e in _operationTasks)
				{
					if (e.Key == localPath)
					{
						continue;
					}

					if (isDirectory && e.Key.StartsWith(localPath.EndsWith('\\') ? localPath : localPath + '\\'))
					{
						return false;
					}

					if (localPath.StartsWith(e.Key.EndsWith('\\') ? e.Key : e.Key + '\\'))
					{
						return false;
					}
				}
			}
		}
		catch (Exception)
		{
			return false;
		}

		return true;
	}


	/// <summary>
	///     Adds a new task to the internal collection of currently active file operation tasks.
	///     The task is associated with a specific local file path to ensure that concurrent operations
	///     on the same file path are not allowed.
	/// </summary>
	/// <param name="localPath">
	///     The local path of the file or directory that the task is associated with. This path serves
	///     as a unique identifier to prevent multiple tasks from being processed simultaneously
	///     for the same file path.
	/// </param>
	/// <param name="task">
	///     The task representing the file operation to be performed. This task is added to the
	///     internal collection to track its execution status.
	/// </param>
	/// <exception cref="InvalidOperationException">
	///     Thrown when a task is already in progress for the specified <paramref name="localPath" />.
	///     Only one task can be active for a given file path at any time.
	/// </exception>
	private void AddTask(string localPath, Task task)
	{
		lock (_operationTasks)
		{
			if (_operationTasks.ContainsKey(localPath))
			{
				throw new InvalidOperationException("Task for this local path is in progress.");
			}

			_operationTasks[localPath] = task;
		}
	}


	/// <summary>
	///     Removes a file operation task associated with the specified local path from the task dictionary.
	/// </summary>
	/// <param name="localPath">The path of the file or directory whose associated task is to be removed.</param>
	private void RemoveTask(string localPath)
	{
		lock (_operationTasks)
		{
			if (_operationTasks.ContainsKey(localPath))
			{
				_operationTasks.Remove(localPath);
			}
		}
	}


	/// <summary>
	///     Waits for any existing task associated with the specified local path to complete.
	///     This ensures that no conflicting operations are performed concurrently on the same local path.
	/// </summary>
	/// <param name="localPath">The local file or directory path for which to wait for the existing task to complete.</param>
	private void WaitForExistingTask(string localPath)
	{
		Task? task = null;
		lock (_operationTasks)
		{
			if (_operationTasks.TryGetValue(localPath, out var previousTask))
			{
				task = previousTask;
			}
		}

		if (task != null)
		{
			task.Wait(_cancellationTokenSource.Token);
		}
	}


	/// <summary>
	///     Converts a local file or directory path to a corresponding cloud file or directory path.
	///     This method ensures that the local root path matches and formats the path for cloud usage.
	/// </summary>
	/// <param name="localPath">The full local path to the file or directory.</param>
	/// <param name="isDirectory">A boolean indicating whether the path represents a directory.</param>
	/// <returns>The cloud-compatible path as a string. Directories will include a trailing '/' character.</returns>
	/// <exception cref="InvalidOperationException">Thrown if the local path does not match the predefined root path.</exception>
	private string LocalPathToCloudPath(string localPath, bool isDirectory)
	{
		var prefix = _localRootpath + '\\';
		if (localPath.StartsWith(prefix))
		{
			var cloudPath = localPath.Substring(prefix.Length).Replace('\\', '/');
			return isDirectory ? cloudPath + '/' : cloudPath;
		}

		throw new InvalidOperationException("Local path does not match the root path.");
	}


	/// <summary>
	///     Displays a message with an optional title and delegates the handling of the notification
	///     through the associated notification mechanism.
	/// </summary>
	/// <param name="message">The message content to be displayed.</param>
	private void ShowMessage(string message)
	{
		ShowMessage(string.Empty, message);
	}


	/// <summary>
	///     Displays a message with a specified title and content using the defined notification mechanism.
	/// </summary>
	/// <param name="title">The title of the message to be displayed.</param>
	/// <param name="message">The content of the message to be displayed.</param>
	private void ShowMessage(string title, string message)
	{
		_onShowNotification?.Invoke(title, message);
	}

	#endregion
}
