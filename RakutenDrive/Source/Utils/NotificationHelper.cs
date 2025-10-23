using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Windows.Forms;


namespace RakutenDrive.Utils;

/// <summary>
///     A utility class for displaying system tray notifications to the user.
/// </summary>
[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
internal class NotificationHelper
{
	// --------------------------------------------------------------------------------------------
	// PROPERTIES
	// --------------------------------------------------------------------------------------------

	private static readonly NotifyIcon _notifyIcon;
	private static readonly int _timeout = 1500;


	// --------------------------------------------------------------------------------------------
	// INIT
	// --------------------------------------------------------------------------------------------


	/// <summary>
	///     Provides static methods for displaying system tray notifications to the user.
	/// </summary>
	static NotificationHelper()
	{
		// Initialize the NotifyIcon
		_notifyIcon = new NotifyIcon
		{
			Icon = SystemIcons.Error, // You can use a custom icon here
			Visible = true
		};
	}


	// --------------------------------------------------------------------------------------------
	// PUBLIC METHODS
	// --------------------------------------------------------------------------------------------


	/// <summary>
	///     Displays an error notification as a balloon tip in the system tray.
	/// </summary>
	/// <param name="message">The error message to be displayed in the notification.</param>
	public static void ShowError(string message)
	{
		// Show a balloon tip with the error message
		_notifyIcon.BalloonTipTitle = "Error";
		_notifyIcon.BalloonTipText = message;
		_notifyIcon.BalloonTipIcon = ToolTipIcon.Error;
		_notifyIcon.ShowBalloonTip(_timeout); // Show the balloon tip for 3 seconds
	}


	/// <summary>
	///     Displays an information notification as a balloon tip in the system tray.
	/// </summary>
	/// <param name="message">The information message to be displayed in the notification.</param>
	public static void ShowInformation(string message)
	{
		// Show a balloon tip with the error message
		_notifyIcon.BalloonTipTitle = "Information";
		_notifyIcon.BalloonTipText = message;
		_notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
		_notifyIcon.ShowBalloonTip(_timeout); // Show the balloon tip for 3 seconds
	}
}
