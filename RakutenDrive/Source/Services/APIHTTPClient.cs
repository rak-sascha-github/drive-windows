using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json;
using RakutenDrive.Utils;
using RakutenDrive.Utils.CredentialManagement;
using Strings = RakutenDrive.Resources.Strings;


namespace RakutenDrive.Services;

/// <summary>
///     Provides an HTTP client for making API requests to various services with built-in
///     authorization and base URL configuration management. This class serves as a utility
///     to facilitate HTTP operations for different service functionalities.
/// </summary>
internal class APIHTTPClient
{
	// --------------------------------------------------------------------------------------------
	// PROPERTIES
	// --------------------------------------------------------------------------------------------

	private readonly string _baseURLAccount = Global.WebURL + "api";
	private readonly string _baseURLCloud   = Global.ApiURL + "cloud/service";
	private readonly string _baseURLLogger  = Global.ApiURL + "logger/service/activity/v1";

	private readonly HttpClient _client;
	private string _currentBaseURL;


	// --------------------------------------------------------------------------------------------
	// INIT
	// --------------------------------------------------------------------------------------------

	/// <summary>
	///     A utility class that simplifies HTTP communication with APIs.
	///     This class provides a set of methods for performing HTTP requests such as GET, POST, PUT, and DELETE.
	///     It encapsulates the complexities of managing the HTTP client, setting authorization headers, and handling
	///     base URL configurations. This class is used by various services for interacting with their respective APIs.
	/// </summary>
	public APIHTTPClient()
	{
		_client = new HttpClient();
		_currentBaseURL = _baseURLCloud; // Default to the first base URL
		SetAuthorizationHeader();
	}


	// --------------------------------------------------------------------------------------------
	// PUBLIC METHODS
	// --------------------------------------------------------------------------------------------

	/// <summary>
	///     Sets the current base URL for the HTTP client based on the specified parameter value.
	///     This method allows switching between predefined base URLs used by various services such as
	///     "cloud", "account", and "logger". Throws an exception if an invalid parameter value is provided.
	/// </summary>
	/// <param name="baseURLValue">The identifier for the desired base URL ("cloud", "account", or "logger").</param>
	public void SetBaseURL(string baseURLValue)
	{
		switch (baseURLValue)
		{
			case "cloud":
				_currentBaseURL = _baseURLCloud;
				break;
			case "account":
				_currentBaseURL = _baseURLAccount;
				break;
			case "logger":
				_currentBaseURL = _baseURLLogger;
				break;
			default:
				throw new ArgumentException("Invalid base URL index.");
		}
	}


	/// <summary>
	///     Sets the Authorization header for the HttpClient instance using a JWT token retrieved from secure storage.
	///     If a valid token is available, it is applied to the client's DefaultRequestHeaders as a Bearer token.
	///     This method ensures that subsequent API requests include the proper authentication credentials.
	/// </summary>
	public void SetAuthorizationHeader()
	{
		var jwtToken = TokenStorage.GetAccessToken();
		if (!string.IsNullOrEmpty(jwtToken))
		{
			_client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtToken);
		}
	}


	/// <summary>
	///     Sends an asynchronous GET request to the specified URL and retrieves the response content as a string.
	///     Automatically appends the current base URL to the requested endpoint and includes relevant headers such as authorization.
	///     Handles request exceptions and ensures a successful HTTP response.
	/// </summary>
	/// <param name="url">The relative endpoint or path to which the GET request is to be made.</param>
	/// <param name="cancellationToken">A token to observe for cancellation of the operation. This is optional and defaults to CancellationToken.None.</param>
	/// <returns>A task representing the asynchronous operation. The task result contains the response content as a string if the request is successful; otherwise, null.</returns>
	public async Task<string?> GetAsync(string url, CancellationToken cancellationToken = default)
	{
		try
		{
			var response = await _client.GetAsync(_currentBaseURL + url, cancellationToken);
			response.EnsureSuccessStatusCode();
			return await response.Content.ReadAsStringAsync(cancellationToken);
		}
		catch (HttpRequestException e)
		{
			Log.Error($@"Request error: {e.Message}");
			return null;
		}
	}


	/// <summary>
	///     Sends an HTTP POST request to the specified URL with the provided JSON content
	///     and retrieves the response content and status code.
	/// </summary>
	/// <param name="url">The endpoint URL to which the HTTP POST request is sent.</param>
	/// <param name="payload">The payload to be sent as JSON in the body of the request.</param>
	/// <param name="isAcceptException">
	///     A boolean value indicating whether exceptions should be handled internally
	///     or allowed to propagate. Defaults to false.
	/// </param>
	/// <param name="cancellationToken">A token that can be used to cancel the request. Defaults to None.</param>
	/// <returns>
	///     A tuple containing the response content as a string and the HTTP status code of the response.
	///     If an error occurs, the content will be null, and the status code may indicate the error.
	/// </returns>
	public async Task<(string? content, HttpStatusCode statusCode)> PostAsync(string url, object payload, bool isAcceptException = false, CancellationToken cancellationToken = default)
	{
		Log.Debug($"PostAsync request: {url}");

		var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
		var response = await _client.PostAsync(_currentBaseURL + url, content, cancellationToken);
		
		Log.Debug($"RESPONSE: {response.Headers}");

		if (!isAcceptException)
		{
			try
			{
				response.EnsureSuccessStatusCode();
			}
			catch (HttpRequestException e)
			{
				Log.Warn($"Could not ensure success status code. HTTP request error: {e.Message}, URL: {url} ...");
				if (response.StatusCode == HttpStatusCode.Forbidden)
				{
					UIUtil.ShowMessageBox(string.Format(Strings.error_no_permission_message_download), Strings.error_no_permission_title, MessageBoxButton.OK, MessageBoxImage.Error, false);
					return (null, HttpStatusCode.Forbidden);
				}
			}
		}

		var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
#if DEBUG
		//Log.Debug($"RESPONSE: {JSONUtil.Pretty(responseContent)}");
#endif

		switch (response.StatusCode)
		{
			case HttpStatusCode.OK:
			case HttpStatusCode.Created:
			case HttpStatusCode.Accepted:
			case HttpStatusCode.NonAuthoritativeInformation:
			case HttpStatusCode.NoContent:
			case HttpStatusCode.ResetContent:
			case HttpStatusCode.PartialContent:
			case HttpStatusCode.MultiStatus:
			case HttpStatusCode.AlreadyReported:
				return (responseContent, response.StatusCode);
			
			case HttpStatusCode.BadRequest:
				Log.Error($"PostAsync 400 bad request: {responseContent}");
				break;
			
			case HttpStatusCode.Unauthorized:
				Log.Error($"PostAsync 401 unauthorized: {responseContent}. Trying to refresh token ...");
				/* If the request fails as an activity log request with 401 it is likely due to password change. */
				var (success, newRefreshToken) = await TryRefreshToken();
				if (success && newRefreshToken != null)
				{
					var newPayload = new { refresh_token = newRefreshToken };
					return await PostAsync(url, newPayload, isAcceptException, cancellationToken);
				}
				return (null, HttpStatusCode.Unauthorized);
			
			case HttpStatusCode.Forbidden:
				Log.Error($"PostAsync 403 forbidden: {responseContent}");
				UIUtil.ShowMessageBox(string.Format(Strings.error_no_permission_message_download), Strings.error_no_permission_title, MessageBoxButton.OK, MessageBoxImage.Error, false);
				return (null, HttpStatusCode.Forbidden);
			
			case HttpStatusCode.InternalServerError:
				/* Should only force logout if response comes from auth/refreshtoken! */
				if (url.EndsWith("auth/refreshtoken"))
				{
					Log.Error("PostAsync 500 Internal Server Error. Trying to log out ...");
					_ = await TryForceLogout(url, responseContent);
				}
				break;
			
			default:
				Log.Error($"PostAsync Error: {response.StatusCode}");
				break;
		}

		return (null, HttpStatusCode.BadRequest);
	}


	/// <summary>
	///     Executes an HTTP PUT request to the specified URL with the provided JSON content.
	///     This method sends a PUT request using the current base URL and handles serialization of the JSON payload.
	///     It also returns the response content and the HTTP status code as a tuple.
	/// </summary>
	/// <param name="url">The relative endpoint URL for the HTTP PUT request.</param>
	/// <param name="jsonContent">The object to be serialized into JSON and sent as the request body.</param>
	/// <param name="isAcceptException">Indicates whether HTTP exceptions (e.g., non-success status codes) should be suppressed.</param>
	/// <returns>
	///     A tuple containing the response content as a string and the HTTP status code.
	///     If an exception occurs, the content may be null and the status code will default to BadRequest.
	/// </returns>
	public async Task<(string? content, HttpStatusCode statusCode)> PutAsync(string url, object jsonContent, bool isAcceptException = false)
	{
		try
		{
			Log.Debug($"PutAsync request: {url}");

			HttpContent content = new StringContent(JsonConvert.SerializeObject(jsonContent), Encoding.UTF8, "application/json");
			var response = await _client.PutAsync(_currentBaseURL + url, content);

			if (!isAcceptException)
			{
				response.EnsureSuccessStatusCode();
			}

			var responseContent = await response.Content.ReadAsStringAsync();
			return (responseContent, response.StatusCode);
		}
		catch (HttpRequestException e)
		{
			Log.Error($"PutAsync request error: {e.Message}");
			return (null, e.StatusCode ?? HttpStatusCode.BadRequest);
		}
	}


	/// <summary>
	///     Sends an HTTP DELETE request to the specified URL with optional JSON content, returning the response content and status code.
	///     This method is used for deleting resources at a given endpoint, optionally including a payload
	///     with additional data in the request body.
	/// </summary>
	/// <param name="url">The relative URL to which the DELETE request is sent.</param>
	/// <param name="jsonContent">Optional object to be serialized into JSON and included in the request body.</param>
	/// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
	/// <returns>A tuple containing the response content as a string and the HTTP status code.</returns>
	public async Task<(string? content, HttpStatusCode statusCode)> DeleteAsync(string url, object? jsonContent = null, CancellationToken cancellationToken = default)
	{
		try
		{
			Log.Debug($"DeleteAsync request: {url}");

			var request = new HttpRequestMessage(HttpMethod.Delete, _currentBaseURL + url) { Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionOrLower };
			HttpContent? content = jsonContent != null ? new StringContent(JsonConvert.SerializeObject(jsonContent), Encoding.UTF8, "application/json") : null;

			if (content != null)
			{
				request.Content = content;
			}

			var response = await _client.SendAsync(request, cancellationToken);
			response.EnsureSuccessStatusCode();

			var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
			return (responseContent, response.StatusCode);
		}
		catch (HttpRequestException e)
		{
			Log.Error($"DeleteAsync request error: {e.Message}");
			return (null, e.StatusCode ?? HttpStatusCode.BadRequest);
		}
		catch (Exception e)
		{
			Log.Error($"DeleteAsync request error: {e.Message}");
			return (null, HttpStatusCode.BadRequest);
		}
	}


	// --------------------------------------------------------------------------------------------
	// PRIVATE METHODS
	// --------------------------------------------------------------------------------------------

	/// <summary>
	///     Attempts to refresh the access token using the stored refresh token.
	///     This method interacts with the account service to obtain a new access
	///     token and refresh token if the current refresh token is valid. If the
	///     operation is successful, the retrieved tokens are stored for future use.
	/// </summary>
	/// <returns>
	///     A tuple where the first value indicates whether the operation was successful
	///     and the second value contains the new refresh token if the operation succeeded,
	///     or null if it failed.
	/// </returns>
	private async Task<(bool, string?)> TryRefreshToken()
	{
		var refreshToken = TokenStorage.GetRefreshToken();
		if (!string.IsNullOrEmpty(refreshToken))
		{
			var accountService = new AccountService();
			var refreshTokenObj = new { refresh_token = refreshToken };
			var refreshResponse = await accountService.GetRefreshToken(refreshTokenObj);

			if (refreshResponse != null && refreshResponse.IDToken != null && refreshResponse.RefreshToken != null)
			{
				TokenStorage.SetAccessToken(refreshResponse.IDToken);
				TokenStorage.SetRefreshToken(refreshResponse.RefreshToken);
				return (true, refreshResponse.RefreshToken);
			}
			
			Log.Error($"Try refreshing token failed: {refreshResponse}, {refreshResponse?.IDToken}, {refreshResponse?.RefreshToken}");
			return (false, null);
		}

		Log.Error("Try refreshing token failed: refreshToken is null or empty.");
		return (false, null);
	}


	/// <summary>
	///     Attempts to forcefully log the user out based on certain conditions.
	///     This method checks specific criteria in the server's response to determine if the user
	///     should be logged out. If the criteria are met, it triggers the logout process, showing
	///     an error message to the user and performing cleanup tasks.
	/// </summary>
	/// <param name="url">The URL associated with the request that resulted in the error.</param>
	/// <param name="responseContent">The content of the server's response, which may contain additional information about the error.</param>
	/// <returns>
	///     A boolean value indicating whether the user was forcefully logged out.
	///     Returns <c>true</c> if logout was performed, otherwise <c>false</c>.
	/// </returns>
	private async Task<bool> TryForceLogout(string url, string? responseContent)
	{
		/* Check if user should be force-logged-out. */
		if (responseContent != null)
		{
			using var doc = JsonDocument.Parse(responseContent);
			var root = doc.RootElement;

			if (JSONUtil.TryFindString(root, "message", out var value) && value != null && value.Contains("no user exists with the uid"))
			{
				/* Log out! */
				Log.Info("No user with UID found. Logging out ...");
				await App.Instance.Logout();
				UIUtil.ShowMessageBox(Strings.error_force_logout_message, Strings.error_force_logout_title);
				return true;
			}
		}
		else
		{
			Log.Error("TryForceLogout: Response content is null.");
			await App.Instance.Logout();
			UIUtil.ShowMessageBox(Strings.error_force_logout_message, Strings.error_force_logout_title);
		}

		return false;
	}
}
