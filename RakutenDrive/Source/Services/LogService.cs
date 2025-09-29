using System;
using System.Net;
using System.Threading.Tasks;
using RakutenDrive.Models;
using RakutenDrive.Utils;


namespace RakutenDrive.Services;

/// <summary>
///     LogServices serves as a utility for handling activity logging within the application.
///     It provides methods to write activity logs to a remote server for specified teams.
/// </summary>
internal class LogService
{
	// --------------------------------------------------------------------------------------------
	// PUBLIC METHODS
	// --------------------------------------------------------------------------------------------

	/// <summary>
	///     Writes activity logs to a remote logging service associated with a specific team.
	/// </summary>
	/// <param name="teamId">The unique identifier of the team for which the logs are being written.</param>
	/// <param name="payload">The payload containing the activity log data to be stored.</param>
	/// <returns>A task that represents the asynchronous operation. The task result contains a boolean value indicating the success or failure of the logging operation.</returns>
	public async Task<bool> WriteActivityLogs(string teamId, ActivityLog.TemplatesActivityLogResponse payload)
	{
		try
		{
			var apiHttpClient = new APIHTTPClient();
			apiHttpClient.SetBaseURL("logger");
			
			var (userInfoResponse, statusCode) = await apiHttpClient.PostAsync($"/{teamId}/logs", payload);
			if (statusCode != HttpStatusCode.NoContent)
			{
				return true;
			}
		}
		catch (Exception ex)
		{
			Log.Error($"Error writing activity logs to remote logging service: {ex.Message}");
			return false;
		}

		return false;
	}
}
