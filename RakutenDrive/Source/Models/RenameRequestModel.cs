using System;
using Newtonsoft.Json;


namespace RakutenDrive.Models;

/// <summary>
///     Represents the request model for renaming a file or directory.
/// </summary>
public class RenameRequestModel
{
	[JsonProperty("name")]
	public required string Name { get; set; }

	[JsonProperty("file")]
	public required FileDetails File { get; set; }

	[JsonProperty("prefix")]
	public string? Prefix { get; set; }
}


/// <summary>
///     Represents detailed information about a file, including path, size, version, and last modified date.
/// </summary>
public class FileDetails
{
	[JsonProperty("path")]
	public required string Path { get; set; }

	[JsonProperty("size")]
	public int Size { get; set; }

	[JsonProperty("version_id")]
	public string? VersionID { get; set; }

	[JsonProperty("last_modified")]
	public DateTime LastModified { get; set; }
}
