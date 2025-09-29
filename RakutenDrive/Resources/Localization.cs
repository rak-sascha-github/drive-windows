using System.Reflection;
using System.Resources;


namespace RakutenDrive.Resources;

/// <summary>
///     Provides functionality for managing and retrieving localized strings.
/// </summary>
internal class Localization
{
	internal static ResourceManager resourceManager = new("RakutenDrive.Resources.Strings", Assembly.GetExecutingAssembly());

	public static string VersionString
	{
		get
		{
			try
			{
				var version = string.Format(Strings.about_window_version, Assembly.GetExecutingAssembly().GetName().Version.ToString());
				return Global.BuildType switch
				{
					BuildType.Development => version + "-DEV",
					BuildType.Staging => version + "-STG",
					_ => version
				};
			}
			catch
			{
				return "Unknown";
			}
		}
	}
}


/// <summary>
///     Provides extension methods for handling localization-related functionality.
/// </summary>
public static class LocalizationExtensions
{
	public static string LocalizedString(this string key)
	{
		return Localization.resourceManager.GetString(key) ?? key;
	}
}
