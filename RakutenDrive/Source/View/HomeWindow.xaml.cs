using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RakutenDrive.Utils;
using RakutenDrive.Utils.CredentialManagement;


namespace RakutenDrive;

/// <summary>
///     Represents the primary window displayed after a successful login or token refresh in the Rakuten Drive application.
/// </summary>
/// <remarks>
///     This class initializes the HomeWindow UI and handles asynchronous initialization logic if a valid JWT token
///     is retrieved from the TokenStorage. If a token exists, the application performs necessary setup tasks before
///     making the window visible to the user.
/// </remarks>
[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
public partial class HomeWindow
{
	private readonly string _syncRootPath = StringHelper.GetFullPathRootFolder();
	private string _syncServerPath = string.Empty;


	/// <summary>
	///     Represents the main application window displayed after successful authentication
	///     or token validation in the Rakuten Drive application.
	/// </summary>
	/// <remarks>
	///     The HomeWindow is initialized when a valid JWT access token is retrieved from
	///     the TokenStorage. If a valid token is found, the window triggers asynchronous
	///     initialization tasks to prepare the application's state before displaying the UI.
	/// </remarks>
	public HomeWindow()
	{
		InitializeComponent();
		
		/* Window bounds store/restore. */
		SourceInitialized += (_, __) => WindowBoundsPersistence.Restore(this, "HomeWindow");
		Closing += (_, __) => WindowBoundsPersistence.Save(this, "HomeWindow");
		
		var jwtToken = TokenStorage.GetAccessToken();
		if (!string.IsNullOrEmpty(jwtToken))
		{
			HandleInitShowUpAsync(jwtToken).FireAndForget("HandleInitShowUpAsync");
		}
	}
	

	/// <summary>
	///     Handles the initialization and setup of user-related display elements in the HomeWindow
	///     based on the provided JWT token payload.
	/// </summary>
	/// <param name="jwtToken">
	///     A string representing the JWT access token retrieved from the TokenStorage, used to parse
	///     user-specific data such as display name, email, and profile picture.
	/// </param>
	/// <returns>
	///     A Task representing the asynchronous operation for initializing and updating the UI
	///     elements of the HomeWindow, including profile name, email, and profile image.
	/// </returns>
	private Task HandleInitShowUpAsync(string jwtToken)
	{
		JWTPayloadInfo? jsonPayload;
		try
		{
			jsonPayload = JWTParser.Parse(jwtToken);
		}
		catch (Exception ex)
		{
			Log.Error($"HandleInitShowUpAsync exception thrown: {ex}");
			return Task.CompletedTask;
		}
		
		displayNameLabel.Text = jsonPayload.DisplayName;
		displayEmailLabel.Text = jsonPayload.Email;

		if (jsonPayload.DisplayName != null)
		{
			textProfile.Text = jsonPayload.DisplayName.First().ToString();
		}

		if (!string.IsNullOrEmpty(jsonPayload.Picture))
		{
			/* Retrieve the ImageBrush resource by its key. */
			var imageBrush = new ImageBrush
			{
				/* Set the ImageSource to the desired URL. */
				ImageSource = new BitmapImage(new Uri(jsonPayload.Picture))
			};

			myEllipse.Fill = imageBrush;
		}

		return Task.CompletedTask;
	}
	
	
	/// <summary>
	///     Handles the event triggered when the left mouse button is released after clicking on the folder icon in the UI.
	///     Opens the folder associated with the application's root sync directory in the default file explorer.
	/// </summary>
	/// <param name="sender">The source of the event, typically a UI element like the folder icon.</param>
	/// <param name="e">The event data containing information about the mouse button action.</param>
	private void OnOpenFolderMouseLeftButtonUp2(object sender, MouseButtonEventArgs e)
	{
		Task.Run(() =>
		{
			try
			{
				Process.Start(new ProcessStartInfo { FileName = _syncRootPath, UseShellExecute = true, Verb = "open" });
			}
			catch (Exception ex)
			{
				/* Marshal UI feedback back to the dispatcher. */
				Application.Current.Dispatcher.BeginInvoke(new Action(() => MessageBox.Show($"Failed to open folder:\n{ex.Message}", "Rakuten Drive", MessageBoxButton.OK, MessageBoxImage.Error)));
			}
		}).FireAndForget("Home_OpenFolder");
	}


	/// <summary>
	/// Handles the event triggered when the left mouse button is released after clicking on the folder icon in the UI.
	/// Waits for the sync provider to finish initialising before opening the folder in Explorer.
	/// </summary>
	private async void OnOpenFolderMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		try
		{
			// obtain the provider from the App singleton
			var provider = App.Instance?.SyncProvider;

			// if the provider exists, wait until it is fully initialised
			if (provider != null)
			{
				while (!provider.IsFullyInitialized)
				{
					await Task.Delay(200);
				}
			}

			// open the folder off the UI thread
			await Task.Run(() => { Process.Start(new ProcessStartInfo { FileName = _syncRootPath, UseShellExecute = true, Verb = "open" }); });
		}
		catch (Exception ex)
		{
			// marshal UI feedback back to the dispatcher
			Application.Current.Dispatcher.BeginInvoke(new Action(() => MessageBox.Show($"Failed to open folder:\n{ex.Message}", "Rakuten Drive", MessageBoxButton.OK, MessageBoxImage.Error)));
		}
	}


	/// <summary>
	///     Handles the click event for the Logout button in the HomeWindow.
	/// </summary>
	/// <param name="sender">The source of the event, typically the Logout button.</param>
	/// <param name="e">The event data associated with the button click.</param>
	/// <remarks>
	///     This method clears the user's display name and email labels in the UI and
	///     triggers the application's logout process by invoking the Logout method on
	///     the singleton App instance.
	/// </remarks>
	private async void OnLogOutButtonClicked(object sender, RoutedEventArgs e)
	{
		/* Optional: clear UI immediately. */
		displayNameLabel.Text = string.Empty;
		displayEmailLabel.Text = string.Empty;

		/* Prevent double-clicks while we’re shutting down. */
		if (sender is System.Windows.Controls.Button btn) btn.IsEnabled = false;

		try
		{
			/* Await so we don’t fire-and-forget. */
			await App.Instance.Logout();
		}
		catch (Exception ex)
		{
			Log.Error($"LogOutButton_Click: {ex}");
			MessageBox.Show("Logout encountered an error. Please try again.", "Rakuten Drive", MessageBoxButton.OK, MessageBoxImage.Error);
		}
		finally
		{
			if (sender is System.Windows.Controls.Button btn2) btn2.IsEnabled = true;
		}
	}
}
