using System.Linq;
using System.Windows;


/// <summary>
///     Provides utility methods for user interface operations, such as displaying message boxes.
/// </summary>
public static class UIUtil
{
	/// <summary>
	///     Shows a message box with optional modality and topmost behavior.
	/// </summary>
	/// <param name="message">The message to display.</param>
	/// <param name="caption">The title of the message box.</param>
	/// <param name="buttons">The buttons to display.</param>
	/// <param name="icon">The icon to display.</param>
	/// <param name="isModal">If true, message box is modal to an active or main window. If false, no owner is attached.</param>
	public static void ShowMessageBox(string message, string caption = "Alert", MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.Information, bool isModal = true)
	{
		Application.Current.Dispatcher.Invoke(() =>
		{
			Window? owner = null;

			if (isModal)
			{
				owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow;
				owner?.Activate();
			}

			if (owner != null && isModal)
			{
				MessageBox.Show(owner, message, caption, buttons, icon, MessageBoxResult.None, MessageBoxOptions.DefaultDesktopOnly);
			}
			else
			{
				MessageBox.Show(message, caption, buttons, icon, MessageBoxResult.None, MessageBoxOptions.DefaultDesktopOnly);
			}
		});
	}
}
