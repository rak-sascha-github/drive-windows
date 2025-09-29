using System;
using System.IO;
using System.Linq;
using System.Reflection;
using log4net;
using log4net.Appender;
using log4net.Config;


namespace RakutenDrive.Utils;

/// <summary>
///     Provides a utility class for logging messages at different levels of severity.
/// </summary>
/// <remarks>
///     Utilizes the log4net framework to handle logging operations, including
///     Debug, Info, Warn, Error, and Fatal levels. Messages can be logged directly,
///     or with an associated exception for additional details.
/// </remarks>
public static class Log
{
	// --------------------------------------------------------------------------------------------
	// PROPERTIES
	// --------------------------------------------------------------------------------------------

	private static readonly ILog _logger;


	// --------------------------------------------------------------------------------------------
	// CONSTRUCTOR
	// --------------------------------------------------------------------------------------------


	static Log()
	{
		var logFilePath = SetupLogging();
		_logger = LogManager.GetLogger("AppLogger");
		Info($"Logger initialized. Log file path:  {logFilePath}");
	}


	// --------------------------------------------------------------------------------------------
	// STATIC API
	// --------------------------------------------------------------------------------------------


	public static void Debug(string message)
	{
		_logger.Debug(message);
	}


	public static void Info(string message)
	{
		_logger.Info(message);
	}


	public static void Warn(string message)
	{
		_logger.Warn(message);
	}


	public static void Error(string message)
	{
		_logger.Error(message);
	}


	public static void Error(string message, Exception ex)
	{
		_logger.Error(message, ex);
	}


	public static void Fatal(string message)
	{
		_logger.Fatal(message);
	}


	public static void Fatal(string message, Exception ex)
	{
		_logger.Fatal(message, ex);
	}


	// --------------------------------------------------------------------------------------------
	// PRIVATE METHODS
	// --------------------------------------------------------------------------------------------


	/// <summary>
	///     Configures the logging system for the application by setting up the log file directory
	///     and initializing the logging configuration using a log4net configuration file. Ensures
	///     the log file path is correctly set and activates any affiliated appenders.
	/// </summary>
	private static string SetupLogging()
	{
		var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RakutenDrive", "logs");

		Directory.CreateDirectory(logDir);
		var logFilePath = Path.Combine(logDir, "app.log");

		/* Load configuration */
		var repo = LogManager.GetRepository(Assembly.GetEntryAssembly() ?? throw new InvalidOperationException());
		XmlConfigurator.Configure(repo, new FileInfo("log4net.config"));

		/* Get appender and override the path */
		var appender = repo.GetAppenders().OfType<RollingFileAppender>().FirstOrDefault(a => a.Name == "FileAppender");

		if (appender != null)
		{
			appender.File = logFilePath;
			appender.ActivateOptions();
		}

		return logFilePath;
	}
}
