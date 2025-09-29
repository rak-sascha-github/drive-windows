using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Windows;
using System.Windows.Controls;
using RakutenDrive.Services;
using RakutenDrive.Utils;
using RakutenDrive.Utils.CredentialManagement;


namespace RakutenDrive;

/// <summary>
///     Represents the main window of the RakutenDrive application. This class is responsible for initializing
///     the UI, setting up necessary services, and handling custom URL schemes.
/// </summary>
[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
public partial class MainWindow
{
	// --------------------------------------------------------------------------------------------
	// PROPERTIES
	// --------------------------------------------------------------------------------------------
	#region PROPERTIES

	private const string PIPE_NAME = "RakutenDrivePipe";
	private readonly CancellationTokenSource _cancellation = new();

	/* 0 = not launching, 1 = launching (atomic flag). */
	private int _loginGate;

	#endregion

	// --------------------------------------------------------------------------------------------
	// INIT
	// --------------------------------------------------------------------------------------------
	#region INIT

	/// <summary>
	///     Represents the main window of the RakutenDrive application.
	///     This class provides the core UI initialization, manages the lifecycle of the main window,
	///     starts the pipe server for inter-process communication, and handles incoming custom URL schemes.
	/// </summary>
	/// <remarks>
	///     The MainWindow serves as the central user interface component of the application and is created explicitly
	///     during application's startup or during specific update flows defined in the App class.
	///     It also implements cleanup logic when the window is closed.
	/// </remarks>
	public MainWindow()
	{
		InitializeComponent();
		StartPipeServer();
	}

	#endregion

	// --------------------------------------------------------------------------------------------
	// METHODS
	// --------------------------------------------------------------------------------------------
	#region METHODS

	/// <summary>
	///     Starts the pipe server for inter-process communication within the application.
	///     Listens for incoming connections on a named pipe and processes the received data, such as handling custom URL schemes.
	/// </summary>
	/// <remarks>
	///     This method continuously runs on a background task until the cancellation token is triggered.
	///     It creates a named pipe server, waits for incoming connections, and processes incoming data streams.
	///     Proper cleanup is handled when the application shuts down by leveraging the cancellation token.
	/// </remarks>
	private void StartPipeServer()
	{
		Task.Run(async () =>
		{
			while (!_cancellation.IsCancellationRequested)
			{
				using (var server = new NamedPipeServerStream(PIPE_NAME, PipeDirection.In))
				{
					await server.WaitForConnectionAsync(_cancellation.Token);
					using (var reader = new StreamReader(server))
					{
						var url = reader.ReadLine();
						if (!string.IsNullOrWhiteSpace(url))
						{
							await Dispatcher.Invoke(() => HandleCustomURLSchemeAsync(url));
						}
						else
						{
							Log.Error("Received empty URL input from pipe.");
						}
					}
				}
			}
		});
	}


	/// <summary>
	///     Processes a custom URL scheme received by the application, extracting relevant parameters
	///     such as tokens, managing authentication flows, and updating the application state or UI accordingly.
	/// </summary>
	/// <param name="url">The custom URL received, which contains query parameters and other relevant data for processing.</param>
	/// <returns>A task that represents the completion of URL handling logic, which may involve asynchronous operations like network calls or UI updates.</returns>
	public async Task HandleCustomURLSchemeAsync(string url)
	{
		/* Process the URL and update the UI accordingly. */
		try
		{
			var uri = new Uri(url);
			var token = HttpUtility.ParseQueryString(uri.Query).Get("token");

			if (!string.IsNullOrEmpty(token))
			{
				var jsonPayload = JWTParser.Parse(token);
				var refreshToken = jsonPayload.RefreshToken;
				var accountService = new AccountService();

				/* refresh flow */
				var payload = new { refresh_token = refreshToken };

				var refreshResponse = await accountService.GetRefreshToken(payload);
				if (refreshResponse is { IDToken: not null, RefreshToken: not null })
				{
					TokenStorage.SetAccessToken(refreshResponse.IDToken);
					TokenStorage.SetRefreshToken(refreshResponse.RefreshToken);
				}

				/* Navigate to the homepage window. */
				await App.Instance.UpdateCurrentWindow();
			}
			else
			{
				MessageBox.Show("No token found in the URL.");
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show($"Error handling custom URL scheme: {ex.Message}");
		}
	}
	
	
	/// <summary>
	///     Opens the default web browser with the specified URL.
	/// </summary>
	/// <param name="url">The URL to be opened in the default browser.</param>
	/// <remarks>
	///     This method attempts to launch the default web browser with the given URL.
	///     If an error occurs during this process, an error message is displayed to the user.
	/// </remarks>
	private void OpenBrowser(string url)
	{
		Task.Run(() =>
		{
			try
			{
				Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
			}
			catch (Exception ex)
			{
				Application.Current.Dispatcher.BeginInvoke(new Action(() => MessageBox.Show($"Failed to open browser: {ex.Message}", "Rakuten Drive", MessageBoxButton.OK, MessageBoxImage.Error)));
			}
		}).FireAndForget("Main_OpenBrowser");
	}
	

	/// <summary>
	///     Handles the click event for the login button in the main window.
	///     This method constructs the login URL and opens it in the user's default web browser.
	/// </summary>
	/// <param name="sender">The source of the event, typically the login button.</param>
	/// <param name="e">The event data associated with the button click event.</param>
	private async void OnLoginButtonClicked(object sender, RoutedEventArgs e)
	{
		/* Allow exactly one entry; all queued/rapid clicks return immediately. */
		if (Interlocked.Exchange(ref _loginGate, 1) == 1)
		{
			return;
		}

		var btn = sender as Button;
		if (btn != null)
		{
			/* Optional UI feedback. Prevents further hit-testing immediately */
			btn.IsEnabled = false;
			btn.IsHitTestVisible = false;
		}

		try
		{
			/* Open the browser once. */
			var loginUrl = Global.WebURL + "account/desktop/signin?os=windows";
			OpenBrowser(loginUrl);

			/* Optional tiny delay to swallow any already queued input messages (keeps UX snappy but avoids re-enabling too fast). */
			await Task.Delay(500);
		}
		finally
		{
			/* Re-enable & drop the gate. */
			Interlocked.Exchange(ref _loginGate, 0);

			if (btn != null)
			{
				btn.IsHitTestVisible = true;
				btn.IsEnabled = true;
			}
		}
	}
	
	
	/// <summary>
	///     Handles the logic to execute when the main window is closed.
	///     Overrides the base OnClosed method and performs necessary cleanup, such as canceling ongoing operations.
	/// </summary>
	/// <param name="e">Contains event data for the Closed event of the main window.</param>
	/// <remarks>
	///     This method ensures that any background operations or services started by the MainWindow,
	///     such as the pipe server, are properly terminated when the window is closed.
	/// </remarks>
	protected override void OnClosed(EventArgs e)
	{
		base.OnClosed(e);
		_cancellation.Cancel();
	}

	#endregion
}
