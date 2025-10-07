using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
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

	/// <summary>
	///     Provides static utility methods for handling application logging at various severity levels.
	/// </summary>
	/// <remarks>
	///     Leverages the log4net framework to support detailed logging, capable of capturing
	///     debugging information, warnings, errors, and application events. Includes functionality
	///     to configure a rolling file-based logging system.
	/// </remarks>
	static Log()
	{
		var logFilePath = SetupLogging();
		_logger = LogManager.GetLogger("AppLogger");
		Info($"Logger initialized. Log file path:  {logFilePath}");
	}


	// --------------------------------------------------------------------------------------------
	// STATIC API
	// -------------------------------------------------------------------------------------------
	
	/// <summary>
	///     Logs debug-level messages, including the source file name, line number, and calling member name.
	///     This method is useful for tracing and diagnostics during development.
	/// </summary>
	/// <param name="message">The debug message to log.</param>
	/// <param name="memberName">
	///     The name of the calling method or property. This value is automatically populated
	///     when called and typically corresponds to the member initiating the log request.
	/// </param>
	/// <param name="sourceFilePath">
	///     The file path of the source code file that contains the caller. This value is automatically populated
	///     by the compiler at runtime.
	/// </param>
	/// <param name="sourceLineNumber">
	///     The line number in the source code file where the log request originates. This value is automatically populated
	///     by the compiler at runtime.
	/// </param>
	public static void Debug(string message, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
	{
		var prefix = $"{Path.GetFileName(sourceFilePath)}:{sourceLineNumber} ";
		_logger.Debug(prefix + message);
	}


	/// <summary>
	///     Logs informational messages, typically used to provide general runtime details about program execution.
	/// </summary>
	/// <param name="message">
	///     The informational message to log. This message includes contextual information about the operation being performed.
	/// </param>
	/// <param name="memberName">
	///     The name of the calling method. Automatically populated by the runtime unless explicitly provided.
	/// </param>
	/// <param name="sourceFilePath">
	///     The file path of the source code that contains the call to this method. Automatically populated by the runtime unless explicitly provided.
	/// </param>
	/// <param name="sourceLineNumber">
	///     The line number in the source file where the method was called. Automatically populated by the runtime unless explicitly provided.
	/// </param>
	public static void Info(string message, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
	{
		var prefix = $"{Path.GetFileName(sourceFilePath)}:{sourceLineNumber} ";
		_logger.Info(prefix + message);
	}


	/// <summary>
	///     Logs a message with a warning severity level, including the source file name,
	///     line number, and member name where the method was called.
	/// </summary>
	/// <param name="message">The warning message to be logged.</param>
	/// <param name="memberName">The name of the calling member. Automatically set by the compiler.</param>
	/// <param name="sourceFilePath">The full file path of the source code file where the method was called. Automatically set by the compiler.</param>
	/// <param name="sourceLineNumber">The line number in the source code file where the method was called. Automatically set by the compiler.</param>
	public static void Warn(string message, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
	{
		var prefix = $"{Path.GetFileName(sourceFilePath)}:{sourceLineNumber} ";
		_logger.Warn(prefix + message);
	}


	/// <summary>
	///     Logs an error message with information about the source of the call, including file name and line number.
	/// </summary>
	/// <param name="message">The error message to be logged.</param>
	/// <param name="memberName">The name of the member invoking the method. This parameter is automatically populated by the compiler.</param>
	/// <param name="sourceFilePath">The file path of the source code invoking the method. This parameter is automatically populated by the compiler.</param>
	/// <param name="sourceLineNumber">The line number in the source code where the method was invoked. This parameter is automatically populated by the compiler.</param>
	public static void Error(string message, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
	{
		var prefix = $"{Path.GetFileName(sourceFilePath)}:{sourceLineNumber} ";
		_logger.Error(prefix + message);
	}


	/// <summary>
	///     Logs an error message along with detailed contextual information including the calling member name, file path, and line number.
	/// </summary>
	/// <param name="message">The error message to log.</param>
	/// <param name="ex">The exception associated with the error being logged.</param>
	/// <param name="memberName">The name of the member from where the method is called. This value is automatically populated by the compiler.</param>
	/// <param name="sourceFilePath">The full file path from which the method is called. This value is automatically populated by the compiler.</param>
	/// <param name="sourceLineNumber">The line number in the source file from which the method is called. This value is automatically populated by the compiler.</param>
	public static void Error(string message, Exception ex, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
	{
		var prefix = $"{Path.GetFileName(sourceFilePath)}:{sourceLineNumber} ";
		_logger.Error(prefix + message, ex);
	}


	/// <summary>
	///     Logs a critical message indicating a fatal event that will likely lead to application termination.
	/// </summary>
	/// <param name="message">The fatal error message to log.</param>
	/// <param name="memberName">The name of the caller member. This parameter is automatically populated using CallerMemberName.</param>
	/// <param name="sourceFilePath">The file path of the source code. This parameter is automatically populated using CallerFilePath.</param>
	/// <param name="sourceLineNumber">The line number in the source file. This parameter is automatically populated using CallerLineNumber.</param>
	public static void Fatal(string message, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
	{
		var prefix = $"{Path.GetFileName(sourceFilePath)}:{sourceLineNumber} ";
		_logger.Fatal(prefix + message);
	}


	/// <summary>
	///     Logs a fatal error message, optionally including an associated exception, along with caller information for precise traceability.
	/// </summary>
	/// <param name="message">The error message to be logged.</param>
	/// <param name="ex">The exception to be logged alongside the message, providing additional context. This parameter is optional and can be null.</param>
	/// <param name="memberName">The name of the method or property that invoked the logging operation. This is automatically populated by the runtime.</param>
	/// <param name="sourceFilePath">The file path of the source code where the logging call resides. This is automatically populated by the runtime.</param>
	/// <param name="sourceLineNumber">The line number in the source file where the logging call resides. This is automatically populated by the runtime.</param>
	public static void Fatal(string message, Exception ex, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
	{
		var prefix = $"{Path.GetFileName(sourceFilePath)}:{sourceLineNumber} ";
		_logger.Fatal(prefix + message, ex);
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
