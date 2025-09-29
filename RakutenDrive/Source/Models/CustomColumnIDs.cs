namespace RakutenDrive.Models;

/// <summary>
///     Represents the IDs for custom columns used in the application.
///     These columns provide additional metadata or functionality related to file and content management.
/// </summary>
public enum CustomColumnIDs
{
	/// <summary>
	///     Lock Owner column ID. The lock icon is being displayed in the Windows File Manager Status column.
	/// </summary>
	LockOwnerIcon = 2,

	/// <summary>
	///     Lock Scope column ID. Shows if the lock is Exclusive or Shared.
	/// </summary>
	LockScope = 4,

	/// <summary>
	///     Lock Expires column ID.
	/// </summary>
	LockExpirationDate = 5,

	/// <summary>
	///     Content ETag column ID.
	/// </summary>
	ContentETag = 6,

	/// <summary>
	///     Metadata ETag column ID.
	/// </summary>
	MetadataETag = 7
}
