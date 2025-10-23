using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using RakutenDrive.Controllers.Providers.ServerProvider;
using RakutenDrive.Controllers.Providers.SyncProvider;
using RakutenDrive.Resources;
using RakutenDrive.Services;
using RakutenDrive.Utils;
using RakutenDrive.Utils.CredentialManagement;
using MessageBox = System.Windows.MessageBox;


namespace RakutenDrive;

/// <summary>
///     The App class serves as the primary application entry point for RakutenDrive.
///     It extends WPF's Application to handle:
///     - Single-instance enforcement using a named Mutex.
///     - Initialization of logging (log4net) and culture settings.
///     - Configuration and lifecycle management of the SyncProvider for file synchronization.
///     - Registration and handling of a custom URL scheme for deep linking.
///     - Setup of a system tray icon with context menu actions (About, Open, Help, Quit).
///     - Window management based on authentication state, directing users to HomeWindow or MainWindow.
///     - Inter-process communication to forward URLs to an existing instance via named pipes.
/// </summary>
/// <remarks>
///     OnStartup overrides WPF startup to perform all initial setup before showing UI.
///     OnExit ensures the SyncProvider is stopped and system resources are cleaned up.
///     Public methods UpdateCurrentWindow and Logout manage user authentication flows.
///     Private helper methods encapsulate provider initialization, UI actions, and pipe communication.
/// </remarks>
[SupportedOSPlatform("windows")]
public partial class App
{
	// ----------------------------------------------------------------------------------------
	// PROPERTIES
	// ----------------------------------------------------------------------------------------
	#region PROPERTIES

	/* Singleton instance access. */
	public static App Instance { get; private set; } = null!;

	private static Mutex? _mutex;
	private short _progress;
	private string _queueStatus = string.Empty;
	private NotifyIcon? _notifyIcon;
	private SyncProvider _syncProvider = null!;

	private const int balloonTipTimeout = 1500; // 1.5 seconds

	/// <summary>
	/// Provides access to the SyncProvider instance.  Consumers should check SyncProvider.IsFullyInitialised
	/// before performing operations that depend on the provider being ready.
	/// </summary>
	public SyncProvider SyncProvider => _syncProvider;	

	#endregion

	// ----------------------------------------------------------------------------------------
	// APPLICATION CALLBACKS
	// ----------------------------------------------------------------------------------------
	#region APPLICATION CALLBACKS

	/// <summary>
	///     Overrides the application startup to initialize logging, enforce single-instance, configure synchronization,
	///     and setup UI flow depending on authentication state.
	/// </summary>
	/// <param name="e">Event data for the startup event.</param>
	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);
		InstallGlobalExceptionHandlers();
		SyncProvider.CleanupPendingDeletesOnStartup();
		AppStartupAsync(e).FireAndForget("AppStartupAsync");
	}


	/// <summary>
	///     Overrides the application exit method to perform cleanup operations, such as disposing of the tray UI and
	///     ensuring an orderly shutdown of the synchronization provider.
	/// </summary>
	/// <param name="e">Event data for the exit event.</param>
	protected override void OnExit(ExitEventArgs e)
	{
		Log.Debug("Exiting ...");

		/* Hide & dispose tray first (UX + avoids handles during teardown) */
		try
		{
			if (_notifyIcon != null)
			{
				_notifyIcon.Visible = false;
				_notifyIcon.Dispose();
			}
		}
		catch
		{
			/* ignore */
		}

		/* Orderly provider shutdown (don’t deadlock the dispatcher) */
		try
		{
			_syncProvider.StopAsync().GetAwaiter().GetResult();
		}
		catch (Exception ex)
		{
			Log.Error($"Error during orderly shutdown: {ex}");
		}

		base.OnExit(e);
	}

	#endregion

	// ----------------------------------------------------------------------------------------
	// PUBLIC METHODS
	// ----------------------------------------------------------------------------------------
	#region PUBLIC METHODS

	/// <summary>
	///     Updates the current window based on authentication state by closing existing
	///     windows and showing either HomeWindow or MainWindow.
	/// </summary>
	/// <returns>An asynchronous task.</returns>
	public async Task UpdateCurrentWindow()
	{
		Log.Info("Updating current window ...");

		/* Close existing windows */
		foreach (var win in Windows.OfType<Window>().ToList())
		{
			if (win is HomeWindow || win is MainWindow)
			{
				win.Close();
			}
		}

		try
		{
			var jwtToken = TokenStorage.GetAccessToken();
			var refreshToken = TokenStorage.GetRefreshToken();

			if (!string.IsNullOrEmpty(jwtToken) && !JWTParser.IsTokenExpired(jwtToken))
			{
				await _syncProvider.Start();
				new HomeWindow().Show();
			}
			else if (!string.IsNullOrEmpty(refreshToken))
			{
				/* Refresh token flow. */
				var accountService = new AccountService();
				var payload = new { refresh_token = refreshToken };
				var refreshResponse = await accountService.GetRefreshToken(payload);

				if (refreshResponse is { IDToken: not null, RefreshToken: not null })
				{
					TokenStorage.SetAccessToken(refreshResponse.IDToken);
					TokenStorage.SetRefreshToken(refreshResponse.RefreshToken);
				}

				await _syncProvider.Start();
				new HomeWindow().Show();
			}
			else
			{
				_syncProvider.Stop();
				new MainWindow().Show();
			}
		}
		catch (Exception ex)
		{
			Log.Error($"Error updating current window: {ex}");
			_syncProvider.Stop();
			new MainWindow().Show();
		}
	}


	/// <summary>
	///     Logs out the current user by clearing tokens, stopping sync, and showing login.
	/// </summary>
	/// <returns>An asynchronous task.</returns>
	public async Task Logout()
	{
		try
		{
			Log.Info("Logout requested.");

			/* Clear tokens first so UI updates move to login state. */
			TokenStorage.ClearAllTokens();

			/* Orderly provider shutdown (await; do not block UI thread). */
			await _syncProvider.StopAsync().ConfigureAwait(true);

			/* Refresh the UI (e.g., show login window). */
			await UpdateCurrentWindow().ConfigureAwait(true);
			Log.Info("Logout completed.");
		}
		catch (Exception ex)
		{
			Log.Error($"Logout failed: {ex}");
			/* Best effort: still try to update UI. */
			try
			{
				await UpdateCurrentWindow().ConfigureAwait(true);
			}
			catch
			{
				/* ignore */
			}
		}
	}

	#endregion

	// ----------------------------------------------------------------------------------------
	// PRIVATE METHODS
	// ----------------------------------------------------------------------------------------
	#region PRIVATE METHODS

	/// <summary>
	///     Configures global exception handlers for the application, ensuring that exceptions
	///     occurring on the UI thread, background threads, or unobserved tasks are logged,
	///     and appropriate actions are taken to prevent the application from crashing unexpectedly.
	/// </summary>
	private void InstallGlobalExceptionHandlers()
	{
		/* UI thread exceptions */
		DispatcherUnhandledException += (s, e) =>
		{
			Log.Error($"UI thread exception: {e.Exception}");
			/* Prevent default crash. */
			e.Handled = true;
			ShowFatalAndShutdown(e.Exception);
		};

		/* Non-UI/background thread exceptions */
		AppDomain.CurrentDomain.UnhandledException += (s, e) =>
		{
			Log.Error($"Non-UI thread exception: {e.ExceptionObject}");
			ShowFatalAndShutdown(e.ExceptionObject as Exception);
		};

		/* Unobserved task exceptions */
		TaskScheduler.UnobservedTaskException += (s, e) =>
		{
			Log.Error($"Unobserved task exception: {e.Exception}");
			/* Keep process alive; we log and continue. */
			e.SetObserved();
		};
	}


	/// <summary>
	///     Displays a fatal error message to the user and shuts down the application.
	/// </summary>
	/// <param name="ex">The exception causing the fatal error. Can be null if no specific exception is available.</param>
	private void ShowFatalAndShutdown(Exception? ex)
	{
		try
		{
			MessageBox.Show("Rakuten Drive encountered a fatal error and must close.\n\nPlease check the log for details.", "Rakuten Drive", MessageBoxButton.OK, MessageBoxImage.Error);
		}
		catch
		{
			/* best-effort UI */
		}

		Shutdown(-1);
	}


	/// <summary>
	///     Handles the asynchronous startup process for the application, including culture configuration,
	///     logging initialization, mutex enforcement, sync provider setup, URL scheme registration,
	///     and context menu configuration.
	/// </summary>
	/// <param name="e">Startup event arguments, including command-line parameters passed to the application.</param>
	/// <returns>A task representing the asynchronous operation of the startup process.</returns>
	private async Task AppStartupAsync(StartupEventArgs e)
	{
		try
		{
			Instance = this;
			ShutdownMode = ShutdownMode.OnExplicitShutdown;

			/* Culture */
			var defaultCulture = CultureInfo.InstalledUICulture;
			Thread.CurrentThread.CurrentUICulture = defaultCulture;
			Thread.CurrentThread.CurrentCulture = defaultCulture;
			Strings.Culture = defaultCulture;

			Log.Info("Application is starting...");

			var name = Assembly.GetExecutingAssembly().GetName();
			Log.Info($"{name.FullName} v{name.Version} ({name.Version?.Build})");

			/* Single instance */
			bool createdNew;
			_mutex = new Mutex(true, "RakutenDriveApplicationMutex", out createdNew);
			if (!createdNew)
			{
				if (e.Args.Length > 0)
				{
					// Non-blocking: forward on a background-friendly await
					await SendURLToRunningInstanceAsync(e.Args[0]).ConfigureAwait(false);
				}

				// Exit regardless of forward success to honour single-instance
				Environment.Exit(0);
				return;
			}
			
			/* Init provider & clean any old root */
			InitProvider();
			await _syncProvider.UnregisterExistingRoot().ConfigureAwait(true);

			/* URL scheme & first window */
			RegisterURLScheme();
			if (e.Args.Length > 0)
			{
				var mainWindow = new MainWindow();
				mainWindow.Show();
				await mainWindow.HandleCustomURLSchemeAsync(e.Args[0]).ConfigureAwait(true);
			}
			else
			{
				await UpdateCurrentWindow().ConfigureAwait(true);
			}

			/* Tray icon (WinForms) – events marshalled to WPF UI thread */
			_notifyIcon = new NotifyIcon { Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "Images", "Icons", "Logo_light.ico")), Visible = true, Text = "rakuten-drive".LocalizedString(), ContextMenuStrip = BuildContextMenu() };
			_notifyIcon.DoubleClick += (s, _) => OnUI(EnsureAppWindow);

			Log.Info("Application started.");
		}
		catch (Exception ex)
		{
			Log.Error($"Fatal error during startup: {ex}");
			ShowFatalAndShutdown(ex);
		}
	}


	/// <summary>
	///     Configures the system tray notify icon and its associated context menu.
	///     Adds functionality such as opening the application, accessing the help center,
	///     displaying application information, and quitting the application.
	/// </summary>
	private ContextMenuStrip BuildContextMenu()
	{
		var menu = new ContextMenuStrip();
		menu.Items.Add(new ToolStripMenuItem("menu/about".LocalizedString(), null, (s, e) => OnUI(() => new AboutWindow().ShowDialog())));
		menu.Items.Add(new ToolStripSeparator());
		menu.Items.Add(new ToolStripMenuItem("menu/open".LocalizedString(), null, (s, e) => OnUI(EnsureAppWindow)));
		menu.Items.Add(new ToolStripMenuItem("menu/open-in-web".LocalizedString(), null, (s, e) => OnUI(OnOpenRDInWebClicked)));
		menu.Items.Add(new ToolStripSeparator());
		menu.Items.Add(new ToolStripMenuItem("menu/help-center".LocalizedString(), null, (s, e) => OnUI(() => OnOpenRDHelpCenterClicked(s, e))));
		menu.Items.Add(new ToolStripSeparator());

		/* Quit – orderly async shutdown + WPF-safe */
		menu.Items.Add(new ToolStripMenuItem("menu/quit".LocalizedString(), null, (s, e) => OnUI(async () =>
		{
			try
			{
				if (_notifyIcon != null)
				{
					_notifyIcon.Visible = false;
					_notifyIcon.Dispose();
				}
			}
			catch
			{
				/* best effort */
			}

			try
			{
				await _syncProvider.StopAsync().ConfigureAwait(true);
			}
			catch (Exception ex)
			{
				Log.Error($"Tray Quit StopAsync failed: {ex}");
			}

			Shutdown();
		})));

		return menu;
	}


	/// <summary>
	///     Initializes the SyncProvider and hooks up progress and queue event handlers.
	/// </summary>
	private void InitProvider()
	{
		// ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
		if (_syncProvider != null)
		{
			return;
		}

		var syncRootFolder = StringHelper.GetFullPathRootFolder();
		var param = new SyncProviderParameters
		{
			ProviderInfo = new BasicSyncProviderInfo
			{
				ProviderId = Guid.Parse(GetAssemblyGUID()),
				ProviderName = "Rakuten Drive",
				ProviderVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0.0"
			},
			LocalDataPath = syncRootFolder,
			ServerProvider = new ServerProvider(string.Empty)
		};

		_syncProvider = new SyncProvider(param);
		_syncProvider.FileProgressEvent += OnSyncProviderFileProgressEvent;
		//_syncProvider.QueuedItemsCountChanged += OnSyncProviderQueuedItemsCountChanged;
		_syncProvider.OnShowNotification = OnShowNotification;

		Log.Info("Sync provider initialized.");
	}


	/// <summary>
	///     Retrieves the assembly's module version GUID for provider identification.
	/// </summary>
	/// <returns>GUID string of the assembly module.</returns>
	private string GetAssemblyGUID()
	{
		var module = Assembly.GetExecutingAssembly().GetModules().FirstOrDefault();
		return module?.ModuleVersionId.ToString() ?? Guid.Empty.ToString();
	}


	/// <summary>
	///     Registers the custom rddesktop URL scheme in the OS.
	/// </summary>
	private void RegisterURLScheme()
	{
		var scheme = "rddesktop";
		var path = Process.GetCurrentProcess().MainModule?.FileName;
		if (path != null)
		{
			URLSchemeRegistrar.RegisterURLScheme(scheme, path);
			Log.Info($"URL scheme registered: {scheme}, path: {path}");
		}
		else
		{
			Log.Error($"Failed to register URL scheme: {scheme} path is null.");
		}
	}


	/// <summary>
	///     Ensures that an application window is open by updating the current window.
	/// </summary>
	private void EnsureAppWindow()
	{
		if (Current.Windows.Count == 0)
		{
			UpdateCurrentWindow().FireAndForget("UpdateCurrentWindow");
		}
	}


	/// <summary>
	///     Sends a URL to the primary instance via a named pipe, without blocking the UI thread.
	///     Uses ConnectAsync with a short timeout and logs failures instead of showing a modal.
	/// </summary>
	/// <param name="url">URL to send to the existing instance.</param>
	/// <param name="timeoutMs">How long to wait for the server to accept the connection.</param>
	private static async Task SendURLToRunningInstanceAsync(string url, int timeoutMs = 1000)
	{
		try
		{
			using var cts = new CancellationTokenSource(timeoutMs);

			/* Asynchronous client so ConnectAsync truly yields. */
			using var client = new NamedPipeClientStream(serverName: ".", pipeName: "RakutenDrivePipe", direction: PipeDirection.Out, options: PipeOptions.Asynchronous);

			await client.ConnectAsync(cts.Token).ConfigureAwait(false);

			using var writer = new StreamWriter(client) { AutoFlush = true };
			await writer.WriteLineAsync(url).ConfigureAwait(false);

			Log.Info($"URL forwarded to running instance: {url}");
		}
		catch (OperationCanceledException)
		{
			// Timeout — primary instance may be busy or not yet listening
			Log.Warn("Timed out waiting for the running instance to accept the URL (named pipe).");
		}
		catch (IOException ioEx)
		{
			// Pipe unavailable / server not listening — benign in practice
			Log.Warn($"Named pipe forward failed (no listener?): {ioEx.Message}");
		}
		catch (Exception ex)
		{
			Log.Error($"Error sending URL to running instance: {ex}");
		}
	}


	/// <summary>
	///     Executes the provided action on the UI thread. If the current thread is the UI thread, the action is executed immediately;
	///     otherwise, it is dispatched to the UI thread asynchronously.
	/// </summary>
	/// <param name="action">The action to perform on the UI thread.</param>
	private void OnUI(Action action)
	{
		if (Dispatcher.CheckAccess())
		{
			action();
		}
		else
		{
			Dispatcher.BeginInvoke(action);
		}
	}


	/// <summary>
	///     Invokes the specified asynchronous action on the UI thread.
	/// </summary>
	/// <param name="actionAsync">The asynchronous action to be executed on the UI thread.</param>
	private void OnUI(Func<Task> actionAsync)
	{
		Dispatcher.BeginInvoke(new Action(async () =>
		{
			try
			{
				await actionAsync().ConfigureAwait(true);
			}
			catch (Exception ex)
			{
				Log.Error($"UI-dispatched async error: {ex}");
			}
		}));
	}

	#endregion

	// ----------------------------------------------------------------------------------------
	// HANDLERS
	// ----------------------------------------------------------------------------------------
	#region HANDLERS

	/// <summary>
	///     Handles file sync progress updates by storing the latest progress value.
	/// </summary>
	/// <param name="sender">Event source.</param>
	/// <param name="e">Progress event args.</param>
	private void OnSyncProviderFileProgressEvent(object? sender, FileProgressEventArgs e)
	{
		Log.Debug($"OnSyncProviderFileProgressEvent: {e.relativeFilePath} - {e.Progress}");
		_progress = e.Progress;
	}


	/// <summary>
	///     Handles queue count changes by updating the QueueStatus string.
	/// </summary>
	/// <param name="sender">Event source.</param>
	/// <param name="e">New queue count.</param>
	// private void OnSyncProviderQueuedItemsCountChanged(object? sender, int e)
	// {
	// 	Log.Debug($"OnSyncProviderQueuedItemsCountChanged: {e}");
	// 	_queueStatus = e.ToString();
	// }


	/// <summary>
	///     Shows a system tray balloon tip with title and message.
	/// </summary>
	/// <param name="title">Notification title.</param>
	/// <param name="message">Notification message.</param>
	private void OnShowNotification(string title, string message)
	{
		_notifyIcon?.ShowBalloonTip(balloonTipTimeout, title, message, ToolTipIcon.None);
	}


	/// <summary>
	///     Opens the About dialog window.
	/// </summary>
	/// <param name="sender">Event source.</param>
	/// <param name="e">Event args.</param>
	private void OnAboutClicked(object? sender, EventArgs e)
	{
		new AboutWindow().ShowDialog();
	}


	/// <summary>
	///     Opens the local sync folder in Windows Explorer.
	/// </summary>
	/// <param name="sender">Event source.</param>
	/// <param name="e">Event args.</param>
	private void OnOpenRDClicked(object? sender, EventArgs e)
	{
		var path = StringHelper.GetFullPathRootFolder();
		Task.Run(() =>
		{
			try
			{
				Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true, Verb = "open" });
			}
			catch (Exception exception)
			{
				Log.Error("Error opening local sync folder:", exception);
			}
		}).FireAndForget("OpenRDLocalFolder");
	}

	/// <summary>
	///     Handles the click event for opening Rakuten Drive in the default web browser.
	/// </summary>
	private void OnOpenRDInWebClicked()
	{
		/* Keep UI snappy; shell open can block */
		Task.Run(() =>
		{
			try
			{
				Process.Start(new ProcessStartInfo { FileName = Global.WebURL, UseShellExecute = true });
			}
			catch (Exception ex)
			{
				Log.Error($"Open in web failed: {ex}");
			}
		}).FireAndForget("OpenRDInWeb");
	}


	/// <summary>
	///     Opens the cloud drive web interface in the default browser.
	/// </summary>
	/// <param name="sender">Event source.</param>
	/// <param name="e">Event args.</param>
	private void OnOpenRDInWebClicked(object? sender, EventArgs e)
	{
		/* Keep UI snappy; shell open can block */
		Task.Run(() =>
		{
			try
			{
				Process.Start(new ProcessStartInfo { FileName = Global.WebURL, UseShellExecute = true });
			}
			catch (Exception ex)
			{
				Log.Error($"Open in web failed: {ex}");
			}
		}).FireAndForget("OpenRDInWeb");
	}


	/// <summary>
	///     Opens the help center page in the default browser.
	/// </summary>
	/// <param name="sender">Event source.</param>
	/// <param name="e">
	///     Event args.
	/// </param>
	private void OnOpenRDHelpCenterClicked(object? sender, EventArgs e)
	{
		/* Keep UI snappy; shell open can block */
		Task.Run(() =>
		{
			try
			{
				Process.Start(new ProcessStartInfo { FileName = "https://support.rakuten-drive.com/", UseShellExecute = true });
			}
			catch (Exception ex)
			{
				Log.Error($"Open help center failed: {ex}");
			}
		}).FireAndForget("OpenRDHelpCenter");
	}


	/// <summary>
	///     Exits the application when the Quit menu item is clicked.
	/// </summary>
	/// <param name="sender">Event source.</param>
	/// <param name="e">Event args.</param>
	private void OnExitClicked(object? sender, EventArgs e)
	{
		Shutdown();
	}

	#endregion
}
