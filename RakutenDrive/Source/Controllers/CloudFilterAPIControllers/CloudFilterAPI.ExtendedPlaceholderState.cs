using System;
using System.IO;
using System.Threading.Tasks;
using RakutenDrive.Models;
using RakutenDrive.Utils;
using Vanara.Extensions;
using Vanara.PInvoke;


namespace RakutenDrive.Controllers.CloudFilterAPIControllers;

/// <summary>
///     Represents the implementation of the Cloud Filter API for managing cloud file placeholders
///     and related file system operations.
/// </summary>
public partial class CloudFilterAPI
{
	/// <summary>
	///     Represents an extended state of a placeholder file or directory in the file system,
	///     providing attributes, metadata, and functionality for managing its state.
	/// </summary>
	public sealed class ExtendedPlaceholderState : IDisposable
	{
		// --------------------------------------------------------------------------------------------
		// PROPERTIES
		// --------------------------------------------------------------------------------------------
		#region PROPERTIES

		public FileAttributes Attributes;

		public string ETag;
		public CldApi.CF_PLACEHOLDER_STATE PlaceholderState;

		public string FullPath => _fullPath;
		public bool IsDirectory => Attributes.HasFlag(FileAttributes.Directory);
		public bool IsPlaceholder => PlaceholderState.HasFlag(CldApi.CF_PLACEHOLDER_STATE.CF_PLACEHOLDER_STATE_PLACEHOLDER);

		private ulong _fileSize;
		private DateTime _lastWriteTime;
		private readonly string _fullPath;
		private WIN32_FIND_DATA _findData;
		private CldApi.CF_PLACEHOLDER_STANDARD_INFO? _placeholderInfoStandard;
		private SafeHandlers.SafeCreateFileForCldApi _safeFileHandleForCldApi;

		private bool disposedValue;

		#endregion


		// --------------------------------------------------------------------------------------------
		// ACCESSORS
		// --------------------------------------------------------------------------------------------
		#region ACCESSORS

		/// <summary>
		///     Gets the placeholder standard information for a file or directory that is managed by the cloud file provider.
		/// </summary>
		/// <remarks>
		///     This property retrieves the <see cref="CldApi.CF_PLACEHOLDER_STANDARD_INFO" /> structure, which contains information
		///     such as the file's hydration state, pin state, and in-sync state. It ensures the placeholder information is accessible
		///     and cached when required.
		///     If the placeholder state does not include the relevant flag or if no file path is available, the property initializes
		///     a default instance of <see cref="CldApi.CF_PLACEHOLDER_STANDARD_INFO" />. Otherwise, it obtains the placeholder information
		///     using the provided cloud file API handle.
		/// </remarks>
		/// <exception cref="InvalidOperationException">
		///     Thrown if the placeholder cannot be managed due to missing or invalid configuration.
		/// </exception>
		public CldApi.CF_PLACEHOLDER_STANDARD_INFO PlaceholderInfoStandard
		{
			get
			{
				if (_placeholderInfoStandard == null)
				{
					if (string.IsNullOrEmpty(_fullPath))
					{
						_placeholderInfoStandard = new CldApi.CF_PLACEHOLDER_STANDARD_INFO();
					}
					else if (!PlaceholderState.HasFlag(CldApi.CF_PLACEHOLDER_STATE.CF_PLACEHOLDER_STATE_PLACEHOLDER))
					{
						_placeholderInfoStandard = new CldApi.CF_PLACEHOLDER_STANDARD_INFO();
					}
					else
					{
						Log.Debug("GetPlaceholderInfoStandard: " + FullPath);
						_placeholderInfoStandard = GetPlaceholderInfoStandard(SafeFileHandleForCldApi);
					}
				}

				return _placeholderInfoStandard.Value;
			}
		}


		/// <summary>
		///     Gets a safe file handle for operations performed by the cloud file API.
		/// </summary>
		/// <remarks>
		///     This property retrieves a handle to the file or directory specified by the current placeholder's path.
		///     If the handle has not already been initialized, it creates a new handle using the full path of the
		///     file or directory, distinguishing between file and directory attributes as required. The handle is
		///     used by various cloud file operations such as hydrating, dehydrating, or modifying the placeholder state.
		///     Proper management of this handle ensures safe and effective interaction with the cloud file API.
		///     If the file path is not defined or invalid, the handle will not be properly initialized.
		/// </remarks>
		/// <exception cref="InvalidOperationException">
		///     Thrown if the safe handle cannot be created due to an invalid or missing file path, or if the
		///     required file attributes are not properly set.
		/// </exception>
		public HFILE SafeFileHandleForCldApi
		{
			get
			{
				if (_safeFileHandleForCldApi == null)
				{
					_safeFileHandleForCldApi = new SafeHandlers.SafeCreateFileForCldApi(_fullPath, IsDirectory);
				}

				return _safeFileHandleForCldApi;
			}
		}

		#endregion


		// --------------------------------------------------------------------------------------------
		// CONSTRUCTORS
		// --------------------------------------------------------------------------------------------
		#region CONSTRUCTORS

		/// <summary>
		///     Represents the state of an extended placeholder in the file system and provides methods for
		///     managing or inspecting placeholder data.
		/// </summary>
		/// <remarks>
		///     This class is utilized internally for operations such as placeholder updates, validation,
		///     and synchronization in file and folder operations.
		/// </remarks>
		public ExtendedPlaceholderState(string fullPath)
		{
			_fullPath = fullPath;
			using (Kernel32.FindFirstFile(@"\\?\" + _fullPath, out var findData))
			{
				SetValuesByFindData(findData);
			}
		}


		/// <summary>
		///     Represents an extended state and metadata handling mechanism for file system placeholders,
		///     including methods for synchronizing and managing placeholder attributes and states.
		/// </summary>
		/// <remarks>
		///     This class provides functionality to read, update, and manipulate placeholder information,
		///     ensuring interaction with file system placeholders is programmatically accessible and manageable
		///     through various state and attribute configurations.
		/// </remarks>
		public ExtendedPlaceholderState(WIN32_FIND_DATA findData, string directory)
		{
			if (!string.IsNullOrEmpty(directory))
			{
				_fullPath = directory + "\\" + findData.cFileName;
			}

			_findData = findData;
			SetValuesByFindData(findData);
		}

		#endregion


		// --------------------------------------------------------------------------------------------
		// PUBLIC METHODS
		// --------------------------------------------------------------------------------------------
		#region PUBLIC METHODS

		/// <summary>
		///     Releases all resources used by the <see cref="ExtendedPlaceholderState" /> instance and performs
		///     necessary cleanup operations.
		/// </summary>
		/// <remarks>
		///     This method ensures proper disposal of unmanaged resources and suppresses finalization
		///     to optimize memory management. It should be called when the object is no longer needed
		///     to release resources deterministically.
		/// </remarks>
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}


		/// <summary>
		///     Sets the placeholder's in-sync state to the specified value, ensuring that the placeholder's state
		///     reflects whether it is synchronized with the cloud storage.
		/// </summary>
		/// <param name="inSyncState">The desired in-sync state to apply, expressed as a value of <see cref="CldApi.CF_IN_SYNC_STATE" />.</param>
		/// <returns>
		///     A <see cref="GenericResult" /> object indicating the success or failure of the operation. Returns a successful result
		///     if the in-sync state is updated. Otherwise, contains an error code detailing the failure.
		/// </returns>
		/// <remarks>
		///     This method checks if the current object is a placeholder and verifies whether the desired in-sync state
		///     differs from the current state. Upon success, the cached placeholder data is updated to avoid placing unnecessary
		///     reloads and synchronizations on the placeholder structure.
		/// </remarks>
		public GenericResult SetInSyncState(CldApi.CF_IN_SYNC_STATE inSyncState)
		{
			if (!IsPlaceholder)
			{
				return new GenericResult(NtStatus.STATUS_NOT_A_CLOUD_FILE);
			}

			if (PlaceholderInfoStandard.InSyncState == inSyncState)
			{
				return new GenericResult();
			}

			Log.Debug("SetInSyncState " + _fullPath + " " + inSyncState);
			var res = CldApi.CfSetInSyncState(SafeFileHandleForCldApi, inSyncState, CldApi.CF_SET_IN_SYNC_FLAGS.CF_SET_IN_SYNC_FLAG_NONE);

			if (res.Succeeded)
			{
				//Reload();

				// Prevent reload by applying results directly to cached values:
				if (_placeholderInfoStandard != null)
				{
					var p = _placeholderInfoStandard.Value;
					p.InSyncState = inSyncState;
					_placeholderInfoStandard = p;
				}

				if (inSyncState == CldApi.CF_IN_SYNC_STATE.CF_IN_SYNC_STATE_IN_SYNC)
				{
					PlaceholderState |= CldApi.CF_PLACEHOLDER_STATE.CF_PLACEHOLDER_STATE_IN_SYNC;
				}
				else
				{
					PlaceholderState ^= CldApi.CF_PLACEHOLDER_STATE.CF_PLACEHOLDER_STATE_IN_SYNC;
				}


				return new GenericResult();
			}

			Log.Warn("SetInSyncState FAILED " + _fullPath + " Error: " + res.Code);
			return new GenericResult((int)res);
		}


		/// <summary>
		///     Converts a file or directory to a placeholder if necessary.
		///     Returns a GenericResult indicating whether the conversion was successful,
		///     or if the file or directory is already a placeholder.
		/// </summary>
		/// <param name="markInSync">Indicates whether the placeholder should be marked as in-sync.</param>
		/// <param name="fileIdentity">
		///     An optional identity for the file, used during the conversion process.
		///     If null, the identity will be derived from the file's local path.
		/// </param>
		/// <returns>A GenericResult object representing the success or failure of the operation.</returns>
		public GenericResult ConvertToPlaceholder(bool markInSync, CloudFileIdentity? fileIdentity = null)
		{
			if (string.IsNullOrEmpty(_fullPath))
			{
				return new GenericResult(NtStatus.STATUS_UNSUCCESSFUL);
			}

			if (IsPlaceholder)
			{
				return new GenericResult(NtStatus.STATUS_SUCCESS);
			}

			Log.Debug("ConvertToPlaceholder " + _fullPath);

			using (SafeHandlers.SafeOpenFileWithOplock fHandle = new(_fullPath, CldApi.CF_OPEN_FILE_FLAGS.CF_OPEN_FILE_FLAG_EXCLUSIVE))
			{
				if (fHandle.IsInvalid)
				{
					Log.Warn("ConvertToPlaceholder FAILED: Invalid Handle!");
					return new GenericResult(NtStatus.STATUS_UNSUCCESSFUL);
				}

				var flags = markInSync ? CldApi.CF_CONVERT_FLAGS.CF_CONVERT_FLAG_MARK_IN_SYNC : CldApi.CF_CONVERT_FLAGS.CF_CONVERT_FLAG_ENABLE_ON_DEMAND_POPULATION;
				HRESULT res;
				var identity = fileIdentity ?? CloudFileIdentity.FromLocalPath(_fullPath);
				using (var fileId = identity.Serialize())
				{
					res = CldApi.CfConvertToPlaceholder(fHandle, fileId.Ptr, fileId.Size, flags, out var usn);
					if (res.Succeeded)
					{
						Reload();
					}

					Log.Error("ConvertToPlaceholder FAILED: Error " + res.Code);
					return new GenericResult(res.Succeeded);
				}
			}
		}


		/// <summary>
		///     Attempts to revert a placeholder file or directory to its unmanaged state, ensuring
		///     that it is no longer treated as a cloud placeholder. Provides an option to prevent
		///     data loss during the operation.
		/// </summary>
		/// <param name="allowDataLoos">
		///     Indicates whether the operation should proceed even if the placeholder's state
		///     suggests potential data loss. If set to <c>false</c>, the placeholder will first
		///     be hydrated, and synchronization state will be validated before reverting.
		/// </param>
		/// <returns>
		///     A <see cref="GenericResult" /> object representing the result of the operation,
		///     including success or error state and an optional message.
		/// </returns>
		public GenericResult RevertPlaceholder(bool allowDataLoos)
		{
			if (string.IsNullOrEmpty(_fullPath))
			{
				return new GenericResult(CloudExceptions.FileOrDirectoryNotFound);
			}

			if (!IsPlaceholder)
			{
				return new GenericResult { Status = NtStatus.STATUS_NOT_A_CLOUD_FILE, Message = NtStatus.STATUS_NOT_A_CLOUD_FILE.ToString(), Succeeded = true };
			}

			Log.Debug("RevertPlaceholder " + _fullPath);

			using (SafeHandlers.SafeOpenFileWithOplock fHandle = new(_fullPath, CldApi.CF_OPEN_FILE_FLAGS.CF_OPEN_FILE_FLAG_EXCLUSIVE))
			{
				if (fHandle.IsInvalid)
				{
					Log.Warn("RevertPlaceholder FAILED: Invalid Handle!");
					return new GenericResult(NtStatus.STATUS_CLOUD_FILE_IN_USE);
				}

				if (!allowDataLoos)
				{
					var ret = HydratePlaceholder();
					if (!ret.Succeeded)
					{
						return ret;
					}

					if (PlaceholderState.HasFlag(CldApi.CF_PLACEHOLDER_STATE.CF_PLACEHOLDER_STATE_PARTIAL) || PlaceholderState.HasFlag(CldApi.CF_PLACEHOLDER_STATE.CF_PLACEHOLDER_STATE_INVALID) || !PlaceholderState.HasFlag(CldApi.CF_PLACEHOLDER_STATE.CF_PLACEHOLDER_STATE_IN_SYNC))
					{
						return new GenericResult(NtStatus.STATUS_CLOUD_FILE_NOT_IN_SYNC);
					}
				}

				var res = CldApi.CfRevertPlaceholder(fHandle, CldApi.CF_REVERT_FLAGS.CF_REVERT_FLAG_NONE);
				if (res.Succeeded)
				{
					Reload();
				}

				Log.Warn("RevertPlaceholder FAILED: Error " + res.Code);
				return new GenericResult((int)res);
			}
		}


		/// <summary>
		///     Dehydrates a placeholder file by removing its local content while maintaining its metadata in the file system.
		/// </summary>
		/// <param name="setPinStateUnspecified">Determines whether the placeholder's pin state should be set to unspecified after dehydration.</param>
		/// <returns>A <see cref="GenericResult" /> object representing the outcome of the operation. The result indicates success, failure, or a specific error status such as if the file is not a placeholder or is pinned.</returns>
		public GenericResult DehydratePlaceholder(bool setPinStateUnspecified)
		{
			if (!IsPlaceholder)
			{
				return new GenericResult(NtStatus.STATUS_NOT_A_CLOUD_FILE);
			}

			if (PlaceholderInfoStandard.PinState == CldApi.CF_PIN_STATE.CF_PIN_STATE_PINNED)
			{
				return new GenericResult(NtStatus.STATUS_CLOUD_FILE_PINNED);
			}

			Log.Debug("DehydratePlaceholder " + _fullPath);
			var res = CldApi.CfDehydratePlaceholder(SafeFileHandleForCldApi, 0, -1, CldApi.CF_DEHYDRATE_FLAGS.CF_DEHYDRATE_FLAG_NONE);
			if (res.Succeeded)
			{
				Reload();
			}
			else
			{
				Log.Warn("DehydratePlaceholder FAILED" + _fullPath + " Error: " + res.Code);
				return new GenericResult((int)res);
			}

			if (res.Succeeded && setPinStateUnspecified)
			{
				SetPinState(CldApi.CF_PIN_STATE.CF_PIN_STATE_UNSPECIFIED);
			}

			return new GenericResult((int)res);
		}


		/// <summary>
		///     Attempts to hydrate a placeholder file by retrieving its content and metadata
		///     from the cloud provider. This method ensures that the placeholder file becomes fully usable by the system.
		/// </summary>
		/// <returns>
		///     A <see cref="GenericResult" /> indicating the success or failure of the hydration operation.
		///     Returns an error result if the file path is invalid, the file is not a placeholder, or
		///     the hydration process encounters an issue.
		/// </returns>
		public GenericResult HydratePlaceholder()
		{
			if (string.IsNullOrEmpty(_fullPath))
			{
				return new GenericResult(CloudExceptions.FileOrDirectoryNotFound);
			}

			if (!IsPlaceholder)
			{
				return new GenericResult(NtStatus.STATUS_CLOUD_FILE_NOT_SUPPORTED);
			}

			Log.Debug("HydratePlaceholder " + _fullPath);
			var res = CldApi.CfHydratePlaceholder(SafeFileHandleForCldApi);
			if (res.Succeeded)
			{
				Reload();
				return new GenericResult();
			}

			Log.Warn("HydratePlaceholder FAILED " + _fullPath + " Error: " + res.Code);
			return new GenericResult((int)res);
		}


		/// <summary>
		///     Asynchronously hydrates the extended placeholder by ensuring it is populated with the required data,
		///     while maintaining its metadata and file system attributes.
		/// </summary>
		/// <returns>
		///     Returns a <see cref="GenericResult" /> indicating the success or failure of the hydration operation.
		/// </returns>
		public async Task<GenericResult> HydratePlaceholderAsync()
		{
			if (string.IsNullOrEmpty(_fullPath))
			{
				return new GenericResult(CloudExceptions.FileOrDirectoryNotFound);
			}

			if (!IsPlaceholder)
			{
				return new GenericResult(NtStatus.STATUS_CLOUD_FILE_NOT_SUPPORTED);
			}

			Log.Debug("HydratePlaceholderAsync " + _fullPath);
			var res = await Task.Run(() => { return CldApi.CfHydratePlaceholder(SafeFileHandleForCldApi); }).ConfigureAwait(false);
			if (res.Succeeded)
			{
				Log.Debug("HydratePlaceholderAsync Completed: " + _fullPath);
				Reload();
				return new GenericResult();
			}

			Log.Warn("HydratePlaceholderAsync FAILED " + _fullPath + " Error: " + res.Code);
			return new GenericResult((int)res);
		}


		/// <summary>
		///     Sets the pin state for the current placeholder, determining whether the content is available locally
		///     or excluded from the local file system.
		/// </summary>
		/// <param name="state">
		///     The <see cref="CldApi.CF_PIN_STATE" /> value indicating the desired pin state for the placeholder.
		///     Possible values include excluding, unspecified, or pinned states.
		/// </param>
		/// <param name="recursive">
		///     A boolean value indicating whether the pin state change should be applied recursively
		///     to all placeholders within a directory. Default is false.
		/// </param>
		/// <returns>
		///     A boolean value indicating whether the pin state was successfully updated. Returns false
		///     if the current item is not a placeholder or if the pin state change fails.
		/// </returns>
		public bool SetPinState(CldApi.CF_PIN_STATE state, bool recursive = false)
		{
			if (!IsPlaceholder)
			{
				return false;
			}

			if ((int)PlaceholderInfoStandard.PinState == (int)state)
			{
				return true;
			}

			Log.Debug("SetPinState " + _fullPath + " " + state);

			var flags = recursive ? CldApi.CF_SET_PIN_FLAGS.CF_SET_PIN_FLAG_RECURSE : CldApi.CF_SET_PIN_FLAGS.CF_SET_PIN_FLAG_NONE;
			var res = CldApi.CfSetPinState(SafeFileHandleForCldApi, state, flags);

			if (res.Succeeded)
			{
				//Reload();

				// Prevent reload by applying results directly to cached values:
				if (_placeholderInfoStandard != null)
				{
					var p = _placeholderInfoStandard.Value;
					p.PinState = state;
					_placeholderInfoStandard = p;
				}
			}

			Log.Warn("SetPinState FAILED " + _fullPath + " Error: " + res.Code);
			return res.Succeeded;
		}


		/// <summary>
		///     Enables on-demand population of a directory placeholder if certain conditions are met,
		///     such as verifying that the object is a placeholder and a directory.
		/// </summary>
		/// <returns>
		///     Returns <c>true</c> if the operation to enable on-demand population succeeds; otherwise, <c>false</c>.
		/// </returns>
		public bool EnableOnDemandPopulation()
		{
			if (string.IsNullOrEmpty(_fullPath))
			{
				return false;
			}

			if (!IsPlaceholder)
			{
				return false;
			}

			if (!IsDirectory)
			{
				return false;
			}

			if (PlaceholderState.HasFlag(CldApi.CF_PLACEHOLDER_STATE.CF_PLACEHOLDER_STATE_PARTIAL))
			{
				return true;
			}

			Log.Debug("EnableOnDemandPopulation " + _fullPath);

			using (SafeHandlers.SafeOpenFileWithOplock fHandle = new(_fullPath, CldApi.CF_OPEN_FILE_FLAGS.CF_OPEN_FILE_FLAG_NONE))
			{
				if (fHandle.IsInvalid)
				{
					Log.Warn("EnableOnDemandPopulation FAILED: Invalid Handle!");
					return false;
				}

				HRESULT res;
				using (var fileIdentity = CloudFileIdentity.FromLocalPath(_fullPath).Serialize())
				{
					long usn = 0;
					res = CldApi.CfUpdatePlaceholder(fHandle, new CldApi.CF_FS_METADATA(), fileIdentity.Ptr, fileIdentity.Size, null, 0, CldApi.CF_UPDATE_FLAGS.CF_UPDATE_FLAG_ENABLE_ON_DEMAND_POPULATION, ref usn);

					if (res.Succeeded)
					{
						//Reload of Placeholder after EnableOnDemandPopulation triggers FETCH_PLACEHOLDERS
						//Reload();
						PlaceholderState |= CldApi.CF_PLACEHOLDER_STATE.CF_PLACEHOLDER_STATE_PARTIAL | CldApi.CF_PLACEHOLDER_STATE.CF_PLACEHOLDER_STATE_PARTIALLY_ON_DISK;
					}

					Log.Warn("ConvertToPlaceholder FAILED: Error " + res.Code);
					return res.Succeeded;
				}
			}
		}


		/// <summary>
		///     Disables on-demand population for a placeholder directory in the cloud file system.
		/// </summary>
		/// <returns>
		///     True if the operation succeeds or if the placeholder does not require on-demand population; otherwise, false.
		/// </returns>
		/// <remarks>
		///     This method checks for conditions such as whether the target is a valid placeholder, is a directory,
		///     and whether it has a partial placeholder state, before attempting to disable on-demand population.
		/// </remarks>
		public bool DisableOnDemandPopulation()
		{
			if (string.IsNullOrEmpty(_fullPath))
			{
				return false;
			}

			if (!IsPlaceholder)
			{
				return false;
			}

			if (!IsDirectory)
			{
				return false;
			}

			if (!PlaceholderState.HasFlag(CldApi.CF_PLACEHOLDER_STATE.CF_PLACEHOLDER_STATE_PARTIAL))
			{
				return true;
			}

			Log.Debug("EnableOnDemandPopulation " + _fullPath);

			using (SafeHandlers.SafeOpenFileWithOplock fHandle = new(_fullPath, CldApi.CF_OPEN_FILE_FLAGS.CF_OPEN_FILE_FLAG_NONE))
			{
				if (fHandle.IsInvalid)
				{
					Log.Warn("EnableOnDemandPopulation FAILED: Invalid Handle!");
					return false;
				}

				HRESULT res;
				using (var fileIdentity = CloudFileIdentity.FromLocalPath(_fullPath).Serialize())
				{
					long usn = 0;
					res = CldApi.CfUpdatePlaceholder(fHandle, new CldApi.CF_FS_METADATA(), fileIdentity.Ptr, fileIdentity.Size, null, 0, CldApi.CF_UPDATE_FLAGS.CF_UPDATE_FLAG_DISABLE_ON_DEMAND_POPULATION, ref usn);

					if (res.Succeeded)
					{
						//Reload of Placeholder after EnableOnDemandPopulation triggers FETCH_PLACEHOLDERS
						//Reload();
						PlaceholderState ^= CldApi.CF_PLACEHOLDER_STATE.CF_PLACEHOLDER_STATE_PARTIAL;
						PlaceholderState ^= CldApi.CF_PLACEHOLDER_STATE.CF_PLACEHOLDER_STATE_PARTIALLY_ON_DISK;
					}

					Log.Warn("ConvertToPlaceholder FAILED: Error " + res.Code);
					return res.Succeeded;
				}
			}
		}


		/// <summary>
		///     Updates the placeholder state with the specified placeholder and update flags.
		/// </summary>
		/// <param name="placeholder">The placeholder object representing the file or directory whose state is to be updated.</param>
		/// <param name="cF_UPDATE_FLAGS">Flags specifying the update behavior for the placeholder state, such as marking it in sync.</param>
		/// <returns>A <see cref="GenericResult" /> object indicating the success or failure of the operation.</returns>
		public GenericResult UpdatePlaceholder(Placeholder placeholder, CldApi.CF_UPDATE_FLAGS cF_UPDATE_FLAGS)
		{
			return UpdatePlaceholder(placeholder, cF_UPDATE_FLAGS, false);
		}


		/// <summary>
		///     Updates the metadata and optionally the data state of the placeholder object in the file system.
		/// </summary>
		/// <param name="placeholder">
		///     The placeholder object containing metadata and identity details to be updated in the file system.
		/// </param>
		/// <param name="cF_UPDATE_FLAGS">
		///     Flags specifying the update behavior, such as whether the placeholder is hydrated or syncable.
		/// </param>
		/// <param name="markDataInvalid">
		///     A boolean value indicating whether to mark the data as invalid, which will trigger ranges of the file
		///     to be dehydrated if set to true.
		/// </param>
		/// <returns>
		///     A <see cref="GenericResult" /> instance representing the success or failure of the operation, along
		///     with error details if applicable.
		/// </returns>
		public GenericResult UpdatePlaceholder(Placeholder placeholder, CldApi.CF_UPDATE_FLAGS cF_UPDATE_FLAGS, bool markDataInvalid)
		{
			if (string.IsNullOrEmpty(_fullPath))
			{
				return new GenericResult(CloudExceptions.FileOrDirectoryNotFound);
			}

			if (!IsPlaceholder)
			{
				return new GenericResult(NtStatus.STATUS_CLOUD_FILE_NOT_SUPPORTED);
			}

			Log.Debug("UpdatePlaceholder " + _fullPath + " Flags: " + cF_UPDATE_FLAGS);
			GenericResult res = new();

			using (SafeHandlers.SafeOpenFileWithOplock fHandle = new(_fullPath, CldApi.CF_OPEN_FILE_FLAGS.CF_OPEN_FILE_FLAG_EXCLUSIVE))
			{
				if (fHandle.IsInvalid)
				{
					Log.Warn("UpdatePlaceholder FAILED: Invalid Handle!");
					return new GenericResult(NtStatus.STATUS_CLOUD_FILE_IN_USE);
				}

				using (var fileIdentity = placeholder.FileIdentity.Serialize())
				{
					long usn = 0;
					CldApi.CF_FILE_RANGE[] dehydrateRanges = null;
					uint dehydrateRangesCount = 0;

					if (markDataInvalid)
					{
						dehydrateRanges = new CldApi.CF_FILE_RANGE[1];
						dehydrateRanges[0] = new CldApi.CF_FILE_RANGE { StartingOffset = 0, Length = (long)_fileSize };
						dehydrateRangesCount = 1;
					}

					var res1 = CldApi.CfUpdatePlaceholder(fHandle, CreateFSMetaData(placeholder), fileIdentity.Ptr, fileIdentity.Size, dehydrateRanges, dehydrateRangesCount, cF_UPDATE_FLAGS, ref usn);
					if (!res1.Succeeded)
					{
						res.SetException(res1.GetException());
					}

					if (res.Succeeded)
					{
						Reload();
					}

					Log.Warn("UpdatePlaceholder FAILED: Error " + res.Message);
					return res;
				}
			}
		}


		/// <summary>
		///     Retrieves the file identity for a specified placeholder using its safe file handle.
		/// </summary>
		/// <returns>
		///     A byte array containing the placeholder's file identity data if successful, or null if the operation fails.
		/// </returns>
		public byte[]? GetPlaceHolderFileIdentity()
		{
			return GetPlaceholderFileIdentity(SafeFileHandleForCldApi);
		}


		/// <summary>
		///     Refreshes and updates the internal state of the placeholder data by retrieving the latest file or directory attributes
		///     from the file system for the associated path.
		/// </summary>
		/// <remarks>
		///     This method ensures that the state of the placeholder is synchronized with the current file system state. It retrieves the latest
		///     data, such as attributes and metadata, using file system operations and updates internal properties accordingly.
		/// </remarks>
		public void Reload()
		{
			Log.Debug("Reload Placeholder Data: " + FullPath);
			using (Kernel32.FindFirstFile(@"\\?\" + _fullPath, out var findData))
			{
				SetValuesByFindData(findData);
			}
		}

		#endregion


		// --------------------------------------------------------------------------------------------
		// PRIVATE METHODS
		// --------------------------------------------------------------------------------------------
		#region PRIVATE METHODS

		/// <summary>
		///     Sets the internal state and properties of the extended placeholder instance
		///     based on the provided file data retrieved from the file system.
		/// </summary>
		/// <param name="findData">The file data retrieved from the file system, containing attributes, timestamps, and other metadata.</param>
		private void SetValuesByFindData(WIN32_FIND_DATA findData)
		{
			PlaceholderState = CldApi.CfGetPlaceholderStateFromFindData(findData);
			Attributes = findData.dwFileAttributes;
			_lastWriteTime = findData.ftLastWriteTime.ToDateTime();
			_placeholderInfoStandard = null;
			_fileSize = findData.FileSize;
			ETag = "_" + _lastWriteTime.ToUniversalTime().Ticks + "_" + _fileSize;
		}


		/// <summary>
		///     Releases the resources used by the <see cref="ExtendedPlaceholderState" /> class instance.
		/// </summary>
		/// <param name="disposing">Indicates whether the method is being called from a Dispose method or a finalizer.</param>
		private void Dispose(bool disposing)
		{
			if (!disposedValue)
			{
				if (disposing)
				{
					_safeFileHandleForCldApi?.Dispose();
				}

				disposedValue = true;
			}
		}

		#endregion
	}
}
