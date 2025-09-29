using System;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json;
using RakutenDrive.Models;
using RakutenDrive.Utils;


namespace RakutenDrive.Services;

/// <summary>
///     Provides services related to user account operations.
/// </summary>
internal class AccountService
{
	// --------------------------------------------------------------------------------------------
	// PROPERTIES
	// --------------------------------------------------------------------------------------------

	private readonly string _baseSubURL = "/v1/auth";


	// --------------------------------------------------------------------------------------------
	// METHODS
	// --------------------------------------------------------------------------------------------


	/// <summary>
	///     Sends a request to refresh the authentication token with the provided refresh token payload.
	/// </summary>
	/// <param name="refreshToken">The payload containing the refresh token required for generating a new token.</param>
	/// <returns>A <see cref="RefreshTokenResponse" /> object containing updated token information if successful; otherwise, null.</returns>
	public async Task<RefreshTokenResponse?> GetRefreshToken(object refreshToken)
	{
		try
		{
			var apiHTTPClient = new APIHTTPClient();
			apiHTTPClient.SetBaseURL("account");
			
			var (userInfoResponse, statusCode) = await apiHTTPClient.PostAsync($"{_baseSubURL}/refreshtoken", refreshToken);
			if (userInfoResponse != null)
			{
				var deserializedObj = JsonConvert.DeserializeObject<RefreshTokenResponse>(userInfoResponse);
				if (deserializedObj == null)
				{
					Log.Warn("GetRefreshToken: Refresh token is null.");
				}
				
				return deserializedObj;
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show($"An error occured trying to refresh token: {ex}");
			return null;
		}

		return null;
	}
}
