using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Win32;


namespace RakutenDrive.Utils;

/// <summary>
///     Provides functionality to register custom URL schemes in the operating system.
/// </summary>
[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
internal class URLSchemeRegistrar
{
	// --------------------------------------------------------------------------------------------
	// PUBLIC METHODS
	// --------------------------------------------------------------------------------------------


	/// <summary>
	///     Registers a custom URL scheme with the operating system, allowing the scheme to be associated with
	///     a specific application and enabled for protocol activation.
	/// </summary>
	/// <param name="scheme">
	///     The custom URL scheme to register. This is a string representing the scheme, e.g., "rddesktop".
	/// </param>
	/// <param name="applicationPath">
	///     The file path to the executable that should handle the URL scheme. This is the path to the application
	///     that will be invoked when the scheme is used.
	/// </param>
	/// <exception cref="ArgumentException">
	///     Thrown if the scheme or applicationPath is null or empty.
	/// </exception>
	public static void RegisterURLScheme(string scheme, string applicationPath)
	{
		if (string.IsNullOrEmpty(scheme) || string.IsNullOrEmpty(applicationPath))
		{
			throw new ArgumentException("Scheme and application path must be provided.");
		}

		var keyName = $@"Software\Classes\{scheme}";
		using (var key = Registry.CurrentUser.CreateSubKey(keyName))
		{
			key.SetValue(string.Empty, $"URL:{scheme} Protocol");
			key.SetValue("URL Protocol", string.Empty);

			using (var defaultIcon = key.CreateSubKey("DefaultIcon"))
			{
				defaultIcon.SetValue(string.Empty, $"{applicationPath},1");
			}

			using (var commandKey = key.CreateSubKey(@"shell\open\command"))
			{
				commandKey.SetValue(string.Empty, $"\"{applicationPath}\" \"%1\"");
			}
		}
	}
}
