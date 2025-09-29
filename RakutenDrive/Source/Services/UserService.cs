using System.Threading.Tasks;
using Newtonsoft.Json;
using RakutenDrive.Models;


namespace RakutenDrive.Services;

/// <summary>
///     The UserServices class provides functionalities to interact with the user-related data
///     in the RakutenDrive service. This class acts as a utility for fetching information regarding
///     specific users by integrating with the appropriate API endpoints.
/// </summary>
internal class UserService
{
	// --------------------------------------------------------------------------------------------
	// PROPERTIES
	// --------------------------------------------------------------------------------------------

	private const string BASE_SUB_URL = "/user/v1/users";


	// --------------------------------------------------------------------------------------------
	// PUBLIC METHODS
	// --------------------------------------------------------------------------------------------


	/// <summary>
	///     Retrieves user information for a specific user based on their unique identifier.
	/// </summary>
	/// <param name="userID">The unique identifier of the user whose information is to be retrieved.</param>
	/// <returns>A task representing the asynchronous operation. When completed, contains a <see cref="UserInfo" /> object if the user exists, otherwise null.</returns>
	public async Task<UserInfo?> GetUserInfo(string userID)
	{
		var apiHttpClient = new APIHTTPClient();
		var userInfoResponse = await apiHttpClient.GetAsync($"{BASE_SUB_URL}/{userID}/info");
		if (userInfoResponse != null)
		{
			return JsonConvert.DeserializeObject<UserInfo>(userInfoResponse);
		}

		return null;
	}
}
