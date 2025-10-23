using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;


/// <summary>
///     Provides functionality to monitor and notify when the clipboard content changes.
/// </summary>
public static class ClipboardNotification
{
	// --------------------------------------------------------------------------------------------
	// PROPERTIES
	// --------------------------------------------------------------------------------------------

	public static event EventHandler? ClipboardUpdate;
	
	private const int WM_CLIPBOARDUPDATE = 0x031D;
	private static HwndSource? _hwndSource;

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool AddClipboardFormatListener(IntPtr hwnd);


	// --------------------------------------------------------------------------------------------
	// PUBLIC METHODS
	// --------------------------------------------------------------------------------------------


	/// <summary>
	///     Initializes the clipboard notification system, allowing monitoring of clipboard changes.
	///     This method sets up a message-only window and registers it to receive notifications from
	///     the operating system whenever the clipboard content changes.
	/// </summary>
	/// <remarks>
	///     This method should be called to begin listening for clipboard update notifications. It uses
	///     the Windows API to create a message-only window and hooks into the <c>WM_CLIPBOARDUPDATE</c>
	///     message, which triggers when clipboard content is updated.
	///     Subscribing to the <c>ClipboardUpdate</c> event allows handling clipboard updates.
	/// </remarks>
	public static void Start()
	{
		var hwndSourceParameters = new HwndSourceParameters("ClipboardNotificationWindow")
		{
			WindowStyle = 0x800000, // WS_POPUP
			Width = 0,
			Height = 0,
			ParentWindow = new IntPtr(-3) // HWND_MESSAGE
		};

		_hwndSource = new HwndSource(hwndSourceParameters);
		_hwndSource.AddHook(WndProc);
		AddClipboardFormatListener(_hwndSource.Handle);
	}


	// --------------------------------------------------------------------------------------------
	// PRIVATE METHODS
	// --------------------------------------------------------------------------------------------


	/// <summary>
	///     Processes Windows messages for the clipboard notification system.
	///     This method handles messages sent to the hidden message-only window,
	///     primarily monitoring for clipboard updates.
	/// </summary>
	/// <param name="hwnd">
	///     A handle to the window receiving the message.
	/// </param>
	/// <param name="msg">
	///     The message identifier. This method is primarily concerned with the <c>WM_CLIPBOARDUPDATE</c> message.
	/// </param>
	/// <param name="wParam">
	///     Additional message information, dependent on the message type.
	/// </param>
	/// <param name="lParam">
	///     Additional message information, dependent on the message type.
	/// </param>
	/// <param name="handled">
	///     A reference to a boolean value that is set to <c>true</c> if the message has been processed and should not be passed to default processing.
	/// </param>
	/// <returns>
	///     An <c>IntPtr</c> that represents the result of the message processing.
	///     Returning <c>IntPtr.Zero</c> indicates default processing of the message.
	/// </returns>
	private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
	{
		if (msg == WM_CLIPBOARDUPDATE)
		{
			ClipboardUpdate?.Invoke(null, EventArgs.Empty);
		}

		return IntPtr.Zero;
	}
}
