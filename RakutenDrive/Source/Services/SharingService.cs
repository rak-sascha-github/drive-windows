using System.Threading.Tasks;
using Newtonsoft.Json;
using RakutenDrive.Models;


namespace RakutenDrive.Services;

/// <summary>
///     Provides services for sharing folders or files within the RakutenDrive platform.
/// </summary>
internal class SharingService
{
	// --------------------------------------------------------------------------------------------
	// PROPERTIES
	// --------------------------------------------------------------------------------------------

	private const string BASE_SUB_URL = "/sharing";


	// --------------------------------------------------------------------------------------------
	// PUBLIC METHODS
	// --------------------------------------------------------------------------------------------


	/// <summary>
	///     Shares a folder or file within the RakutenDrive platform by sending the specified payload to the server.
	/// </summary>
	/// <param name="payload">An object containing the necessary details for sharing the target folder or file.</param>
	/// <returns>A <see cref="SharingReponse" /> object containing the details of the shared folder or file, including a generated link. Returns null if the sharing operation fails.</returns>
	public async Task<SharingReponse?> ShareFolderOrFile(object payload)
	{
		var apiHttpClient = new APIHTTPClient();
		var (sharingResponse, statusCode) = await apiHttpClient.PostAsync($"{BASE_SUB_URL}/v1/folders/share", payload);
		
		if (sharingResponse != null)
		{
			return JsonConvert.DeserializeObject<SharingReponse>(sharingResponse);
		}

		return null;
	}
}
