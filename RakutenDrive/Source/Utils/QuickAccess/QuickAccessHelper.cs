using System;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;


namespace RakutenDrive.Utils.QuickAccess;

/// <summary>
///     Provides utility methods for managing folders in the Quick Access feature of Windows.
/// </summary>
internal class QuickAccessHelper
{
	/// <summary>
	///     Determines whether a specified folder is pinned to Quick Access in Windows.
	/// </summary>
	/// <param name="folderPath">The full path of the folder to check.</param>
	/// <returns>
	///     True if the folder is pinned to Quick Access; otherwise, false.
	/// </returns>
	public static bool IsFolderPinnedToQuickAccess(string folderPath)
	{
		// The shell:::{679f85cb-0220-4080-b29b-5540cc05aab6} is the known folder GUID for Quick Access.
		using (var runspace = RunspaceFactory.CreateRunspace())
		{
			runspace.Open();
			using (var ps = PowerShell.Create())
			{
				ps.Runspace = runspace;
				ps.AddScript(@"
                        param ($folderPath)
                        $shell = New-Object -ComObject shell.application
                        $quickAccess = $shell.Namespace('shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}')
                        $items = $quickAccess.Items()
                        $items | ForEach-Object {
                            if ($_.Path -eq $folderPath) {
                                return $true
                            }
                        }
                        return $false
                    ");
				ps.AddParameter("folderPath", folderPath);

				var results = ps.Invoke();
				if (ps.HadErrors)
				{
					foreach (var error in ps.Streams.Error)
					{
						Log.Error(error.ToString());
					}

					throw new Exception("PowerShell script failed.");
				}

				return results.FirstOrDefault() != null && (bool)results.FirstOrDefault().BaseObject;
			}
		}
	}


	/// <summary>
	///     Pins a specified folder to Quick Access in Windows.
	/// </summary>
	/// <param name="folderPath">The full path of the folder to pin to Quick Access.</param>
	public static void PinToQuickAccess(string folderPath)
	{
		if (IsFolderPinnedToQuickAccess(folderPath))
		{
			Log.Info(@"Folder is already pinned to Quick Access.");
			return;
		}

		using (var runspace = RunspaceFactory.CreateRunspace())
		{
			runspace.Open();
			using (var ps = PowerShell.Create())
			{
				ps.Runspace = runspace;
				var shellApplication = ps.AddCommand("New-Object").AddParameter("ComObject", "shell.application").Invoke();
				dynamic nameSpace = shellApplication.FirstOrDefault()?.Methods["NameSpace"].Invoke(folderPath);
				nameSpace?.Self.InvokeVerb("pintohome");
			}
		}

		Log.Info(@"Folder pinned to Quick Access successfully.");
	}


	/// <summary>
	///     Removes a specified folder from Quick Access in Windows.
	/// </summary>
	/// <param name="pathToFolder">The full path of the folder to remove from Quick Access.</param>
	public static void RemoveFolderFromQuickAccess(string pathToFolder)
	{
		using (var runspace = RunspaceFactory.CreateRunspace())
		{
			runspace.Open();
			var ps = PowerShell.Create();
			var removeScript = $"((New-Object -ComObject shell.application).Namespace(\"shell:::{{679f85cb-0220-4080-b29b-5540cc05aab6}}\").Items() | Where-Object {{ $_.Path -EQ \"{pathToFolder}\" }}).InvokeVerb(\"unpinfromhome\")";

			ps.AddScript(removeScript);
			ps.Invoke();
		}
	}
}
