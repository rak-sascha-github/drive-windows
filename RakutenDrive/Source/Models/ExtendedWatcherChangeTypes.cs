using System;


namespace RakutenDrive.Models;

/// <summary>
///     Specifies the type of changes detected by a file watcher, including extended capabilities.
/// </summary>
/// <remarks>
///     This enumeration extends standard file change types to include additional operations such as file copy and move.
///     It is decorated with the <see cref="FlagsAttribute" /> to allow bitwise combination of its member values.
/// </remarks>
[Flags]
public enum ExtendedWatcherChangeTypes
{
	// Original values
	Created = 1,
	Deleted = 2,
	Changed = 4,
	Renamed = 8,
	All = Created | Deleted | Changed | Renamed,

	// New values
	Copied = 16,
	Moved = 32,
	AllExtended = All | Copied | Moved
}
