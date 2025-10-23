using System.Collections.Generic;
using Newtonsoft.Json;


namespace RakutenDrive.Models;

/// <summary>
///     Represents a user or team entity and their associated permissions within a shared context.
/// </summary>
public class Collaborator
{
	[JsonProperty("user_id")]
	public string? UserID { get; set; }

	[JsonProperty("team_id")]
	public string? TeamID { get; set; }

	[JsonProperty("email")]
	public string? Email { get; set; }

	[JsonProperty("access_level")]
	[JsonConverter(typeof(AccessLevelConverter))]
	public AccessLevel AccessLevel { get; set; }
}


/// <summary>
///     Represents the request model used to share files or folders, including details about the host,
///     target path, optional message, and the list of collaborators involved in the sharing operation.
/// </summary>
public class SharingRequestModel
{
	[JsonProperty("host_id")]
	public string? HostID { get; set; }

	[JsonProperty("path")]
	public string? Path { get; set; }

	[JsonProperty("message")]
	public string? Message { get; set; }

	[JsonProperty("collaborator_list")]
	public List<Collaborator>? CollaboratorList { get; set; }
}
