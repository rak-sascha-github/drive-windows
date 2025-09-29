using System;
using System.Collections.Generic;
using Newtonsoft.Json;


namespace RakutenDrive.Models;

/// <summary>
///     Represents the user information, including settings, subscription plan details,
///     and the current state of the user's account.
/// </summary>
public class UserInfo
{
	[JsonProperty("settings")]
	public required Settings Settings { get; set; }

	[JsonProperty("plan_info")]
	public required PlanInfo PlanInfo { get; set; }

	[JsonProperty("state")]
	public string? State { get; set; }
}


/// <summary>
///     Represents the user settings, including storage configurations, challenges, region details, and metadata.
/// </summary>
public class Settings
{
	[JsonProperty("trash_empty_cycle")]
	public int TrashEmptyCycle { get; set; }

	[JsonProperty("space")]
	public Space? Space { get; set; }

	[JsonProperty("challenge_list")]
	public List<Challenge>? ChallengeList { get; set; }

	[JsonProperty("region")]
	public string? Region { get; set; }

	[JsonProperty("bucket")]
	public string? Bucket { get; set; }

	[JsonProperty("team_id")]
	public string? TeamID { get; set; }

	[JsonProperty("created_time")]
	public DateTime CreatedTime { get; set; }

	[JsonProperty("updated_time")]
	public DateTime UpdatedTime { get; set; }
}


/// <summary>
///     Represents information related to a subscription plan, including its name and associated maximum storage space.
/// </summary>
public class PlanInfo
{
	[JsonProperty("plan")]
	public string? Plan { get; set; }

	[JsonProperty("max_space")]
	public MaxSpace? MaxSpace { get; set; }
}


/// <summary>
///     Represents the storage space information and associated categories.
/// </summary>
public class Space
{
	[JsonProperty("bonus")]
	public long Bonus { get; set; }

	[JsonProperty("challenge")]
	public long Challenge { get; set; }

	[JsonProperty("invitation")]
	public long Invitation { get; set; }

	[JsonProperty("plan")]
	public long Plan { get; set; }
}


/// <summary>
///     Represents a challenge with a specific name and its accomplishment timestamp.
/// </summary>
public class Challenge
{
	[JsonProperty("name")]
	public string? Name { get; set; }

	[JsonProperty("accomplish_at")]
	public DateTime AccomplishAt { get; set; }
}


/// <summary>
///     Represents the maximum storage space limit associated with a plan.
/// </summary>
public class MaxSpace
{
	[JsonProperty("plan")]
	public long Plan { get; set; }
}


/// <summary>
///     Represents the response of a sharing operation within the system,
///     providing details such as the generated shareable link.
/// </summary>
public class SharingReponse
{
	[JsonProperty("link")]
	public string? Link { get; set; }
}
