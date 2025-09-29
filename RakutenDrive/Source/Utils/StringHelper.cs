using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;


namespace RakutenDrive.Utils;

/// <summary>
///     Provides utility methods for manipulating and processing file paths and strings.
/// </summary>
internal class StringHelper
{
	// --------------------------------------------------------------------------------------------
	// PUBLIC METHODS
	// --------------------------------------------------------------------------------------------


	/// <summary>
	///     Extracts and returns the last segment of a given file path. If the path ends with a trailing slash, it will be trimmed.
	/// </summary>
	/// <param name="path">The input file path whose last segment is to be extracted.</param>
	/// <returns>The last part of the path without a trailing slash, or an empty string if the path is invalid or empty.</returns>
	public static string GetLastPartOfPath(string path)
	{
		// Define the regex pattern to match the last part of the path
		var pattern = @"[^/]+/?$";
		var match = Regex.Match(path, pattern);

		return match.Success ? match.Value.TrimEnd('/') : string.Empty;
	}


	/// <summary>
	///     Processes the given file path by normalizing it and converting it to a full path.
	///     Appends a trailing slash to ensure the path ends as a directory.
	/// </summary>
	/// <param name="path">The input file path to process. Can be a relative or absolute path.</param>
	/// <returns>A normalized and absolute directory path with a trailing slash, or an empty string if the input path is null, empty, or root ("/").</returns>
	public static string PreprocessPath(string? path)
	{
		if (path == "/" || path == string.Empty || path == null)
		{
			return string.Empty;
		}

		return Path.GetFullPath(Path.Combine(path, "/")).Normalize(NormalizationForm.FormC);
	}


	/// <summary>
	///     Constructs and returns the full path to the root folder used by Rakuten Drive.
	///     The path is based on the user's home directory and includes a subfolder named "Rakuten Drive".
	/// </summary>
	/// <returns>The full path to the Rakuten Drive root folder as a string.</returns>
	public static string GetFullPathRootFolder()
	{
		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Rakuten Drive");
	}


	/// <summary>
	///     Extracts and returns the last segment of a given file path, ensuring the trailing slash is preserved if it exists in the original path.
	/// </summary>
	/// <param name="path">The input file path whose last segment is to be extracted.</param>
	/// <returns>The last part of the path with a trailing slash if it was present in the original path, or an empty string if the path is invalid or empty.</returns>
	public static string GetLastPartWithTrailingSlash(string path)
	{
		// Check if the path ends with a directory separator
		var hasTrailingSlash = path.EndsWith(Path.DirectorySeparatorChar.ToString()) || path.EndsWith(Path.AltDirectorySeparatorChar.ToString());

		// Get the last part of the path
		var lastPart = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

		// Append the trailing slash if it was present in the original path
		if (hasTrailingSlash)
		{
			lastPart += Path.DirectorySeparatorChar;
		}

		return lastPart;
	}


	/// <summary>
	///     Computes and returns the relative path from a specified base path to a full path.
	/// </summary>
	/// <param name="basePath">The base directory path from which the relative path will be calculated.</param>
	/// <param name="fullPath">The full path to be converted into a relative path based on the base directory.</param>
	/// <returns>A string representing the relative path from the base path to the full path, or the full path itself if the paths cannot be made relative.</returns>
	public static string GetRelativePath(string basePath, string fullPath)
	{
		var baseUri = new Uri(basePath);
		var fullUri = new Uri(fullPath);

		var relativeUri = baseUri.MakeRelativeUri(fullUri);
		var relativePath = Uri.UnescapeDataString(relativeUri.ToString());

		return relativePath;
	}


	/// <summary>
	///     Extracts and returns the parent directory path of a given file or directory path. If no parent directory exists, an empty string is returned.
	/// </summary>
	/// <param name="path">The input path from which the parent directory path is to be derived. It is expected to be a normalized path.</param>
	/// <returns>The parent directory path ending with a slash, or an empty string if the path has no parent directory.</returns>
	public static string GetParentPath(string path)
	{
		path = path.TrimEnd('/');
		var lastSlashIndex = path.LastIndexOf('/');
		if (lastSlashIndex == -1)
		{
			return string.Empty;
		}

		return path.Substring(0, lastSlashIndex + 1);
	}


	/// <summary>
	///     Determines whether the provided string represents a valid team ID based on its length.
	/// </summary>
	/// <param name="id">The string to be checked as a potential team ID.</param>
	/// <returns>True if the string is non-null, non-empty, and its length is less than or equal to 24; otherwise, false.</returns>
	public static bool IsTeamID(string? id)
	{
		if (!string.IsNullOrEmpty(id))
		{
			return id.Length <= 24;
		}

		return false;
	}
}
