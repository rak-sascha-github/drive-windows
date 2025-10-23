using System;
using System.Collections.Generic;
using Newtonsoft.Json;


namespace RakutenDrive.Models;

/// <summary>
///     Represents the response data obtained after refreshing an authentication token.
/// </summary>
public class RefreshTokenResponse
{
	[JsonProperty("uid")]
	public string? UID { get; set; }

	[JsonProperty("email")]
	public string? Email { get; set; }

	[JsonProperty("emailVerified")]
	public bool EmailVerified { get; set; }

	[JsonProperty("displayName")]
	public string? DisplayName { get; set; }

	[JsonProperty("photoURL")]
	public string? PhotoURL { get; set; }

	[JsonProperty("disabled")]
	public bool Disabled { get; set; }

	[JsonProperty("metadata")]
	public Metadata? Metadata { get; set; }

	[JsonProperty("providerData")]
	public List<ProviderData>? ProviderData { get; set; }

	[JsonProperty("customClaims")]
	public CustomClaims? CustomClaims { get; set; }

	[JsonProperty("tokensValidAfterTime")]
	public long TokensValidAfterTimeUnix { get; set; }

	[JsonIgnore]
	public DateTime TokensValidAfterTime => DateTimeOffset.FromUnixTimeMilliseconds(TokensValidAfterTimeUnix).DateTime;

	[JsonProperty("refreshToken")]
	public string? RefreshToken { get; set; }

	[JsonProperty("idToken")]
	public string? IDToken { get; set; }
}


/// <summary>
///     Represents metadata information related to a user's authentication or account,
///     including details such as last sign-in time and account creation time.
/// </summary>
public class Metadata
{
	[JsonProperty("lastSignInTime")]
	public long LastSignInTimeUnix { get; set; }

	[JsonIgnore]
	public DateTime LastSignInTime => DateTimeOffset.FromUnixTimeMilliseconds(LastSignInTimeUnix).DateTime;

	[JsonProperty("creationTime")]
	public long CreationTimeUnix { get; set; }

	[JsonIgnore]
	public DateTime CreationTime => DateTimeOffset.FromUnixTimeMilliseconds(CreationTimeUnix).DateTime;
}


/// <summary>
///     Represents data associated with a specific authentication provider for a user,
///     including details such as the provider ID, user ID, display name, email, and photo URL.
/// </summary>
public class ProviderData
{
	[JsonProperty("uid")]
	public string? UID { get; set; }

	[JsonProperty("displayName")]
	public string? DisplayName { get; set; }

	[JsonProperty("email")]
	public string? Email { get; set; }

	[JsonProperty("photoURL")]
	public string? PhotoURL { get; set; }

	[JsonProperty("providerId")]
	public string? ProviderID { get; set; }
}


/// <summary>
///     Represents custom claims that are associated with a user, providing additional
///     attributes such as team identifiers or other context-specific information.
/// </summary>
public class CustomClaims
{
	[JsonProperty("team_id")]
	public string? TeamID { get; set; }
}
