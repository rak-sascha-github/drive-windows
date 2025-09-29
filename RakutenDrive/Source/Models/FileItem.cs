using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RakutenDrive.Utils;
using EnumExtensions = Vanara.Extensions.EnumExtensions;


namespace RakutenDrive.Models;

/// <summary>
///     Represents a file or folder item, encapsulating its metadata and properties in a cloud storage system.
/// </summary>
public class FileItem
{
	// --------------------------------------------------------------------------------------------
	// PROPERTIES
	// --------------------------------------------------------------------------------------------
	
	[JsonProperty("Id")]
	public required string ID { get; set; }

	[JsonProperty("Path")]
	public required string Path { get; set; }

	[JsonProperty("Size")]
	public long Size { get; set; }

	[JsonProperty("ItemsCount")]
	public int ItemsCount { get; set; }

	[JsonProperty("IsFolder")]
	public bool IsFolder { get; set; }

	[JsonProperty("LastModified")]
	public DateTime LastModified { get; set; }

	[JsonProperty("Thumbnail")]
	public required string Thumbnail { get; set; }

	[JsonProperty("IsShare")]
	public required string IsShare { get; set; }

	[JsonProperty("VersionID")]
	public required string VersionID { get; set; }

	[JsonProperty("HasChildFolder")]
	public bool HasChildFolder { get; set; }

	[JsonProperty("IsLatest")]
	public bool IsLatest { get; set; }

	[JsonProperty("LastModifierID")]
	public required string LastModifierID { get; set; }

	[JsonProperty("AccessLevel")]
	[JsonConverter(typeof(AccessLevelConverter))]
	public AccessLevel AccessLevel { get; set; }

	[JsonProperty("HostID")]
	public required string HostID { get; set; }

	[JsonProperty("OwnerID")]
	public required string OwnerID { get; set; }

	[JsonProperty("Version")]
	[JsonConverter(typeof(VersionConverter))]
	public VersionInfo? Version { get; set; }

	[JsonProperty("IsBackedUp")]
	public bool IsBackedUp { get; set; }

	[JsonProperty("UserTags", NullValueHandling = NullValueHandling.Include)]
	public required string[] UserTags { get; set; }

	[JsonIgnore]
	public string LocalPath => Path.ConvertToLocalPath();

	[JsonIgnore]
	public string NormalizedPath => Path.Normalize(NormalizationForm.FormC);
}


// --------------------------------------------------------------------------------------------
// NESTED CLASSES
// --------------------------------------------------------------------------------------------

/// <summary>
///     Provides extension methods for converting cloud file paths into local file system paths.
/// </summary>
public static class CloudFilePathExtension
{
	public static string ConvertToLocalPath(this string cloudPath)
	{
		var rootDir = StringHelper.GetFullPathRootFolder();
		if (!rootDir.EndsWith('\\'))
		{
			rootDir += '\\';
		}

		return rootDir + cloudPath.TrimEnd('/').Replace("/", "\\");
	}
}


// --------------------------------------------------------------------------------------------
// ENUMS
// --------------------------------------------------------------------------------------------

/// <summary>
///     Specifies the level of access permissions for a user or collaborator within a system or file-sharing context.
/// </summary>
public enum AccessLevel
{
	[Description("viewer")]
	Viewer,

	[Description("downloader")]
	Downloader,

	[Description("uploader")]
	Uploader,

	[Description("editor")]
	Editor,

	[Description("team_viewer")]
	TeamViewer,

	[Description("team_commenter")]
	TeamCommenter,

	[Description("team_downloader")]
	TeamDownloader,

	[Description("team_collaborator")]
	TeamCollaborator,

	[Description("team_editor")]
	TeamEditor,

	[Description("team_manager")]
	TeamManager,

	[Description("team_creator")]
	TeamCreator
}


// --------------------------------------------------------------------------------------------
// HELPERS
// --------------------------------------------------------------------------------------------

/// <summary>
///     Provides a set of extension methods for the <see cref="AccessLevel" /> enumeration,
///     enabling utility functionality and access checks associated with different access levels.
/// </summary>
public static class AccessLevelExtensions
{
	public static AccessLevel? GetAccessLevelFromDescription(string description)
	{
		foreach (var field in typeof(AccessLevel).GetFields())
		{
			if (Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute)) is DescriptionAttribute attribute)
			{
				if (attribute.Description == description)
				{
					return (AccessLevel)(field.GetValue(null) ?? AccessLevel.Viewer);
				}
			}
			else
			{
				if (field.Name == description)
				{
					return (AccessLevel)(field.GetValue(null) ?? AccessLevel.Viewer) ;
				}
			}
		}

		return null;
	}


	/// <summary>
	///     Determines whether the specified <see cref="AccessLevel" /> allows downloading functionality.
	/// </summary>
	/// <param name="accessLevel">The access level to check for download permissions.</param>
	/// <returns>
	///     A boolean value indicating whether the provided <see cref="AccessLevel" /> grants download permissions.
	/// </returns>
	public static bool CanDownload(this AccessLevel accessLevel)
	{
		AccessLevel[] list =
		[
			AccessLevel.Editor,
			AccessLevel.Downloader,
			AccessLevel.Uploader,
			AccessLevel.TeamCreator,
			AccessLevel.TeamManager,
			AccessLevel.TeamEditor,
			AccessLevel.TeamCollaborator,
			AccessLevel.TeamDownloader
		];
		return list.Contains(accessLevel);
	}


	/// <summary>
	///     Determines whether the specified <see cref="AccessLevel" /> allows editing functionality.
	/// </summary>
	/// <param name="accessLevel">The access level to check for edit permissions.</param>
	/// <returns>
	///     A boolean value indicating whether the provided <see cref="AccessLevel" /> grants edit permissions.
	/// </returns>
	public static bool CanEdit(this AccessLevel accessLevel)
	{
		AccessLevel[] list =
		[
			AccessLevel.Editor,
			AccessLevel.Uploader,
			AccessLevel.TeamCreator,
			AccessLevel.TeamManager,
			AccessLevel.TeamEditor,
			AccessLevel.TeamCollaborator
		];
		return list.Contains(accessLevel);
	}


	/// <summary>
	///     Determines whether the specified <see cref="AccessLevel" /> allows creating folders.
	/// </summary>
	/// <param name="accessLevel">The access level to check for folder creation permissions.</param>
	/// <returns>
	///     A boolean value indicating whether the provided <see cref="AccessLevel" /> grants permissions to create folders.
	/// </returns>
	public static bool CanCreateFolder(this AccessLevel accessLevel)
	{
		return CanEdit(accessLevel);
	}


	/// <summary>
	///     Determines whether the specified <see cref="AccessLevel" /> allows uploading functionality.
	/// </summary>
	/// <param name="accessLevel">The access level to check for upload permissions.</param>
	/// <returns>
	///     A boolean value indicating whether the provided <see cref="AccessLevel" /> grants upload permissions.
	/// </returns>
	public static bool CanUpload(this AccessLevel accessLevel)
	{
		return CanEdit(accessLevel);
	}


	/// <summary>
	///     Determines whether the specified <see cref="AccessLevel" /> allows renaming functionality.
	/// </summary>
	/// <param name="accessLevel">The access level to check for rename permissions.</param>
	/// <returns>
	///     A boolean value indicating whether the provided <see cref="AccessLevel" /> grants rename permissions.
	/// </returns>
	public static bool CanRename(this AccessLevel accessLevel)
	{
		return CanEdit(accessLevel);
	}


	/// <summary>
	///     Determines whether the specified <see cref="AccessLevel" /> allows deleting functionality.
	/// </summary>
	/// <param name="accessLevel">The access level to check for delete permissions.</param>
	/// <returns>
	///     A boolean value indicating whether the provided <see cref="AccessLevel" /> grants delete permissions.
	/// </returns>
	public static bool CanDelete(this AccessLevel accessLevel)
	{
		AccessLevel[] list =
		[
			AccessLevel.Editor,
			AccessLevel.TeamCreator,
			AccessLevel.TeamManager,
			AccessLevel.TeamEditor
		];
		return list.Contains(accessLevel);
	}


	/// <summary>
	///     Determines whether the specified <see cref="AccessLevel" /> allows moving functionality.
	/// </summary>
	/// <param name="accessLevel">The access level to check for move permissions.</param>
	/// <returns>
	///     A boolean value indicating whether the provided <see cref="AccessLevel" /> grants move permissions.
	/// </returns>
	public static bool CanMove(this AccessLevel accessLevel)
	{
		return CanDelete(accessLevel);
	}


	/// <summary>
	///     Determines whether the specified <see cref="AccessLevel" /> allows copying functionality.
	/// </summary>
	/// <param name="accessLevel">The access level to check for copy permissions.</param>
	/// <returns>
	///     A boolean value indicating whether the provided <see cref="AccessLevel" /> grants copy permissions.
	/// </returns>
	public static bool CanCopy(this AccessLevel accessLevel)
	{
		return CanDownload(accessLevel);
	}


	/// <summary>
	///     Determines whether the specified <see cref="AccessLevel" /> allows sending functionality.
	/// </summary>
	/// <param name="accessLevel">The access level to check for sending permissions.</param>
	/// <returns>
	///     A boolean value indicating whether the provided <see cref="AccessLevel" /> grants sending permissions.
	/// </returns>
	public static bool CanSend(this AccessLevel accessLevel)
	{
		return CanDownload(accessLevel);
	}


	/// <summary>
	///     Determines whether the specified <see cref="AccessLevel" /> permits management capabilities within the system.
	/// </summary>
	/// <param name="accessLevel">The access level to evaluate for management permissions.</param>
	/// <returns>
	///     A boolean value indicating whether the provided <see cref="AccessLevel" /> grants management permissions.
	/// </returns>
	public static bool CanManage(this AccessLevel accessLevel)
	{
		AccessLevel[] list =
		[
			AccessLevel.TeamCreator,
			AccessLevel.TeamManager
		];
		return list.Contains(accessLevel);
	}


	/// <summary>
	///     Determines whether the specified <see cref="AccessLevel" /> allows sharing functionality.
	/// </summary>
	/// <param name="accessLevel">The access level to evaluate for sharing permissions.</param>
	/// <returns>
	///     A boolean value indicating whether the provided <see cref="AccessLevel" /> grants sharing permissions.
	/// </returns>
	public static bool CanShare(this AccessLevel accessLevel)
	{
		return CanManage(accessLevel);
	}
}


/// <summary>
///     Represents a request to copy files or folders, specifying the target location, file details, and rename options.
/// </summary>
public class CopyRequest
{
	[JsonProperty("target_id")]
	public required string TargetID { get; set; }

	[JsonProperty("file")]
	public required FileRename[] File { get; set; }

	[JsonProperty("to_path")]
	public required string ToPath { get; set; }

	[JsonProperty("prefix")]
	public required string Prefix { get; set; }

	[JsonProperty("new_copy_name")]
	public required string NewCopyName { get; set; }
}


/// <summary>
///     Represents a file entity involved in rename or move operations, encapsulating its
///     path, size, version information, and last modified timestamp.
/// </summary>
public class FileRename
{
	[JsonProperty("path")]
	public required string Path { get; set; }

	[JsonProperty("size")]
	public long Size { get; set; }

	[JsonProperty("version_id")]
	public required string VersionId { get; set; }

	[JsonProperty("last_modified")]
	public string? LastModified { get; set; }
}


/// <summary>
///     Represents the response of a copy operation in the cloud storage system,
///     containing information related to the initiated copy task.
/// </summary>
public class CopyResponse
{
	[JsonProperty("task_id")]
	public required string TaskID { get; set; }
}


/// <summary>
///     Represents a request to move a file or multiple files to a different location within a cloud storage system.
///     This class encapsulates details such as the target identifier, the files to move, the destination path, and an optional path
///     prefix.
/// </summary>
public class MoveRequest
{
	[JsonProperty("target_id")]
	public required string? TargetID { get; set; }

	[JsonProperty("file")]
	public required FileRename[] File { get; set; }

	[JsonProperty("to_path")]
	public required string ToPath { get; set; }

	[JsonProperty("prefix")]
	public string? Prefix { get; set; }
}


/// <summary>
///     Represents the response received after performing a move operation on a file or folder
///     within the cloud storage system. Provides details such as the task identifier for the operation.
/// </summary>
public class MoveResponse
{
	[JsonProperty("task_id")]
	public string? TaskID { get; set; }
}


/// <summary>
///     Represents a request to rename a file or folder, encapsulating the necessary information for the operation.
/// </summary>
public class RenameRequest
{
	[JsonProperty("name")]
	public required string Name { get; set; }

	[JsonProperty("file")]
	public required FileRename File { get; set; }

	[JsonProperty("prefix")]
	public string? Prefix { get; set; }
}


/// <summary>
///     Represents the response received after renaming a file or folder.
/// </summary>
public class RenameResponse
{
	[JsonProperty("task_id")]
	public string? TaskID { get; set; }
}


/// <summary>
///     Represents the response containing the URL for viewing a file in the system.
/// </summary>
public class FileViewResponse
{
	[JsonProperty("url")]
	public string? URL { get; set; }
}


/// <summary>
///     Represents a request to retrieve information about a specific file or folder,
///     including its details and metadata within a cloud storage system.
/// </summary>
public class FileOrFolderInfoRequest
{
	[JsonProperty("host_id")]
	public required string? HostID { get; set; }

	[JsonProperty("path")]
	public required string Path { get; set; }

	[JsonProperty("is_folder_detail_info")]
	public bool IsFolderDetailInfo { get; set; }
}


/// <summary>
///     Represents a request to check the validity of file uploads within a specified path in the cloud storage system.
///     This request contains details about the files to check, the upload path, and metadata related to the upload process.
/// </summary>
public class CheckFileUploadRequest
{
	[JsonProperty("file")]
	public required List<FileInfoCheckFile> File { get; set; }

	[JsonProperty("path")]
	public required string Path { get; set; }

	[JsonProperty("upload_id")]
	public required string UploadID { get; set; }

	[JsonProperty("replace")]
	public bool Replace { get; set; }

	[JsonProperty("token")]
	public string? Token { get; set; }
}


/// <summary>
///     Represents a file information entity used for checking file upload requests, containing the file's path and size.
/// </summary>
public class FileInfoCheckFile
{
	[JsonProperty("path")]
	public required string Path { get; set; }

	[JsonProperty("size")]
	public long Size { get; set; }
}


/// <summary>
///     Represents the response received when verifying or checking the upload of a file.
///     Provides details about the upload session including storage bucket, file details,
///     prefix path, region, and upload identifier.
/// </summary>
public class CheckFileUploadResponse
{
	[JsonProperty("bucket")]
	public string? Bucket { get; set; }

	[JsonProperty("file")]
	public List<FileInfoCheckFileResponse>? File { get; set; }

	[JsonProperty("prefix")]
	public string? Prefix { get; set; }

	[JsonProperty("region")]
	public string? Region { get; set; }

	[JsonProperty("upload_id")]
	public string? UploadID { get; set; }
}


/// <summary>
///     Represents temporary AWS credentials, which include the access key, secret access key, session token, and expiration date.
/// </summary>
public class AwsTemporaryCredentials
{
	[JsonProperty("AccessKeyId")]
	public required string AccessKeyID { get; set; }

	[JsonProperty("Expiration")]
	public DateTime Expiration { get; set; }

	[JsonProperty("SecretAccessKey")]
	public required string SecretAccessKey { get; set; }

	[JsonProperty("SessionToken")]
	public required string SessionToken { get; set; }
}


/// <summary>
///     Represents the response containing detailed file information for file checks, including path, size, and associated tagging
///     metadata.
/// </summary>
public class FileInfoCheckFileResponse
{
	[JsonProperty("path")]
	public required string Path { get; set; }

	[JsonProperty("size")]
	public long Size { get; set; }

	[JsonProperty("tagging")]
	public required string Tagging { get; set; }
}


/// <summary>
///     Represents a response containing information about a file or folder,
///     including its metadata, ownership, access level, and usage details.
/// </summary>
public class FileOrFolderInfoResponse
{
	[JsonProperty("access_level")]
	public required string AccessLevel { get; set; }

	[JsonProperty("file")]
	public required FileItem File { get; set; }

	[JsonProperty("owner")]
	public required string Owner { get; set; }

	[JsonProperty("prefix")]
	public required string Prefix { get; set; }

	[JsonProperty("usage_size")]
	public long UsageSize { get; set; }
}


/// <summary>
///     Represents the response received when checking the status of a file or folder action.
///     This response includes details such as the action performed, the state of the action, and the storage usage size.
/// </summary>
public class CheckFileStatusResponse
{
	[JsonProperty("action")]
	public required string Action { get; set; }

	[JsonProperty("state")]
	public required string State { get; set; }

	[JsonProperty("usage_size")]
	public long UsageSize { get; set; }
}


/// <summary>
///     Represents a response containing metadata and details of files within a file list,
///     including file properties, access level, owner, usage size, and pagination info.
/// </summary>
public class FileListResponse
{
	[JsonProperty("access_level")]
	public required string AccessLevel { get; set; }

	[JsonProperty("count")]
	public int Count { get; set; }

	[JsonProperty("file")]
	public required List<FileItem> File { get; set; }

	[JsonProperty("last_page")]
	public bool LastPage { get; set; }

	[JsonProperty("owner")]
	public required string Owner { get; set; }

	[JsonProperty("prefix")]
	public required string Prefix { get; set; }

	[JsonProperty("usage_size")]
	public long UsageSize { get; set; }
}


/// <summary>
///     Represents information about a specific version of a file, including its metadata such as size, associated file ID,
///     modification details, and status flags such as deletion, locking, and activation states.
/// </summary>
public class VersionInfo
{
	[JsonProperty("id")]
	public string? ID { get; set; }

	[JsonProperty("file_id")]
	public string? FileID { get; set; }

	[JsonProperty("modifier")]
	public string? Modifier { get; set; }

	[JsonProperty("host_id")]
	public string? HostID { get; set; }

	[JsonProperty("version_id")]
	public string? VersionID { get; set; }

	[JsonProperty("size")]
	public long Size { get; set; }

	[JsonProperty("action")]
	public int Action { get; set; }

	[JsonProperty("tag")]
	public int Tag { get; set; }

	[JsonProperty("is_deleted")]
	public bool IsDeleted { get; set; }

	[JsonProperty("is_locked")]
	public bool IsLocked { get; set; }

	[JsonProperty("is_active")]
	public bool IsActive { get; set; }

	[JsonProperty("created")]
	public DateTime Created { get; set; }
}


/// <summary>
///     Represents parameters of a file, including its path, size, version information, and last modified timestamp.
/// </summary>
public class FileParam
{
	[JsonProperty("path")]
	public required string Path { get; set; }

	[JsonProperty("size")]
	public long Size { get; set; }

	[JsonProperty("version_id")]
	public string? VersionID { get; set; }

	[JsonProperty("last_modified")]
	public string? LastModified { get; set; }
}


/// <summary>
///     Represents the response associated with a file operation, including details such as the task identifier.
/// </summary>
public class FileOperationResponse
{
	[JsonProperty("task_id")]
	public required string TaskID { get; set; }
}


/// <summary>
///     Represents a delete request used to remove files or folders from the cloud storage system.
///     Provides options for specifying whether to move files to trash and supports targeted item deletion based on prefixes.
/// </summary>
public class DeleteRequest
{
	[JsonProperty("trash")]
	public bool Trash { get; set; }

	[JsonProperty("prefix")]
	public string? Prefix { get; set; }

	[JsonProperty("file")]
	public FileParam[]? File { get; set; }
}


/// <summary>
///     Represents a request object for performing a validation or "check" operation,
///     specifically used for identifying or verifying a resource based on a unique key.
/// </summary>
public class CheckRequest
{
	[JsonProperty("key")]
	public required string Key { get; set; }
}


/// <summary>
///     Represents the response for a state-check operation, providing the current state of the requested operation.
/// </summary>
public class CheckResponse
{
	[JsonProperty("state")]
	public string? State { get; set; }
}


/// <summary>
///     Represents the identity of a file or directory within a cloud storage system.
///     Encapsulates details like path, version identifiers, and supports serialization and deserialization operations.
/// </summary>
public class CloudFileIdentity
{
	public string LocalID { get; set; } = Guid.NewGuid().ToString();
	public string? Path { get; set; }
	public string? VersionID { get; set; }
	public string? VersionInfoID { get; set; }


	/// <summary>
	///     Serializes the current instance of the <see cref="CloudFileIdentity" /> class into a JSON string representation
	///     and returns it as a <see cref="StringPtr" /> object.
	/// </summary>
	/// <param name="maxBufferSize">
	///     An optional maximum buffer size to ensure the serialized output does not exceed the specified size.
	///     Throws an <see cref="InvalidOperationException" /> if exceeded.
	/// </param>
	/// <returns>
	///     A <see cref="StringPtr" /> containing the JSON string representation of the current <see cref="CloudFileIdentity" />
	///     instance.
	/// </returns>
	public StringPtr Serialize(uint maxBufferSize = 0)
	{
		var json = JsonConvert.SerializeObject(this);
		var ptr = new StringPtr(json);
		if (maxBufferSize > 0 && ptr.Size > maxBufferSize)
		{
			throw new InvalidOperationException();
		}

		return ptr;
	}


	/// <summary>
	///     Deserializes a <see cref="CloudFileIdentity" /> object from a given pointer to JSON data.
	/// </summary>
	/// <param name="ptr">A pointer to the memory containing the JSON representation of a <see cref="CloudFileIdentity" /> object.</param>
	/// <returns>
	///     A <see cref="CloudFileIdentity" /> object reconstructed from the provided JSON data, or null if deserialization fails.
	/// </returns>
	public static CloudFileIdentity? Deserialize(IntPtr ptr)
	{
		var json = new StringPtr(ptr).Data;
		return JsonConvert.DeserializeObject<CloudFileIdentity>(json);
	}


	/// <summary>
	///     Creates a new instance of <see cref="CloudFileIdentity" /> from the provided local file system path.
	/// </summary>
	/// <param name="path">The local file or directory path to convert into a <see cref="CloudFileIdentity" />.</param>
	/// <returns>
	///     A <see cref="CloudFileIdentity" /> object representing the file or directory at the provided path.
	/// </returns>
	public static CloudFileIdentity FromLocalPath(string path)
	{
		return new CloudFileIdentity();
	}


	/// <summary>
	///     Converts a <see cref="FileItem" /> object into an instance of <see cref="CloudFileIdentity" />.
	/// </summary>
	/// <param name="file">The <see cref="FileItem" /> to be converted, containing information about the cloud file.</param>
	/// <returns>
	///     A new instance of <see cref="CloudFileIdentity" /> initialized with the values from the provided <see cref="FileItem" />.
	/// </returns>
	public static CloudFileIdentity FromCloudFile(FileItem file, string parentPath)
	{
		return new CloudFileIdentity { Path = file.Path.StartsWith(parentPath) ? null : file.Path, VersionID = file.VersionID, VersionInfoID = file.Version?.ID };
	}
}


/// <summary>
///     Represents a request to fetch file activity logs within a specified date range.
/// </summary>
public class FileLogRequest
{
	[JsonProperty("from_date")]
	public long FromDate { get; set; }

	[JsonProperty("to_date")]
	public long ToDate { get; set; }
}


/// <summary>
///     Represents a logged action or event related to files within the system, encapsulating details such as the action type,
///     associated drive, paths, and file metadata.
/// </summary>
public class FileLog
{
	[JsonProperty("action")]
	public required string Action { get; set; }

	[JsonProperty("drive")]
	public required string Drive { get; set; }

	[JsonProperty("parentId")]
	public string? ParentId { get; set; }

	[JsonProperty("path")]
	public string? Path { get; set; }

	[JsonProperty("old_drive")]
	public string? OldDrive { get; set; }

	[JsonProperty("old_path")]
	public string? OldPath { get; set; }

	[JsonProperty("file")]
	public FileItem? File { get; set; }

	[JsonIgnore]
	public string? LocalPath => Path?.ConvertToLocalPath();

	[JsonIgnore]
	public string? OldLocalPath => OldPath?.ConvertToLocalPath();
}


/// <summary>
///     Represents a response containing a collection of file activity logs.
/// </summary>
public class FileLogResponse
{
	[JsonProperty("logs")]
	public required FileLog[] Logs { get; set; }
}


/// <summary>
///     Represents a request to initiate the download of files from a cloud storage system,
///     specifying the file path, file details, and optionally the host identifier.
/// </summary>
public class DownloadRequest
{
	[JsonProperty("path")]
	public required string Path { get; set; }

	[JsonProperty("file")]
	public required FileParam[] File { get; set; }

	[JsonProperty("host_id")]
	public string? HostID { get; set; }
}


/// <summary>
///     Represents the response for a file download operation, containing the URL to access the downloaded file.
/// </summary>
public class DownloadResponse
{
	[JsonProperty("url")]
	public required string URL { get; set; }
}


/// <summary>
///     Provides custom JSON serialization and deserialization for version data.
///     Enables the seamless conversion between raw JSON objects or strings
///     and the strongly-typed VersionInfo class during serialization and deserialization processes.
/// </summary>
public class VersionConverter : JsonConverter
{
	/// <summary>
	///     Determines whether the specified object type can be converted using this converter.
	/// </summary>
	/// <param name="objectType">The type of object to evaluate for compatibility with the converter.</param>
	/// <returns>
	///     A boolean value indicating whether the specified object type is supported by the converter.
	/// </returns>
	public override bool CanConvert(Type objectType)
	{
		return objectType == typeof(object);
	}


	/// <summary>
	///     Reads JSON data from the provided <see cref="JsonReader" /> and converts it into an object of the specified type.
	///     The method supports deserialization of JSON strings, JSON objects, and null values into their respective .NET
	///     representations.
	/// </summary>
	/// <param name="reader">The <see cref="JsonReader" /> instance providing the JSON data to read.</param>
	/// <param name="objectType">The type of the object to deserialize the JSON data into.</param>
	/// <param name="existingValue">An existing object value to populate, if applicable.</param>
	/// <param name="serializer">The <see cref="JsonSerializer" /> instance used to resolve and convert nested objects.</param>
	/// <returns>
	///     An object representing the deserialized JSON data, or a concrete <see cref="VersionInfo" /> instance if the JSON represents
	///     an object.
	/// </returns>
	public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
		{
			return null;
		}

		if (reader.TokenType == JsonToken.String && reader.Value is string)
		{
			return reader.Value.ToString();
		}

		if (reader.TokenType == JsonToken.StartObject)
		{
			var jObject = JObject.Load(reader);
			return jObject.ToObject<VersionInfo>();
		}

		throw new JsonSerializationException("Unexpected token type: " + reader.TokenType);
	}


	/// <summary>
	///     Writes JSON representation of the specified value using the provided <see cref="JsonWriter" /> and
	///     <see cref="JsonSerializer" />.
	/// </summary>
	/// <param name="writer">The <see cref="JsonWriter" /> used for writing JSON data.</param>
	/// <param name="value">The object to serialize into JSON.</param>
	/// <param name="serializer">The <see cref="JsonSerializer" /> used to perform custom serialization logic.</param>
	/// <exception cref="JsonSerializationException">
	///     Thrown when an unexpected value type is encountered during serialization.
	/// </exception>
	public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
	{
		if (value is string)
		{
			writer.WriteValue((string)value);
		}
		else if (value is VersionInfo)
		{
			serializer.Serialize(writer, value);
		}
		else
		{
			throw new JsonSerializationException("Unexpected value type: " + value?.GetType());
		}
	}
}


/// <summary>
///     Responsible for serializing and deserializing the <see cref="AccessLevel" /> enumeration
///     to and from its string representation in JSON.
/// </summary>
[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
public class AccessLevelConverter : JsonConverter
{
	/// <summary>
	///     Determines whether the specified object type can be converted by this converter.
	/// </summary>
	/// <param name="objectType">The type of the object to be checked for conversion eligibility.</param>
	/// <returns>
	///     A boolean value indicating whether the provided object type is supported for conversion.
	/// </returns>
	public override bool CanConvert(Type objectType)
	{
		return objectType == typeof(AccessLevel);
	}


	/// <summary>
	///     Deserializes a JSON value into an <see cref="AccessLevel" /> instance based on its string representation.
	///     If the string does not match a valid description, defaults to <see cref="AccessLevel.Viewer" />.
	/// </summary>
	/// <param name="reader">The <see cref="JsonReader" /> to read the JSON value from.</param>
	/// <param name="objectType">The type of the object to deserialize.</param>
	/// <param name="existingValue">The existing value of the object, if any.</param>
	/// <param name="serializer">The calling <see cref="JsonSerializer" /> instance.</param>
	/// <returns>
	///     An <see cref="AccessLevel" /> instance corresponding to the string representation in the JSON data.
	/// </returns>
	public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
		{
			return null;
		}

		if (reader.TokenType == JsonToken.String && reader.Value is string value)
		{
			return AccessLevelExtensions.GetAccessLevelFromDescription(value) ?? AccessLevel.Viewer;
		}

		throw new JsonSerializationException("Unexpected token type: " + reader.TokenType);
	}


	/// <summary>
	///     Writes the JSON representation of the specified <see cref="AccessLevel" /> value.
	/// </summary>
	/// <param name="writer">The <see cref="JsonWriter" /> used to write the JSON output.</param>
	/// <param name="value">The <see cref="AccessLevel" /> value to be serialized into JSON.</param>
	/// <param name="serializer">The <see cref="JsonSerializer" /> handling the serialization process.</param>
	/// <exception cref="JsonSerializationException">
	///     Thrown when the specified value is not of type <see cref="AccessLevel" />.
	/// </exception>
	public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
	{
		if (value is AccessLevel)
		{
			writer.WriteValue(EnumExtensions.GetDescription((AccessLevel)value));
		}
		else
		{
			throw new JsonSerializationException("Unexpected value type: " + value?.GetType());
		}
	}
}
