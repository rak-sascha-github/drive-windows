using System.Windows;


namespace RakutenDrive;

/// <summary>
///     Represents the About dialog window for the application, providing information about the app.
/// </summary>
public partial class AboutWindow : Window
{
	public AboutWindow()
	{
		InitializeComponent();
	}


	private void CloseButton_Click(object sender, RoutedEventArgs e)
	{
		Close();
	}
}
