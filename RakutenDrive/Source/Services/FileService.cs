using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json;
using RakutenDrive.Models;
using RakutenDrive.Resources;
using RakutenDrive.Utils;


namespace RakutenDrive.Services;

/// <summary>
///     Provides services to interact with files or folders in Rakuten Drive.
///     Includes operations to retrieve file or folder information,
///     check file or folder action status, check file upload status,
///     retrieve file access levels, manage AWS tokens for file links,
///     and download files or folders.
/// </summary>
internal class FileService
{
	// --------------------------------------------------------------------------------------------
	// PROPERTIES
	// --------------------------------------------------------------------------------------------
	#region PROPERTIES

	private const string BASE_SUB_URL = "/file";

	#endregion


	// --------------------------------------------------------------------------------------------
	// INTERNAL METHODS
	// --------------------------------------------------------------------------------------------
	#region INTERNAL METHODS

	/// <summary>
	///     Waits for a task to complete using its task identifier, with periodic checks until confirmation of completion or failure.
	/// </summary>
	/// <param name="taskId">The unique identifier of the task to monitor for completion.</param>
	/// <param name="cancellationToken">An optional cancellation token to observe while waiting for the operation to complete.</param>
	/// <returns>
	///     A task that represents the asynchronous operation. The task result is a boolean where true indicates the task was successfully completed, and false or an exception indicates failure.
	/// </returns>
	internal async Task<bool> WaitUntil(string taskId, CancellationToken cancellationToken = default)
	{
		var limitOfRequests = 300;
		var isCompleted     = false;
		var url             = $"{BASE_SUB_URL}/v3/files/check";
		var payload         = new CheckRequest { Key = taskId };
		var apiHttpClient   = new APIHTTPClient();
		var random          = new Random();

		async Task sleep()
		{
			await Task.Delay(random.Next(3000, 4000), cancellationToken);
		}

		while (!isCompleted && !cancellationToken.IsCancellationRequested)
		{
			if (--limitOfRequests < 0)
			{
				throw new InvalidOperationException("Too many requests.");
			}

			var (response, status) = await apiHttpClient.PostAsync(url, payload, cancellationToken: cancellationToken);
			if (status == HttpStatusCode.OK && response != null)
			{
				var state = JsonConvert.DeserializeObject<CheckResponse>(response)?.State;
				switch (state)
				{
					case "complete":
						isCompleted = true;
						break;
					case "error":
						Log.Error($"WaitUntil - state returned error.");
						throw new InvalidOperationException();
					default:
						await sleep();
						break;
				}
			}
			else if (status == HttpStatusCode.NoContent)
			{
				await sleep();
			}
			else
			{
				throw new InvalidOperationException();
			}
		}

		return isCompleted;
	}

	#endregion

	// --------------------------------------------------------------------------------------------
	// PUBLIC METHODS
	// --------------------------------------------------------------------------------------------
	#region PUBLIC METHODS

	/// <summary>
	///     Retrieves detailed information about a file or folder based on the provided request payload.
	/// </summary>
	/// <param name="payload">An instance of <see cref="FileOrFolderInfoRequest" /> containing the file or folder details to query.</param>
	/// <returns>
	///     A task that represents the asynchronous operation. The task result contains an instance of <see cref="FileOrFolderInfoResponse" />
	///     with the retrieved information, or null if the operation fails or no information is available.
	/// </returns>
	public async Task<FileOrFolderInfoResponse?> GetFileOrFolderInfo(FileOrFolderInfoRequest payload)
	{
		var apiHttpClient = new APIHTTPClient();
		var (getFileOrFolderInfoResponse, statusCode) = await apiHttpClient.PostAsync($"{BASE_SUB_URL}/v1/file", payload);
		if (getFileOrFolderInfoResponse != null)
		{
			return JsonConvert.DeserializeObject<FileOrFolderInfoResponse>(getFileOrFolderInfoResponse);
		}

		return null;
	}


	/// <summary>
	///     Retrieves information about a file or folder based on the specified host ID and file path.
	/// </summary>
	/// <param name="hostID">The unique identifier of the host where the file or folder resides.</param>
	/// <param name="filePath">The path of the file or folder to retrieve information for.</param>
	/// <returns>
	///     A task that represents the asynchronous operation. The task result contains an instance of <see cref="FileOrFolderInfoResponse" />
	///     with the retrieved information, or null if the operation fails or no information is found.
	/// </returns>
	public async Task<FileOrFolderInfoResponse?> GetFileOrFolderInfo(string? hostID, string filePath)
	{
		var payload = new FileOrFolderInfoRequest
		{
			HostID = hostID,
			Path = filePath,
			IsFolderDetailInfo = filePath.EndsWith('/')
		};
		return await GetFileOrFolderInfo(payload);
	}


	/// <summary>
	///     Retrieves the access level of a file based on the host ID and file path provided.
	/// </summary>
	/// <param name="hostID">The unique identifier of the host where the file is located.</param>
	/// <param name="filePath">The path of the file within the host for which the access level is to be determined.</param>
	/// <returns>
	///     A task that represents the asynchronous operation. The task result contains an instance of <see cref="AccessLevel" />
	///     indicating the access level of the specified file, or null if no information is available.
	/// </returns>
	public async Task<AccessLevel?> GetFileAccessLevel(string? hostID, string filePath)
	{
		return (await GetFileOrFolderInfo(hostID, filePath))?.File.AccessLevel;
	}


	/// <summary>
	///     Checks the action status of a file or folder using the provided key.
	/// </summary>
	/// <param name="key">A unique string key identifying the file or folder for which to check the action status.</param>
	/// <returns>
	///     A task that represents the asynchronous operation. The task result contains an instance of <see cref="CheckFileStatusResponse" />
	///     with the retrieved action status information, or null if no data is returned or the operation fails.
	/// </returns>
	public async Task<CheckFileStatusResponse?> CheckFileOrFolderActionStatus(string key)
	{
		var apiHttpClient = new APIHTTPClient();
		var payload = new { key };
		var (checkFileStatusResponse, statusCode) = await apiHttpClient.PostAsync($"{BASE_SUB_URL}/v3/files/check", payload);
		
		if (checkFileStatusResponse != null)
		{
			return JsonConvert.DeserializeObject<CheckFileStatusResponse>(checkFileStatusResponse);
		}

		return null;
	}


	/// <summary>
	///     Checks the status of a file upload for the specified team based on the provided request payload.
	/// </summary>
	/// <param name="payload">An instance of <see cref="CheckFileUploadRequest" /> containing the file information and upload details to verify.</param>
	/// <param name="teamID">A string representing the team ID for which the file upload is being checked.</param>
	/// <returns>
	///     A task that represents the asynchronous operation. The task result contains an instance of <see cref="CheckFileUploadResponse" />
	///     with the upload status details, or null if the operation fails or the response is unavailable.
	/// </returns>
	public async Task<CheckFileUploadResponse?> CheckFileUpload(CheckFileUploadRequest payload, string teamID)
	{
		var apiHttpClient = new APIHTTPClient();
		var (checkFileUploadResponse, statusCode) = await apiHttpClient.PostAsync($"{BASE_SUB_URL}/v3/teams/{teamID}/files/uploads", payload);
		
		if (checkFileUploadResponse != null)
		{
			return JsonConvert.DeserializeObject<CheckFileUploadResponse>(checkFileUploadResponse);
		}

		if (statusCode == HttpStatusCode.Forbidden)
		{
			MessageBox.Show(Strings.error_no_permission_title, "Rakuten Drive", MessageBoxButton.OK, MessageBoxImage.Error);
		}

		return null;
	}


	/// <summary>
	///     Fetches AWS temporary credentials required to access a file link for the specified host and path.
	/// </summary>
	/// <param name="hostID">The identifier of the host where the file is located.</param>
	/// <param name="path">The path of the file for which the AWS temporary credentials are requested. Defaults to "hello" if not provided.</param>
	/// <returns>
	///     A task that represents the asynchronous operation. The task result contains an instance of <see cref="AwsTemporaryCredentials" />
	///     with the retrieved AWS temporary credentials, or null if the operation fails or no credentials are available.
	/// </returns>
	public async Task<AwsTemporaryCredentials?> GetFileLinkAWSTokens(string hostID, string path = "hello")
	{
		var apiHTTPClient = new APIHTTPClient();
		var getFileLinkAWSTokensResponse = await apiHTTPClient.GetAsync($"{BASE_SUB_URL}/v1/filelink/token?host_id={hostID}&path={path}");
		
		if (getFileLinkAWSTokensResponse != null)
		{
			return JsonConvert.DeserializeObject<AwsTemporaryCredentials>(getFileLinkAWSTokensResponse);
		}

		return null;
	}


	/// <summary>
	///     Retrieves the view URL for a specified file or folder in Rakuten Drive.
	/// </summary>
	/// <param name="hostId">The ID of the host where the file or folder resides.</param>
	/// <param name="path">The path of the file or folder for which the view URL is to be retrieved.</param>
	/// <param name="size">The size of the file or folder in bytes.</param>
	/// <param name="cancellationToken">A token to cancel the asynchronous operation, if necessary.</param>
	/// <returns>
	///     A task that represents the asynchronous operation. The task result contains the view URL for the file or folder
	///     as a string, or null if the operation fails or the URL cannot be retrieved.
	/// </returns>
	public async Task<string?> GetViewURL(string hostId, string path, long size, CancellationToken cancellationToken = default)
	{
		var apiHttpClient = new APIHTTPClient();
		var payload = new { path, size };
		var (response, statusCode) = await apiHttpClient.PostAsync($"{BASE_SUB_URL}/v3/{hostId}/files/view", payload, cancellationToken: cancellationToken);
		
		if (response != null)
		{
			return JsonConvert.DeserializeObject<FileViewResponse>(response)?.URL;
		}

		return null;
	}


	/// <summary>
	///     Downloads a file from Rakuten Drive based on the specified host identifier and file parameters.
	/// </summary>
	/// <param name="hostId">The unique identifier of the host from which the file is to be downloaded.</param>
	/// <param name="file">An instance of <see cref="FileParam" /> containing the details of the file, such as path, size, and version information.</param>
	/// <param name="cancellationToken">A token to monitor for cancellation requests, enabling cooperative cancellation of the download operation.</param>
	/// <returns>
	///     A task that represents the asynchronous operation. The task result contains a string representing the URL of the downloaded file,
	///     or null if the download operation fails.
	/// </returns>
	public async Task<string?> DownloadFile(string hostId, FileParam file, CancellationToken cancellationToken = default)
	{
		var path = StringHelper.GetParentPath(file.Path);
		return await DownloadFile(hostId, path, [file], cancellationToken);
	}


	/// <summary>
	///     Downloads specified files from a given host and returns the URL for downloading the files.
	/// </summary>
	/// <param name="hostId">The identifier of the host where the files are located. Can be a team identifier or a standard host identifier.</param>
	/// <param name="path">The parent path of the files to be downloaded.</param>
	/// <param name="files">An array of <see cref="FileParam" /> instances representing the files to be downloaded.</param>
	/// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
	/// <returns>
	///     A task that represents the asynchronous operation. The task result contains the download URL as a string, or null if the operation fails.
	/// </returns>
	public async Task<string?> DownloadFile(string hostId, string path, FileParam[] files, CancellationToken cancellationToken = default)
	{
		var apiHttpClient = new APIHTTPClient();
		var payload = new DownloadRequest { Path = path, File = files };
		var isTeamId = StringHelper.IsTeamID(hostId);
		
		if (isTeamId)
		{
			payload.HostID = hostId;
		}

		var url = isTeamId ? $"{BASE_SUB_URL}/v3/teams/{hostId}/files/download" : $"{BASE_SUB_URL}/v1/filelink/download";
		var (response, statusCode) = await apiHttpClient.PostAsync(url, payload, cancellationToken: cancellationToken);

		if (statusCode == HttpStatusCode.Forbidden)
		{
			return null;
		}
		
		if (response != null)
		{
			return JsonConvert.DeserializeObject<DownloadResponse>(response)?.URL;
		}

		return null;
	}

	#endregion
}
