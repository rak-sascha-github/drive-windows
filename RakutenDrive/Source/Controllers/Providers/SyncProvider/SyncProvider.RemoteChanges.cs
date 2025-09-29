using System;
using RakutenDrive.Utils;


namespace RakutenDrive.Controllers.Providers.SyncProvider;

/// <summary>
///     Represents a synchronization provider responsible for managing file synchronization processes.
/// </summary>
/// <remarks>
///     SyncProvider is a primary class used to handle synchronization between local and remote data.
///     It supports event-driven architecture with various event handlers for progress tracking
///     and notifications during synchronization operations.
/// </remarks>
/// <threadsafety>
///     This class is not guaranteed to be thread-safe. Proper synchronization is required when
///     accessing public or protected members from multiple threads.
/// </threadsafety>
/// <seealso cref="IDisposable" />
public sealed partial class SyncProvider : IDisposable
{
	/// <summary>
	///     Handles the file changed event triggered by the server file provider.
	///     Enqueues the file change event to the remote changes queue for further processing.
	/// </summary>
	/// <param name="sender">The object that raised the event. Could be null.</param>
	/// <param name="e">
	///     The event arguments containing information about the file change, including the type of change, old relative
	///     path, and other related data.
	/// </param>
	private async void OnServerProviderFileChanged(object? sender, FileChangedEventArgs e)
	{
		// MessageBox.Show($"ServerProvider_FileChanged {e}");
		Log.Debug("ServerProvider_FileChanged: (Delay 2000ms) " + e.Placeholder?.RelativeFileName);
		/** TODO 2 ways **/
		// await Task.Delay(5000);
		// RemoteChangesQueue.Post(e);
	}


	/// <summary>
	///     Handles the state change event for the server provider. Adjusts the timers for local synchronization
	///     and failed queue processing based on the new connection status.
	/// </summary>
	/// <param name="sender">The source of the event. May be null.</param>
	/// <param name="e">The event arguments providing details about the server provider's new state, including the connection status.</param>
	private void OnServerProviderStateChanged(object? sender, ServerProviderStateChangedEventArgs e)
	{
		Log.Debug("OnServerProviderStateChanged: " + e.Status);
	}
}
