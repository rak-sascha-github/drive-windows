using System.Threading.Tasks;


namespace RakutenDrive.Utils;

/// <summary>
///     Helpers for safely launching background Tasks while ensuring exceptions are observed and logged.
/// </summary>
public static class TaskExtensions
{
	/// <summary>
	///     Safely "fire-and-forget" a Task.
	///     - Logs any faulted exception.
	///     - Treats OperationCanceledException as expected noise.
	///     - Attaches a continuation so the exception is observed even if nothing awaits the Task.
	/// </summary>
	/// <param name="task">The task to run.</param>
	/// <param name="name">Short name for logging context.</param>
	public static void FireAndForget(this Task? task, string name)
	{
		if (task is null)  return;

		if (task.IsCompleted)
		{
			// Already completed - observe any exception synchronously
			if (task.IsFaulted && task.Exception is not null)
			{
				Log.Error($"Fire-and-forget task '{name}' faulted (completed): {task.Exception}");
			}

			return;
		}

		_ = task.ContinueWith(t =>
		{
			if (t.IsFaulted && t.Exception is not null)
			{
				Log.Error($"Fire-and-forget task '{name}' faulted: {t.Exception}");
			}
			else if (t.IsCanceled)
			{
				Log.Debug($"Fire-and-forget task '{name}' canceled.");
			}
		}, TaskScheduler.Default);
	}
}
