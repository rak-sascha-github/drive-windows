using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RakutenDrive.Models;
using RakutenDrive.Resources;
using RakutenDrive.Utils;
using RakutenDrive.Utils.CredentialManagement;


namespace RakutenDrive.Services;

/// <summary>
///     Represents the result of an operation, including an HTTP status code and an optional message.
/// </summary>
public class OperationResult
{
	public HttpStatusCode StatusCode { get; set; }
	public string? Message { get; set; }
}


/// <summary>
///     Encapsulates the structure of an error response returned by an API, providing details about the specific error encountered.
/// </summary>
public class ApiErrorResponse
{
	[JsonProperty("error")]
	public required string Error { get; set; }
}


/// <summary>
///     Provides methods to interact with and manage files and folders within a team drive environment.
///     Includes functionalities for fetching file lists, renaming, copying, moving, creating folders,
///     deleting files, and retrieving file activity logs.
/// </summary>
internal class TeamDriveService
{
	// --------------------------------------------------------------------------------------------
	// PROPERTIES
	// --------------------------------------------------------------------------------------------
	#region PROPERTIES

	private const string BASE_SUB_URL = "/file/v3/teams";

	#endregion

	// --------------------------------------------------------------------------------------------
	// PUBLIC METHODS
	// --------------------------------------------------------------------------------------------
	#region PUBLIC METHODS

	/// <summary>
	///     Retrieves the list of files from a specified team drive.
	/// </summary>
	/// <param name="teamID">The unique identifier of the team drive from which to fetch the file list.</param>
	/// <param name="payload">An object containing the parameters and filters for the file list query, such as file path range, sort type, and other configurations.</param>
	/// <returns>A task that represents the asynchronous operation. The task result contains a <see cref="FileListResponse" /> representing the response from the team drive, or null if the request fails.</returns>
	public async Task<FileListResponse?> GetTeamDriveFileList(string teamID, object payload)
	{
		var apiHttpClient = new APIHTTPClient();
		var (userInfoResponse, statusCode) = await apiHttpClient.PostAsync($"{BASE_SUB_URL}/{teamID}/files/list", payload);
		if (userInfoResponse != null)
		{
			return JsonConvert.DeserializeObject<FileListResponse>(userInfoResponse);
		}

		return null;
	}


	/// <summary>
	///     Renames a file or folder in the specified team drive.
	/// </summary>
	/// <param name="payload">An object containing the necessary information for renaming, such as new name, file path, and meta details.</param>
	/// <param name="teamID">The unique identifier of the team drive where the file or folder resides.</param>
	/// <returns>A task that represents the asynchronous operation. The task result contains a tuple where the first item is a <see cref="RenameResponse" /> representing the rename operation result and the second item is an <see cref="HttpStatusCode" /> indicating the HTTP status of the operation.</returns>
	public async Task<(RenameResponse? RenameResponse, HttpStatusCode StatusCode)> RenameTheFileOrFolder(RenameRequest payload, string teamID)
	{
		var apiHttpClient = new APIHTTPClient();
		var (renameResponse, statusCode) = await apiHttpClient.PutAsync($"{BASE_SUB_URL}/{teamID}/files/rename", payload);
		if (renameResponse != null)
		{
			return (JsonConvert.DeserializeObject<RenameResponse>(renameResponse), statusCode);
		}

		return (null, statusCode);
	}


	/// <summary>
	///     Copies a specified file or folder to a target path within the team drive.
	/// </summary>
	/// <param name="payload">An instance of <see cref="CopyRequest" /> containing the details of the file or folder to be copied, including the source file(s), target ID, and destination path.</param>
	/// <param name="teamID">The unique identifier of the team drive where the copy operation will be performed.</param>
	/// <returns>A task that represents the asynchronous operation. The task result contains a tuple where the first item is a <see cref="CopyResponse" /> detailing the result of the copy operation, and the second item is an <see cref="HttpStatusCode" /> indicating the HTTP status of the operation.</returns>
	public async Task<(CopyResponse? CopyResponse, HttpStatusCode StatusCode)> CopyFileOrFolder(CopyRequest payload, string teamID)
	{
		var apiHttpClient = new APIHTTPClient();
		var (copyResponse, statusCode) = await apiHttpClient.PostAsync($"{BASE_SUB_URL}/{teamID}/files/copy", payload);
		if (copyResponse != null)
		{
			return (JsonConvert.DeserializeObject<CopyResponse>(copyResponse), statusCode);
		}

		return (null, statusCode);
	}


	/// <summary>
	///     Moves a file or folder to a specified location within a team drive.
	/// </summary>
	/// <param name="payload">An instance of <see cref="MoveRequest" /> containing information about the file or folder to be moved, including source and destination details.</param>
	/// <param name="teamID">The unique identifier of the team drive where the file or folder resides.</param>
	/// <returns>A task that represents the asynchronous operation. The task result contains a tuple where the first element is a <see cref="MoveResponse" /> providing details about the move operation, and the second element is a <see cref="HttpStatusCode" /> indicating the status of the operation.</returns>
	public async Task<(MoveResponse? MoveResponse, HttpStatusCode StatusCode)> MoveFileOrFolder(MoveRequest payload, string teamID)
	{
		var apiHttpClient = new APIHTTPClient();
		var (moveResponse, statusCode) = await apiHttpClient.PutAsync($"{BASE_SUB_URL}/{teamID}/files/move", payload);
		if (moveResponse != null)
		{
			return (JsonConvert.DeserializeObject<MoveResponse>(moveResponse), statusCode);
		}

		return (null, statusCode);
	}


	/// <summary>
	///     Creates a new folder in the specified team drive path.
	/// </summary>
	/// <param name="name">The name of the folder to be created.</param>
	/// <param name="path">The path within the team drive where the folder should be created. Provide an empty string or "/" to create the folder at the root level.</param>
	/// <returns>
	///     A task that represents the asynchronous operation. The task result contains an <see cref="OperationResult" />
	///     object that provides information about the success or failure of the folder creation,
	///     including the HTTP status code and an optional message.
	/// </returns>
	public async Task<OperationResult?> CreateTeamFolder(string name, string path)
	{
		var payload = new { name, path };

		var jwtToken = TokenStorage.GetAccessToken();

		if (string.IsNullOrEmpty(jwtToken))
		{
			Log.Warn("CreateTeamFolder failed: JWT token is null or empty.");
			return null;
		}

		var jsonPayload = JWTParser.Parse(jwtToken);
		var apiHTTPClient = new APIHTTPClient();
		var (createFolderResponse, createFolderStatusCode) = await apiHTTPClient.PostAsync($"{BASE_SUB_URL}/{jsonPayload.TeamID}/files", payload, true);
		string? message = null;

		if (createFolderStatusCode == HttpStatusCode.NoContent)
		{
			var isRoot = path == string.Empty || path == "/";
			if (isRoot)
			{
				var collaborators = new List<Collaborator>
				{
					new()
					{
						UserID      = jsonPayload.UserID,
						TeamID      = jsonPayload.TeamID,
						Email       = jsonPayload.Email,
						AccessLevel = AccessLevel.TeamCreator
					}
				};
				
				var payloadSharing = new SharingRequestModel
				{
					HostID           = jsonPayload.TeamID,
					Path             = name + "/",
					Message          = string.Empty,
					CollaboratorList = collaborators
				};

				/* sharing here */
				await new SharingService().ShareFolderOrFile(payloadSharing);
			}
		}
		else if (createFolderStatusCode == HttpStatusCode.BadRequest)
		{
			if (!string.IsNullOrEmpty(createFolderResponse))
			{
				/* Deserialize the error response. */
				var errorResponse = JsonConvert.DeserializeObject<ApiErrorResponse>(createFolderResponse);

				/* Check the specific error message. */
				if (errorResponse != null && errorResponse.Error == "SENDY_ERR_FILE_ALREADY_EXIST_FILE_NAME")
				{
					message = Strings.error_item_already_exists;
				}
			}
			else
			{
				Log.Warn("CreateTeamFolder failed: createFolderResponse is null or empty.");
			}
		}
		else if (createFolderStatusCode == HttpStatusCode.Forbidden)
		{
			message = Strings.error_item_no_permission;
		}
		else
		{
			message = Strings.error_general_message;
		}

		return new OperationResult { StatusCode = createFolderStatusCode, Message = message };
	}


	/// <summary>
	///     Deletes a specified file from a team drive, with an option to move it to the trash.
	/// </summary>
	/// <param name="file">The <see cref="FileParam" /> object representing the file to be deleted, including its path and other properties.</param>
	/// <param name="isTrash">A boolean indicating whether the file should be moved to the trash (true) or permanently deleted (false).</param>
	/// <param name="cancellationToken">A token to monitor for cancellation requests during the operation.</param>
	/// <returns>A task that represents the asynchronous delete operation.</returns>
	public async Task DeleteTeamFile(FileParam file, bool isTrash, CancellationToken cancellationToken = default)
	{
		var prefix = Path.GetDirectoryName(file.Path.Replace('/', '\\'))?.Replace('\\', '/') ?? throw new InvalidOperationException();
		if (!prefix.EndsWith('/'))
		{
			prefix += '/';
		}

		await DeleteTeamFiles(prefix, [file], isTrash);
	}


	/// <summary>
	///     Deletes files from a team drive based on the specified prefix and file parameters.
	/// </summary>
	/// <param name="prefix">The file path prefix used to locate the files in the team drive.</param>
	/// <param name="files">An array of <see cref="FileParam" /> representing the files to be deleted.</param>
	/// <param name="isTrash">A boolean indicating whether the files should be moved to the trash or permanently deleted.</param>
	/// <param name="cancellationToken">A <see cref="CancellationToken" /> used to cancel the asynchronous operation, if necessary.</param>
	/// <returns>A task that represents the asynchronous delete operation. The task completes once the specified files are deleted or the deletion process fails.</returns>
	public async Task DeleteTeamFiles(string prefix, FileParam[] files, bool isTrash, CancellationToken cancellationToken = default)
	{
		var payload = new DeleteRequest { Prefix = prefix, File = files, Trash = isTrash };
		var teamId = TokenStorage.TeamID;
		var apiClient = new APIHTTPClient();
		var (response, status) = await apiClient.DeleteAsync($"{BASE_SUB_URL}/{teamId}/files", payload, cancellationToken);
		if (response == null)
		{
			throw new InvalidOperationException();
		}

		var taskID = JsonConvert.DeserializeObject<FileOperationResponse>(response)?.TaskID;
		if (taskID != null)
		{
			var fileServices = new FileService();
			if (!await fileServices.WaitUntil(taskID, cancellationToken))
			{
				throw new InvalidOperationException();
			}
		}
		else
		{
			Log.Warn("DeleteTeamFiles: taskID is null.");
		}
	}


	/// <summary>
	///     Retrieves the file activity logs for a specified date range from the team drive.
	/// </summary>
	/// <param name="fromDate">The start date of the logs to be retrieved, represented as the number of milliseconds since the Unix epoch.</param>
	/// <param name="toDate">The end date of the logs to be retrieved, represented as the number of milliseconds since the Unix epoch.</param>
	/// <param name="cancellationToken">The token to monitor for cancellation requests, allowing the operation to be cancelled.</param>
	/// <returns>A task that represents the asynchronous operation. The task result contains a <see cref="FileLogResponse" /> with the file logs retrieved from the team drive, or throws an exception if the retrieval fails.</returns>
	public async Task<FileLogResponse?> GetFileLogs(long fromDate, long toDate, CancellationToken cancellationToken = default)
	{
		var payload = new FileLogRequest { FromDate = fromDate, ToDate = toDate };

		var apiHTTPClient = new APIHTTPClient();
		var url = $"{BASE_SUB_URL}/{TokenStorage.TeamID}/activity/logs";
		var (response, status) = await apiHTTPClient.PostAsync(url, payload, cancellationToken: cancellationToken);

		if (status == HttpStatusCode.OK && response != null)
		{
			return JsonConvert.DeserializeObject<FileLogResponse>(response);
		}

		throw new InvalidOperationException();
	}

	#endregion
}
