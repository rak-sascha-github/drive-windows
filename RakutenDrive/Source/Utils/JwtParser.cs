using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Text.Json;


namespace RakutenDrive.Utils;

/// <summary>
///     Represents the payload information extracted from a JSON Web Token (JWT).
/// </summary>
/// <remarks>
///     This class is used to encapsulate specific user-related data
///     extracted from a JWT during parsing. It includes properties such as
///     user's email, display name, team ID, user ID, profile picture URL, and
///     refresh token where applicable.
/// </remarks>
public class JWTPayloadInfo
{
	public string? Email { get; set; }
	public string? DisplayName { get; set; }
	public string? TeamID { get; set; }
	public string? UserID { get; set; }
	public string? Picture { get; set; }
	public string? RefreshToken { get; set; }
}


/// <summary>
///     Provides utilities for parsing JSON Web Tokens (JWTs) and extracting their payload information.
/// </summary>
/// <remarks>
///     This class includes methods for parsing a JWT to retrieve its payload as a structured object
///     and determining whether a given JWT has expired. The parsing process validates the token format
///     and ensures all required claims are present in the payload. Typical use cases involve validating tokens
///     and extracting relevant claims such as team ID, email address, user ID, etc.
/// </remarks>
internal class JWTParser
{
	// --------------------------------------------------------------------------------------------
	// PUBLIC METHODS
	// --------------------------------------------------------------------------------------------


	/// <summary>
	///     Parses a JWT token and extracts its payload information.
	/// </summary>
	/// <param name="token">The JWT token to be parsed.</param>
	/// <returns>A <see cref="JWTPayloadInfo" /> object containing the extracted payload information.</returns>
	/// <exception cref="ArgumentException">
	///     Thrown if the provided token is invalid or does not contain the required properties
	///     such as TeamID, Email, Name, or UserID.
	/// </exception>
	public static JWTPayloadInfo Parse(string? token)
	{
		if (string.IsNullOrEmpty(token))
		{
			throw new ArgumentException("JWT token is null or empty.");
		}

		var handler = new JwtSecurityTokenHandler();
		var jsonToken = handler.ReadToken(token) as JwtSecurityToken;

		if (jsonToken == null)
		{
			throw new ArgumentException("Invalid JWT token.");
		}

		var payload = jsonToken.Payload;
		var jsonPayload = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

		using (var doc = JsonDocument.Parse(jsonPayload))
		{
			var root = doc.RootElement;
			string? teamID = null;

			/* Check if team_id is at the root level. */
			if (root.TryGetProperty("team_id", out var teamIDElement))
			{
				teamID = teamIDElement.GetString();
			}
			/* Check if team_id is inside customClaims. */
			else if (root.TryGetProperty("customClaims", out var customClaimsElement) && customClaimsElement.TryGetProperty("team_id", out teamIDElement))
			{
				teamID = teamIDElement.GetString();
			}

			if (teamID != null)
			{
				if (root.TryGetProperty("email", out var emailElement) && root.TryGetProperty("user_id", out var userIDElement))
				{
					string? displayName = null;
					if (root.TryGetProperty("name", out var displayNameElement))
					{
						displayName = displayNameElement.GetString();
					}
					
					var email = emailElement.GetString();
					var pictureAvatar = string.Empty;
					var userID = userIDElement.GetString();

					if (root.TryGetProperty("picture", out var pictureElement))
					{
						pictureAvatar = pictureElement.GetString();
					}

					return new JWTPayloadInfo
					{
						Email       = email,
						DisplayName = displayName,
						TeamID      = teamID,
						Picture     = pictureAvatar,
						UserID      = userID
					};
				}

				if (root.TryGetProperty("refreshToken", out var refreshTokenElement))
				{
					var refreshToken = refreshTokenElement.GetString();
					return new JWTPayloadInfo { RefreshToken = refreshToken };
				}
			}
			else
			{
				NotificationHelper.ShowError("Please login with an account that has TeamDrive capability.");
				throw new ArgumentException("Team Drive feature is required.");
			}
		}

		throw new ArgumentException("Required properties (TeamID, Email, Name or UserID) not found in JWT token.");
	}


	/// <summary>
	///     Determines whether a given JWT token is expired based on its "exp" (expiration) claim.
	/// </summary>
	/// <param name="token">The JWT token to be checked for expiration.</param>
	/// <returns>
	///     A boolean value indicating whether the token is expired (true) or not (false).
	/// </returns>
	/// <exception cref="ArgumentException">
	///     Thrown if the provided token is not in a valid JWT format, or if it does not contain the required "exp" claim.
	/// </exception>
	public static bool IsTokenExpired(string token)
	{
		var jwtHandler = new JwtSecurityTokenHandler();

		if (!jwtHandler.CanReadToken(token))
		{
			throw new ArgumentException("The token doesn't seem to be in a proper JWT format.");
		}

		var jwtToken = jwtHandler.ReadJwtToken(token);
		var exp = jwtToken.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Exp)?.Value;

		if (exp == null)
		{
			throw new ArgumentException("The token doesn't contain an 'exp' claim.");
		}

		var expDateTime = DateTimeOffset.FromUnixTimeSeconds(long.Parse(exp)).UtcDateTime;
		return expDateTime < DateTime.UtcNow;
	}
}
