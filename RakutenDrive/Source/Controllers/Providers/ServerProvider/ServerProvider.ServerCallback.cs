using System;
using System.IO;
using System.Threading.Tasks.Dataflow;
using RakutenDrive.Utils;


namespace RakutenDrive.Controllers.Providers.ServerProvider;

/// <summary>
///     The ServerProvider class is a sealed partial class that provides functionalities
///     to manage server-related operations and events.
/// </summary>
/// <remarks>
///     This class is used to initialize, manage, and handle server-based interactions
///     within the application. It works in conjunction with other components, such as
///     file monitoring systems, to track and respond to changes on the file system.
/// </remarks>
public sealed partial class ServerProvider
{
	/// <summary>
	///     The ServerCallback class is an internal component of the ServerProvider class that manages
	///     file system monitoring and event handling for changes within the server's file structure.
	/// </summary>
	/// <remarks>
	///     This class is responsible for setting up and managing a FileSystemWatcher instance to monitor
	///     file changes such as creation, deletion, renaming, and modifications. It utilizes an ActionBlock
	///     for processing file change events asynchronously and ensures proper resource cleanup by implementing IDisposable.
	/// </remarks>
	internal class ServerCallback : IDisposable
	{
		private readonly ServerProvider _serverProvider;
		public readonly ActionBlock<FileChangedEventArgs> fileChangedActionBlock;
		internal readonly FileSystemWatcher fileSystemWatcher;
		private bool _disposedValue;


		/// <summary>
		///     Represents an internal component of the ServerProvider class used for monitoring and handling
		///     file system events within the server's file structure.
		/// </summary>
		/// <remarks>
		///     The ServerCallback class works with a FileSystemWatcher instance to observe changes in the
		///     specified server path, including events related to file creation, deletion, renaming, and modifications.
		///     It ensures asynchronous processing of file change events through an ActionBlock and proper cleanup of
		///     system resources by implementing the IDisposable interface.
		/// </remarks>
		public ServerCallback(ServerProvider serverProvider)
		{
			_serverProvider = serverProvider;
			/* 2 ways */
			/* fileChangedActionBlock = new(data => serverProvider.RaiseFileChanged(data));

			fileSystemWatcher = new FileSystemWatcher
			{
				NotifyFilter = NotifyFilters.LastWrite |
				NotifyFilters.DirectoryName |
				NotifyFilters.FileName |
				NotifyFilters.Size |
				NotifyFilters.Attributes,

				Filter = "*",
				IncludeSubdirectories = true,
				Path = serverProvider.Parameter.ServerPath
			};
			fileSystemWatcher.Created += FileSystemWatcher_Created;
			fileSystemWatcher.Deleted += FileSystemWatcher_Deleted;
			fileSystemWatcher.Renamed += FileSystemWatcher_Renamed;
			fileSystemWatcher.Changed += FileSystemWatcher_Changed;
			fileSystemWatcher.Error += FileSystemWatcher_Error;

			fileSystemWatcher.EnableRaisingEvents = true; */
		}


		/// <summary>
		///     Releases all resources used by the ServerCallback instance.
		/// </summary>
		/// <remarks>
		///     This method ensures proper disposal of resources allocated by the ServerCallback instance,
		///     including cleaning up the FileSystemWatcher and ActionBlock components.
		///     It invokes the protected Dispose method to handle the resource cleanup logic and suppresses
		///     finalization by the garbage collector to optimize resource management.
		/// </remarks>
		public void Dispose()
		{
			// Ändern Sie diesen Code nicht. Fügen Sie Bereinigungscode in der Methode "Dispose(bool disposing)" ein.
			Dispose(true);
			GC.SuppressFinalize(this);
		}


		/// <summary>
		///     Handles the error event of the FileSystemWatcher and updates the server provider status to Failed.
		/// </summary>
		/// <param name="sender">The source of the event, typically the FileSystemWatcher.</param>
		/// <param name="e">An instance of <see cref="ErrorEventArgs" /> that contains the event data.</param>
		/// <remarks>
		///     This method disables the FileSystemWatcher by setting its EnableRaisingEvents property to false
		///     and sets the server provider's status to Failed. This ensures the system recognizes the failure
		///     and initiates appropriate actions, such as triggering a full synchronization when the connection
		///     is reestablished.
		/// </remarks>
		private void FileSystemWatcher_Error(object sender, ErrorEventArgs e)
		{
			fileSystemWatcher.EnableRaisingEvents = false;

			// Set ServerProviderState to FAILED.
			// Connection Monitoring will trigger a FullSync if connection could be reestablished.
			_serverProvider.SetProviderStatus(ServerProviderStatus.Failed);
		}


		/// <summary>
		///     Handles the event triggered when a file or directory within the monitored file system structure is changed.
		/// </summary>
		/// <param name="sender">The source of the event, typically the instance of <see cref="FileSystemWatcher" /> that raised the event.</param>
		/// <param name="e">The <see cref="FileSystemEventArgs" /> containing details about the change, such as the file's path and the type of change.</param>
		/// <remarks>
		///     This method processes file or directory changes by posting a <see cref="FileChangedEventArgs" /> object to the <see cref="ActionBlock{T}" /> for further handling.
		///     It filters out changes from specific system directories (e.g., "$Recycle.bin") and placeholders starting with "$_".
		/// </remarks>
		private void FileSystemWatcher_Changed(object sender, FileSystemEventArgs e)
		{
			if (e.FullPath.Contains(@"$Recycle.bin"))
			{
				return;
			}

			if (Path.GetFileName(e.FullPath).StartsWith("$_"))
			{
				return;
			}

			try
			{
				fileChangedActionBlock.Post(new FileChangedEventArgs { ChangeType = WatcherChangeTypes.Changed, ResyncSubDirectories = false, Placeholder = new Placeholder(e.FullPath, _serverProvider.GetRelativePath(e.FullPath)) });
			}
			catch (Exception ex)
			{
				Log.Error(ex.Message);
			}
		}


		/// <summary>
		///     Handles the Renamed event of the FileSystemWatcher, triggered when a file or directory within the monitored path is renamed.
		/// </summary>
		/// <param name="sender">The source of the event, typically the FileSystemWatcher instance.</param>
		/// <param name="e">An object of type RenamedEventArgs containing the old and new names of the file or directory, as well as its path.</param>
		/// <remarks>
		///     This method processes rename events by applying filters to exclude specific system or temporary files, and posts the appropriate
		///     file change actions to the ActionBlock for further processing. Additionally, it handles cases involving the recycle bin
		///     to generate file creation or deletion events as needed.
		/// </remarks>
		private void FileSystemWatcher_Renamed(object sender, RenamedEventArgs e)
		{
			if (e.FullPath.Contains(@"$Recycle.bin") && e.OldFullPath.Contains(@"$Recycle.bin"))
			{
				return;
			}

			if (e.Name.StartsWith("$_"))
			{
				return;
			}

			if (e.OldName.StartsWith("$_"))
			{
				FileSystemWatcher_Changed(sender, new FileSystemEventArgs(WatcherChangeTypes.Changed, Path.GetDirectoryName(e.FullPath), e.Name));
				return;
			}

			try
			{
				if (e.FullPath.Contains(@"$Recycle.bin"))
				{
					fileChangedActionBlock.Post(new FileChangedEventArgs { ChangeType = WatcherChangeTypes.Deleted, Placeholder = new Placeholder(_serverProvider.GetRelativePath(e.OldFullPath), false) });
				}
				else if (e.OldFullPath.Contains(@"$Recycle.bin"))
				{
					fileChangedActionBlock.Post(new FileChangedEventArgs { ChangeType = WatcherChangeTypes.Created, Placeholder = new Placeholder(_serverProvider.GetRelativePath(e.FullPath), false) });
				}
				else
				{
					fileChangedActionBlock.Post(new FileChangedEventArgs { ChangeType = WatcherChangeTypes.Renamed, Placeholder = new Placeholder(e.FullPath, _serverProvider.GetRelativePath(e.FullPath)), OldRelativePath = _serverProvider.GetRelativePath(e.OldFullPath) });
				}
			}
			catch (Exception ex)
			{
				Log.Error(ex.Message);
			}
		}


		/// <summary>
		///     Handles the FileSystemWatcher Deleted event and processes the deletion of files within the monitored directory.
		/// </summary>
		/// <param name="sender">The source of the event, typically the FileSystemWatcher instance.</param>
		/// <param name="e">
		///     Provides data related to the file system event, including the full path of the affected file
		///     and the type of change that occurred.
		/// </param>
		/// <remarks>
		///     This method filters out specific system files and temporary files to avoid unnecessary processing.
		///     Valid file deletion events are posted to the associated ActionBlock for further asynchronous handling.
		/// </remarks>
		private void FileSystemWatcher_Deleted(object sender, FileSystemEventArgs e)
		{
			if (e.FullPath.Contains(@"$Recycle.bin"))
			{
				return;
			}

			if (Path.GetFileName(e.FullPath).StartsWith("$_"))
			{
				return;
			}

			try
			{
				fileChangedActionBlock.Post(new FileChangedEventArgs { ChangeType = e.ChangeType, ResyncSubDirectories = false, Placeholder = new Placeholder(_serverProvider.GetRelativePath(e.FullPath), false) });
			}
			catch (Exception ex)
			{
				Log.Error(ex.Message);
			}
		}


		/// <summary>
		///     Handles the 'Created' event triggered by the FileSystemWatcher to process newly created files
		///     within the monitored server directory.
		/// </summary>
		/// <param name="sender">The source of the FileSystemWatcher event, typically the FileSystemWatcher instance.</param>
		/// <param name="e">Provides data specific to the file creation event, including the file path and type of change.</param>
		/// <remarks>
		///     This method checks for specific conditions such as system-protected files and temporary placeholders before posting
		///     a file change event to the ActionBlock for asynchronous processing. It ensures only valid changes are processed and
		///     gracefully handles exceptions that may occur during the event handling.
		/// </remarks>
		private void FileSystemWatcher_Created(object sender, FileSystemEventArgs e)
		{
			if (e.FullPath.Contains(@"$Recycle.bin"))
			{
				return;
			}

			if (Path.GetFileName(e.FullPath).StartsWith("$_"))
			{
				return;
			}

			try
			{
				fileChangedActionBlock.Post(new FileChangedEventArgs { ChangeType = e.ChangeType, ResyncSubDirectories = false, Placeholder = new Placeholder(e.FullPath, _serverProvider.GetRelativePath(e.FullPath)) });
			}
			catch (Exception ex)
			{
				Log.Error(ex.Message);
			}
		}


		/// <summary>
		///     Releases the unmanaged resources used by the ServerCallback and optionally releases the managed resources.
		/// </summary>
		/// <remarks>
		///     This method ensures that both managed and unmanaged resources are properly disposed of
		///     to free memory and other resources, preventing memory leaks due to unclosed handles
		///     or pending operations in the FileSystemWatcher and ActionBlock instances.
		/// </remarks>
		/// <param name="disposing">
		///     A boolean value indicating whether to release both managed and unmanaged resources
		///     (true) or only unmanaged resources (false). When true, it disposes of the FileSystemWatcher
		///     and completes the ActionBlock.
		/// </param>
		protected virtual void Dispose(bool disposing)
		{
			if (!_disposedValue)
			{
				if (disposing)
				{
					if (fileSystemWatcher != null)
					{
						fileSystemWatcher.EnableRaisingEvents = false;
						fileSystemWatcher.Dispose();
					}

					fileChangedActionBlock?.Complete();
				}

				_disposedValue = true;
			}
		}
	}
}
