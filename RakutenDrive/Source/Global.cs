using System;


namespace RakutenDrive;

/// <summary>
///     Represents the type of build configuration used in the application.
/// </summary>
/// <remarks>
///     This enum defines the different build environments that the application can target,
///     including Development, Staging, and Production.
/// </remarks>
internal enum BuildType
{
	Development,
	Staging,
	Production
}


/// <summary>
///     Represents global configuration settings for the application, such as
///     environment-specific URLs and build configuration types.
/// </summary>
internal class Global
{
	public static readonly BuildType BuildType = BuildType.Development;

	/// <summary>
	///     Provides the base API URL for the application based on the current build configuration.
	/// </summary>
	/// <remarks>
	///     The API URL dynamically adjusts to match the environment the application is running in.
	///     - Development: https://dev.api.rakuten-drive.com/
	///     - Staging: https://stg.api.rakuten-drive.com/
	///     - Production: https://api.rakuten-drive.com/
	/// </remarks>
	public static string ApiURL
	{
		get
		{
			return BuildType switch
			{
				BuildType.Development => "https://dev.api.rakuten-drive.com/",
				BuildType.Staging => "https://stg.api.rakuten-drive.com/",
				BuildType.Production => "https://api.rakuten-drive.com/",
				_ => throw new ArgumentOutOfRangeException()
			};
		}
	}

	/// <summary>
	///     Provides the base web URL for the application based on the current build configuration.
	/// </summary>
	/// <remarks>
	///     The Web URL dynamically adjusts to match the environment in which the application is running.
	///     Values for different build configurations are as follows:
	///     - Development: https://dev.rakuten-drive.com/
	///     - Staging: https://stg.rakuten-drive.com/
	///     - Production: https://rakuten-drive.com/
	/// </remarks>
	public static string WebURL
	{
		get
		{
			return BuildType switch
			{
				BuildType.Development => "https://dev.rakuten-drive.com/",
				BuildType.Staging => "https://stg.rakuten-drive.com/",
				BuildType.Production => "https://rakuten-drive.com/",
				_ => throw new ArgumentOutOfRangeException()
			};
		}
	}
}
