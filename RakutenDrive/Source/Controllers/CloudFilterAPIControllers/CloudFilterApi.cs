using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using RakutenDrive.Models;
using RakutenDrive.Utils;
using Vanara.Extensions;
using Vanara.InteropServices;
using Vanara.PInvoke;


namespace RakutenDrive.Controllers.CloudFilterAPIControllers;

/// <summary>
///     Provides functionality to interact with the Windows Cloud Files API (CfApi) for managing placeholders and
///     related metadata in a virtualized file system. This class includes methods to retrieve and manipulate
///     file and placeholder information, as well as create and manage placeholder states.
/// </summary>
/// <remarks>
///     The methods in this class utilize the Vanara.PInvoke library and the Windows CfApi functionality to operate
///     on files and directories, allowing operations such as fetching placeholder details, setting synchronization
///     states, and creating placeholder file information. It is supported on Windows platforms only.
/// </remarks>
[SupportedOSPlatform("windows")]
public partial class CloudFilterAPI
{
	// --------------------------------------------------------------------------------------------
	// CONSTANTS
	// --------------------------------------------------------------------------------------------
	#region CONSTANTS

	/// <summary>
	///     Defines the maximum allowable length for an identity value or identifier
	///     used within the Cloud Filter API operations, such as file placeholder
	///     identity serialization and retrieval of placeholder information.
	///     This constant is used in various methods to enforce a boundary when
	///     handling identity-related data structures or when allocating memory
	///     buffers for file identity processing.
	///     The value of this constant may correspond to platform-specific or
	///     application-defined constraints to ensure consistency and prevent issues
	///     related to resource overutilization or buffer overflow.
	/// </summary>
	private const int MAX_IDENTIFY_LENGTH = 4096;

	#endregion


	// ----------------------------------------------------------------------------------------
	// PUBLIC METHODS
	// ----------------------------------------------------------------------------------------
	#region PUBLIC METHODS

	/// <summary>
	///     Retrieves basic placeholder information for a given file or directory.
	/// </summary>
	/// <param name="fullPath">The full path to the file or directory for which to retrieve placeholder information.</param>
	/// <param name="isDirectory">A boolean value indicating whether the specified path refers to a directory (true) or a file (false).</param>
	/// <returns>
	///     A <see cref="CldApi.CF_PLACEHOLDER_BASIC_INFO" /> structure containing the basic placeholder information for the specified file or directory.
	///     If retrieval fails, the method returns the default value of <see cref="CldApi.CF_PLACEHOLDER_BASIC_INFO" />.
	/// </returns>
	public static CldApi.CF_PLACEHOLDER_BASIC_INFO GetPlaceholderInfoBasic(string fullPath, bool isDirectory)
	{
		using SafeHandlers.SafeCreateFileForCldApi h = new(fullPath, isDirectory);

		if (h.IsInvalid)
		{
			var err = Marshal.GetLastWin32Error();
			Log.Error($"GetPlaceholderInfoBasic INVALID Handle! Error: {err}, fullPath: {fullPath}");
			return default;
		}

		try
		{
			return GetPlaceholderInfoBasic(h);
		}
		catch (Exception e)
		{
			Log.Error($"GetPlaceholderInfoBasic FAILED: {e.Message}");
			return default;
		}
	}


	/// <summary>
	///     Retrieves standard placeholder information for a specified file or directory.
	/// </summary>
	/// <param name="fullPath">The full path to the file or directory for which to retrieve placeholder information.</param>
	/// <param name="isDirectory">A boolean value indicating whether the specified path refers to a directory (true) or a file (false).</param>
	/// <returns>
	///     A <see cref="CldApi.CF_PLACEHOLDER_STANDARD_INFO" /> structure containing the standard placeholder information for the specified file or directory.
	///     If retrieval fails, the method returns the default value of <see cref="CldApi.CF_PLACEHOLDER_STANDARD_INFO" />.
	/// </returns>
	public static CldApi.CF_PLACEHOLDER_STANDARD_INFO GetPlaceholderInfoStandard(string fullPath, bool isDirectory)
	{
		using SafeHandlers.SafeCreateFileForCldApi h = new(fullPath, isDirectory);

		if (h.IsInvalid)
		{
			var err = Marshal.GetLastWin32Error();
			Log.Warn($"GetPlaceholderInfoBasic INVALID Handle! Error {err}, fullPath: {fullPath}");
			return default;
		}

		try
		{
			return GetPlaceholderInfoStandard(h);
		}
		catch (Exception e)
		{
			Log.Error($"GetPlaceholderInfoBasic FAILED: {e.Message}");
			return default;
		}
	}


	/// <summary>
	///     Retrieves standard placeholder information for a given file or directory.
	/// </summary>
	/// <param name="fullPath">The full path to the file or directory for which to retrieve placeholder information.</param>
	/// <param name="isDirectory">A boolean value indicating whether the specified path refers to a directory (true) or a file (false).</param>
	/// <returns>
	///     A <see cref="CF_PLACEHOLDER_STANDARD_INFO" /> structure containing the standard placeholder information for the specified file or directory.
	///     If retrieval fails, the method returns the default value of <see cref="CF_PLACEHOLDER_STANDARD_INFO" />.
	/// </returns>
	public static CldApi.CF_PLACEHOLDER_STANDARD_INFO GetPlaceholderInfoStandard(HFILE FileHandle)
	{
		if (FileHandle.IsInvalid)
		{
			Log.Warn("GetPlaceholderInfoStandard INVALID Handle!");
			return default;
		}

		try
		{
			var InfoBufferLength = 1024;
			CldApi.CF_PLACEHOLDER_STANDARD_INFO ResultInfo = default;

			using (SafeHandlers.SafeAllocCoTaskMem bufferPointerHandler = new(InfoBufferLength))
			{
				_ = CldApi.CfGetPlaceholderInfo(FileHandle, CldApi.CF_PLACEHOLDER_INFO_CLASS.CF_PLACEHOLDER_INFO_STANDARD, bufferPointerHandler, (uint)InfoBufferLength, out var returnedLength);
				if (returnedLength > 0)
				{
					ResultInfo = Marshal.PtrToStructure<CldApi.CF_PLACEHOLDER_STANDARD_INFO>(bufferPointerHandler);
				}

				return ResultInfo;
			}
		}
		catch (Exception e)
		{
			Log.Error($"GetPlaceholderInfoStandard FAILED: {e.Message}");
			return default;
		}
	}


	/// <summary>
	///     Retrieves basic placeholder information for a specified file or directory.
	/// </summary>
	/// <param name="fullPath">The full path to the file or directory for which placeholder information is to be retrieved.</param>
	/// <param name="isDirectory">A boolean value indicating whether the specified path refers to a directory (true) or a file (false).</param>
	/// <returns>
	///     A <see cref="CF_PLACEHOLDER_BASIC_INFO" /> structure containing basic information about the placeholder for the specified file or directory.
	///     If the operation fails, the method returns the default value of <see cref="CF_PLACEHOLDER_BASIC_INFO" />.
	/// </returns>
	public static CldApi.CF_PLACEHOLDER_BASIC_INFO GetPlaceholderInfoBasic(HFILE FileHandle)
	{
		var InfoBufferLength = 1024;
		CldApi.CF_PLACEHOLDER_BASIC_INFO ResultInfo = default;

		using (SafeHandlers.SafeAllocCoTaskMem bufferPointerHandler = new(InfoBufferLength))
		{
			if (!FileHandle.IsInvalid)
			{
				_ = CldApi.CfGetPlaceholderInfo(FileHandle, CldApi.CF_PLACEHOLDER_INFO_CLASS.CF_PLACEHOLDER_INFO_BASIC, bufferPointerHandler, (uint)InfoBufferLength, out var returnedLength);
				if (returnedLength > 0)
				{
					ResultInfo = Marshal.PtrToStructure<CldApi.CF_PLACEHOLDER_BASIC_INFO>(bufferPointerHandler);
				}
			}

			return ResultInfo;
		}
	}


	/// <summary>
	///     Retrieves the file identity data stored in a placeholder file using the provided file handle.
	/// </summary>
	/// <param name="FileHandle">A handle to the file for which to retrieve the placeholder file identity.</param>
	/// <returns>
	///     A byte array containing the placeholder file identity data if the retrieval is successful.
	///     Returns null if the file handle is invalid or the retrieval fails.
	/// </returns>
	public static byte[]? GetPlaceholderFileIdentity(HFILE FileHandle)
	{
		if (FileHandle.IsInvalid)
		{
			return null;
		}

		var bufferSize = Marshal.SizeOf(typeof(CldApi.CF_PLACEHOLDER_BASIC_INFO)) + MAX_IDENTIFY_LENGTH;
		using SafeHandlers.SafeAllocCoTaskMem bufferPointerHandler = new(bufferSize);

		var res = CldApi.CfGetPlaceholderInfo(FileHandle, CldApi.CF_PLACEHOLDER_INFO_CLASS.CF_PLACEHOLDER_INFO_BASIC, bufferPointerHandler, (uint)bufferSize, out var infoSize);
		if (res == HRESULT.S_OK && infoSize > 0)
		{
			var info = Marshal.PtrToStructure<CldApi.CF_PLACEHOLDER_BASIC_INFO>(bufferPointerHandler);
			var length = info.FileIdentityLength;

			unsafe
			{
				var ptr = (byte*)((IntPtr)bufferPointerHandler).ToPointer();
				var srcPtr = new IntPtr(ptr + infoSize - length);
				var bytes = new byte[length];
				Marshal.Copy(srcPtr, bytes, 0, (int)length);
				return bytes;
			}
		}

		return null;
	}


	/// <summary>
	///     Retrieves the cloud file identity for a specified file.
	/// </summary>
	/// <param name="filePath">The full path to the file for which to retrieve the cloud file identity.</param>
	/// <returns>
	///     A <see cref="CloudFileIdentity" /> object representing the cloud file identity of the specified file.
	///     Returns null if the cloud file identity could not be determined.
	/// </returns>
	public static CloudFileIdentity? GetCloudFileIdentity(string filePath)
	{
		using var fileHandle = new SafeHandlers.SafeCreateFileForCldApi(filePath);
		var bytes = GetPlaceholderFileIdentity(fileHandle);

		if (bytes != null)
		{
			var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
			var ptr = handle.AddrOfPinnedObject();
			try
			{
				return CloudFileIdentity.Deserialize(ptr);
			}
			catch
			{
				return null;
			}
			finally
			{
				handle.Free();
			}
		}

		return null;
	}


	/// <summary>
	///     Creates placeholder information for a specified file or directory.
	/// </summary>
	/// <param name="placeholder">
	///     The <see cref="Placeholder" /> object containing details of the file or directory for which to create placeholder information.
	/// </param>
	/// <returns>
	///     A tuple where the first element is a <see cref="CldApi.CF_PLACEHOLDER_CREATE_INFO" /> structure representing the placeholder information,
	///     and the second element is a <see cref="StringPtr" /> instance containing the serialized file identity, or null if serialization fails.
	/// </returns>
	public static (CldApi.CF_PLACEHOLDER_CREATE_INFO, StringPtr?) CreatePlaceholderInfo(Placeholder placeholder)
	{
		StringPtr? identity;
		try
		{
			identity = placeholder.FileIdentity.Serialize(MAX_IDENTIFY_LENGTH);
		}
		catch
		{
			identity = null;
		}

		CldApi.CF_PLACEHOLDER_CREATE_INFO cfInfo = new()
		{
			FileIdentity = identity?.Ptr ?? IntPtr.Zero,
			FileIdentityLength = identity?.Size ?? 0,
			RelativeFileName = placeholder.RelativeFileName,
			FsMetadata = new CldApi.CF_FS_METADATA { FileSize = placeholder.FileSize, BasicInfo = CreateFileBasicInfo(placeholder) },
			Flags = CldApi.CF_PLACEHOLDER_CREATE_FLAGS.CF_PLACEHOLDER_CREATE_FLAG_MARK_IN_SYNC
		};

		return (cfInfo, identity);
	}


	/// <summary>
	///     Creates filesystem metadata for a given placeholder object.
	/// </summary>
	/// <param name="placeholder">The <see cref="Placeholder" /> object containing data to generate the filesystem metadata.</param>
	/// <returns>
	///     A <see cref="CldApi.CF_FS_METADATA" /> structure containing the metadata based on the attributes from the provided placeholder.
	/// </returns>
	public static CldApi.CF_FS_METADATA CreateFSMetaData(Placeholder placeholder)
	{
		return new CldApi.CF_FS_METADATA { FileSize = placeholder.FileSize, BasicInfo = CreateFileBasicInfo(placeholder) };
	}


	/// <summary>
	///     Creates a <see cref="Kernel32.FILE_BASIC_INFO" /> structure based on the provided placeholder information.
	/// </summary>
	/// <param name="placeholder">An instance of <see cref="Placeholder" /> containing file metadata used to construct the FILE_BASIC_INFO object.</param>
	/// <returns>
	///     A <see cref="Kernel32.FILE_BASIC_INFO" /> structure populated with the basic file details,
	///     including attributes, creation time, last access time, and last write time.
	/// </returns>
	public static Kernel32.FILE_BASIC_INFO CreateFileBasicInfo(Placeholder placeholder)
	{
		return new Kernel32.FILE_BASIC_INFO
		{
			FileAttributes = (FileFlagsAndAttributes)placeholder.FileAttributes,
			CreationTime = placeholder.CreationTime.ToFileTimeStruct(),
			LastWriteTime = placeholder.LastWriteTime.ToFileTimeStruct(),
			LastAccessTime = placeholder.LastAccessTime.ToFileTimeStruct(),
			ChangeTime = placeholder.LastWriteTime.ToFileTimeStruct()
		};
	}


	/// <summary>
	///     Sets the in-sync state of a placeholder file or directory.
	/// </summary>
	/// <param name="fullPath">The full path to the file or directory whose in-sync state is to be set.</param>
	/// <param name="inSyncState">The desired in-sync state to apply to the specified file or directory.</param>
	/// <param name="isDirectory">Indicates whether the specified path refers to a directory (true) or a file (false).</param>
	/// <returns>
	///     A boolean value indicating whether the in-sync state was successfully set.
	///     Returns true if the operation succeeds; otherwise, false.
	/// </returns>
	public static bool SetInSyncState(string fullPath, CldApi.CF_IN_SYNC_STATE inSyncState, bool isDirectory)
	{
		var d = fullPath.TrimEnd('\\');
		Log.Debug($"SetInSyncState {inSyncState}, {d}");

		using (SafeHandlers.SafeCreateFileForCldApi h = new(fullPath, isDirectory))
		{
			if (h.IsInvalid)
			{
				Log.Warn($"SetInSyncState INVALID Handle! {fullPath.TrimEnd('\\')}");
				return false;
			}

			var result = CldApi.CfSetInSyncState((SafeFileHandle)h, inSyncState, CldApi.CF_SET_IN_SYNC_FLAGS.CF_SET_IN_SYNC_FLAG_NONE);
			return result.Succeeded;
		}
	}


	/// <summary>
	///     Updates the synchronization state of a file or directory to indicate its in-sync status with the cloud.
	/// </summary>
	/// <param name="fileHandle">A <see cref="SafeFileHandle" /> representing the handle to the file or directory for which to update the sync state.</param>
	/// <param name="inSyncState">The desired synchronization state to set, represented by the <see cref="CldApi.CF_IN_SYNC_STATE" /> enumeration.</param>
	/// <returns>
	///     A boolean value indicating whether the synchronization state was successfully updated.
	///     Returns true if the operation was successful; otherwise, false.
	/// </returns>
	public static bool SetInSyncState(SafeFileHandle fileHandle, CldApi.CF_IN_SYNC_STATE inSyncState)
	{
		Log.Debug($"SetInSyncState {inSyncState}, FileHandle: {fileHandle.DangerousGetHandle()}");
		var res = CldApi.CfSetInSyncState(fileHandle, inSyncState, CldApi.CF_SET_IN_SYNC_FLAGS.CF_SET_IN_SYNC_FLAG_NONE);
		return res.Succeeded;
	}

	#endregion


	// --------------------------------------------------------------------------------------------
	// NESTED CLASSES
	// --------------------------------------------------------------------------------------------
	#region NESTED CLASSES

	/// <summary>
	///     Represents a collection of safe handle wrapper classes designed to manage and encapsulate resource
	///     handling, file operations, and memory management in the context of the Cloud File API (CfApi).
	/// </summary>
	/// <remarks>
	///     This class includes nested types that provide safe handle implementations for specific scenarios, such
	///     as creating and managing file handles, memory allocation, and working with transfer keys. These utility
	///     classes ensure proper resource disposal and help avoid memory leaks or unmanaged resource mismanagement,
	///     supporting Cloud File API operations.
	/// </remarks>
	public class SafeHandlers
	{
		/// <summary>
		///     Encapsulates a safe handle for creating a file or directory handle compatible with the Cloud Files API (CfApi).
		///     This class is designed to manage the lifecycle of file handles used in CfApi-related operations, ensuring proper
		///     resource disposal to prevent handle leaks or resource contention. It handles both files and directories as required
		///     by the CfApi functionalities.
		/// </summary>
		/// <remarks>
		///     This class provides functionality to safely create a handle for a file or directory, wrapping the Windows API's
		///     CreateFile operation. It offers implicit conversion to SafeFileHandle and HFILE, making it compatible with APIs
		///     and operations expecting these handle types. The instances of this class are disposable, ensuring handle resources
		///     are properly released.
		/// </remarks>
		[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
		public sealed class SafeCreateFileForCldApi : IDisposable
		{
			#region Properties

			public bool IsInvalid => _handle.IsInvalid;
			
			private const uint GENERIC_READ                 = 0x80000000;
			private const uint FILE_SHARE_READ              = 0x00000001;
			private const uint FILE_SHARE_WRITE             = 0x00000002;
			private const uint FILE_SHARE_DELETE            = 0x00000004;
			private const uint OPEN_EXISTING                = 3;
			private const uint FILE_ATTRIBUTE_NORMAL        = 0x00000080;
			private const uint FILE_FLAG_BACKUP_SEMANTICS   = 0x02000000;
			private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
			private const uint FILE_FLAG_OVERLAPPED         = 0x40000000;
			
			private readonly SafeFileHandle _handle;

			// Fallback P/Invoke for CreateFileW when CsWin32 types are unavailable
			[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateFileW")]
			private static extern SafeFileHandle CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

			private bool disposedValue;

			#endregion
			

			#region Constructors

			/// <summary>
			///     A safe handle wrapper for creating file or directory handles used for Cloud Filter API operations.
			///     Ensures proper resource management and disposal.
			/// </summary>
			/// <param name="fullPath">The full path to the file or directory for which a handle is to be created.</param>
			/// <param name="isDirectory">A boolean value indicating whether the specified path refers to a directory (true) or a file (false).</param>
			public SafeCreateFileForCldApi(string fullPath, bool isDirectory)
			{
				_handle = CreateFileHandle(fullPath, isDirectory);
			}


			/// <summary>
			///     Represents a wrapper for safely creating and managing a file handle for use with the Cloud Filter API.
			/// </summary>
			/// <param name="fullPath">The full path to the file or directory for which the file handle is to be created.</param>
			/// <exception cref="System.IO.FileNotFoundException">Thrown if the specified file or directory does not exist.</exception>
			/// <exception cref="System.UnauthorizedAccessException">Thrown if the application lacks necessary permissions to access the specified file or directory.</exception>
			/// <remarks>
			///     The created handle is used to interact with cloud-backed files or directories, enabling operations like querying
			///     or manipulating placeholder states. This class ensures proper resource cleanup by implementing <see cref="System.IDisposable" />.
			/// </remarks>
			public SafeCreateFileForCldApi(string fullPath)
			{
				bool isDirectory;
				try
				{
					isDirectory = File.GetAttributes(fullPath).HasFlag(FileAttributes.Directory);
				}
				catch
				{
					isDirectory = false;
				}

				_handle = CreateFileHandle(fullPath, isDirectory);
			}

			#endregion


			#region Methods

			/// <summary>
			///     Releases all resources used by the current instance of the class.
			/// </summary>
			/// <remarks>
			///     This method implements the <see cref="IDisposable.Dispose" /> interface to allow for the explicit release
			///     of unmanaged resources or other cleanup operations when the instance is no longer needed.
			/// </remarks>
			public void Dispose()
			{
				Dispose(true);
				GC.SuppressFinalize(this);
			}


			/// <summary>
			///     Creates a handle for accessing a file or directory with specified access and attribute flags.
			/// </summary>
			/// <param name="fullPath">The full path to the file or directory for which to create the handle.</param>
			/// <param name="isDirectory">A boolean value indicating whether the specified path refers to a directory (true) or a file (false).</param>
			/// <returns>
			///     A <see cref="Microsoft.Win32.SafeHandles.SafeFileHandle" /> object representing the handle for the specified file or directory.
			///     If the handle cannot be created, the method returns an invalid SafeFileHandle.
			/// </returns>
			
			private static SafeFileHandle CreateFileHandle(string fullPath, bool isDirectory)
			{
				// Use GENERIC_READ and liberal sharing for CfApi queries
				var access = GENERIC_READ;
				var share = FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE;
				var flags = FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OVERLAPPED;
				
				if (isDirectory)
				{
					flags |= FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT;
				}

				// Prefix with \\?\ to allow long paths
				return CreateFileW(@"\\?\" + fullPath, access, share, IntPtr.Zero, OPEN_EXISTING, flags, IntPtr.Zero);
			}


			/// <summary>
			///     Defines an implicit conversion operator that converts an instance of
			///     <see cref="CloudFilterAPI.SafeHandlers.SafeCreateFileForCldApi" /> to a <see cref="SafeFileHandle" />.
			/// </summary>
			/// <param name="instance">The instance of <see cref="CloudFilterAPI.SafeHandlers.SafeCreateFileForCldApi" /> to convert.</param>
			/// <returns>
			///     A <see cref="SafeFileHandle" /> representing the underlying file handle of the given
			///     <see cref="CloudFilterAPI.SafeHandlers.SafeCreateFileForCldApi" /> instance.
			/// </returns>
			public static implicit operator SafeFileHandle(SafeCreateFileForCldApi instance)
			{
				return instance._handle;
			}


			/// <summary>
			///     Converts an instance of <see cref="SafeCreateFileForCldApi" /> to an <see cref="HFILE" /> handle.
			/// </summary>
			/// <param name="instance">The instance of <see cref="SafeCreateFileForCldApi" /> to be converted.</param>
			/// <returns>An <see cref="HFILE" /> handle representing the underlying file handle of the specified instance.</returns>
			public static implicit operator HFILE(SafeCreateFileForCldApi instance)
			{
				return instance._handle;
			}


			/// <summary>
			///     Releases the resources used by the <see cref="SafeCreateFileForCldApi" /> instance.
			/// </summary>
			/// <param name="disposing">
			///     A boolean value indicating whether the method is being called directly or by a finalizer.
			///     If true, managed and unmanaged resources are disposed; if false, only unmanaged resources are released.
			/// </param>
			private void Dispose(bool disposing)
			{
				if (!disposedValue)
				{
					if (disposing)
					{
						_handle.Dispose();
					}

					disposedValue = true;
				}
			}

			#endregion
		}


		/// <summary>
		///     Represents a wrapper around a file handle opened with opportunistic locks (oplocks) in the Windows CF API.
		///     This class ensures safe handling and disposal of the underlying file handle used for managing file operations
		///     with exclusive access or other oplock-defined behaviors.
		/// </summary>
		/// <remarks>
		///     This class is primarily used for interacting with files that need to be managed using oplocks as part of
		///     placeholder or synchronization operations. It encapsulates low-level handle management and provides
		///     implicit conversions to simplify its usage in methods that require specific handle types.
		/// </remarks>
		public sealed class SafeOpenFileWithOplock : IDisposable
		{
			#region Constructors

			/// <summary>
			///     Provides a safe and encapsulated handle for opening a file with an opportunistic lock (oplock).
			/// </summary>
			/// <param name="fullPath">The full path to the file to be opened with an oplock.</param>
			/// <param name="Flags">A set of flags, specified by <see cref="CldApi.CF_OPEN_FILE_FLAGS" />, that determine how the file is opened.</param>
			/// <remarks>
			///     Safely wraps the file handle created through the <see cref="CldApi.CfOpenFileWithOplock" /> method. The class ensures proper resource management
			///     and disposal to prevent resource leaks or invalid file operations.
			/// </remarks>
			public SafeOpenFileWithOplock(string fullPath, CldApi.CF_OPEN_FILE_FLAGS Flags)
			{
				CldApi.CfOpenFileWithOplock(fullPath, Flags, out _handle);
			}

			#endregion
			#region Properties

			private readonly CldApi.SafeHCFFILE _handle;
			private bool disposedValue;
			public bool IsInvalid => _handle.IsInvalid;

			#endregion


			#region Methods

			/// <summary>
			///     Releases all resources used by the <see cref="CloudFilterAPI.SafeHandlers.SafeOpenFileWithOplock" /> instance.
			/// </summary>
			public void Dispose()
			{
				// Ändern Sie diesen Code nicht. Fügen Sie Bereinigungscode in der Methode "Dispose(bool disposing)" ein.
				Dispose(true);
				GC.SuppressFinalize(this);
			}


			/// <summary>
			///     Defines an implicit conversion operator for the <see cref="SafeOpenFileWithOplock" /> class,
			///     allowing instances of this class to be implicitly converted to a <see cref="CldApi.SafeHCFFILE" /> object.
			/// </summary>
			/// <param name="instance">
			///     The <see cref="SafeOpenFileWithOplock" /> instance to convert.
			/// </param>
			/// <returns>
			///     A <see cref="CldApi.SafeHCFFILE" /> object representing the internal handle of the given <see cref="SafeOpenFileWithOplock" /> instance.
			/// </returns>
			public static implicit operator CldApi.SafeHCFFILE(SafeOpenFileWithOplock instance)
			{
				return instance._handle;
			}


			/// <summary>
			///     Converts an instance of <see cref="CloudFilterAPI.SafeHandlers.SafeOpenFileWithOplock" />
			///     to an <see cref="Vanara.PInvoke.Kernel32.HFILE" /> handle.
			/// </summary>
			/// <param name="instance">
			///     The instance of <see cref="CloudFilterAPI.SafeHandlers.SafeOpenFileWithOplock" />
			///     to be converted.
			/// </param>
			/// <returns>
			///     An <see cref="Vanara.PInvoke.Kernel32.HFILE" /> handle obtained from
			///     the internal handle of the provided instance.
			/// </returns>
			public static implicit operator HFILE(SafeOpenFileWithOplock instance)
			{
				return instance._handle.DangerousGetHandle();
			}


			/// <summary>
			///     Releases the resources used by the <see cref="SafeOpenFileWithOplock" /> instance.
			/// </summary>
			/// <param name="disposing">A boolean value indicating whether to release managed resources (true) or only unmanaged resources (false).</param>
			private void Dispose(bool disposing)
			{
				if (!disposedValue)
				{
					if (disposing)
					{
						_handle.Dispose();
					}

					disposedValue = true;
				}
			}

			#endregion
		}


		/// <summary>
		///     Provides a memory management utility for allocating and freeing memory in the COM Task Memory space (CoTaskMem),
		///     ensuring proper handling of unmanaged resources using .NET's IDisposable pattern.
		/// </summary>
		/// <remarks>
		///     This class facilitates memory allocation in scenarios where unmanaged memory needs to be managed safely
		///     within a .NET environment, especially in interactions with Windows API functions that require or return
		///     memory allocated using CoTaskMem. It supports various constructors to handle allocations for raw memory sizes,
		///     managed structures, and string data. The allocated memory is automatically released when the instance is
		///     disposed, reducing the risk of memory leaks in applications utilizing unmanaged memory.
		/// </remarks>
		public sealed class SafeAllocCoTaskMem : IDisposable
		{
			private readonly IntPtr _pointer;
			public readonly int Size;
			private bool disposedValue;


			/// <summary>
			///     Manages memory allocation for a specified size in unmanaged memory using the CoTaskMem allocator.
			/// </summary>
			/// <param name="size">The size, in bytes, of the memory block to be allocated.</param>
			public SafeAllocCoTaskMem(int size)
			{
				Size = size;
				_pointer = Marshal.AllocCoTaskMem(Size);
			}


			/// <summary>
			///     Provides a safe handle for memory allocated using <see cref="Marshal.AllocCoTaskMem" />.
			/// </summary>
			/// <remarks>
			///     Ensures proper cleanup of unmanaged memory allocated for a given structure, string, or size.
			/// </remarks>
			/// <example>
			///     This class can be used to safely allocate and manage unmanaged memory, ensuring disposal when no longer needed.
			/// </example>
			/// <seealso cref="Marshal.AllocCoTaskMem" />
			/// <seealso cref="Marshal.StructureToPtr" />
			public SafeAllocCoTaskMem(object structure)
			{
				Size = Marshal.SizeOf(structure);
				_pointer = Marshal.AllocCoTaskMem(Size);
				Marshal.StructureToPtr(structure, _pointer, false);
			}


			/// <summary>
			///     Represents a managed wrapper for an unmanaged memory buffer allocated with CoTaskMem.
			///     Automatically frees the allocated memory when disposed.
			/// </summary>
			/// <remarks>
			///     This class is used to allocate memory using CoTaskMem functions
			///     and manage its lifecycle within a safe context, releasing unmanaged resources when appropriate.
			/// </remarks>
			public SafeAllocCoTaskMem(string data)
			{
				Size = data.Length * Marshal.SystemDefaultCharSize;
				_pointer = Marshal.StringToCoTaskMemUni(data);
			}


			/// <summary>
			///     Releases all resources used by the <see cref="CloudFilterAPI.SafeHandlers.SafeAllocCoTaskMem" /> instance.
			/// </summary>
			public void Dispose()
			{
				// Ändern Sie diesen Code nicht. Fügen Sie Bereinigungscode in der Methode "Dispose(bool disposing)" ein.
				Dispose(true);
				GC.SuppressFinalize(this);
			}


			/// <summary>
			///     Converts a <see cref="SafeAllocCoTaskMem" /> instance to an <see cref="IntPtr" />.
			/// </summary>
			/// <param name="instance">
			///     The <see cref="SafeAllocCoTaskMem" /> instance to be converted.
			/// </param>
			/// <returns>
			///     An <see cref="IntPtr" /> that points to the memory allocated by the <see cref="SafeAllocCoTaskMem" /> instance.
			/// </returns>
			public static implicit operator IntPtr(SafeAllocCoTaskMem instance)
			{
				return instance._pointer;
			}


			/// <summary>
			///     Releases all resources used by the current instance of the <see cref="SafeAllocCoTaskMem" /> class.
			/// </summary>
			/// <param name="disposing">
			///     A boolean value indicating whether the method is called explicitly (true) to release both managed and unmanaged resources, or by the finalizer (false) to release only unmanaged resources.
			/// </param>
			private void Dispose(bool disposing)
			{
				if (!disposedValue)
				{
					if (disposing)
					{
					}

					Marshal.FreeCoTaskMem(_pointer);
					disposedValue = true;
				}
			}


			/// <summary>
			///     Represents a memory allocation using CoTaskMem, which is used to allocate and manage unmanaged memory.
			/// </summary>
			/// <remarks>
			///     This class provides mechanisms to allocate, manage, and release unmanaged memory safely using CoTaskMem.
			///     It implements the <see cref="IDisposable" /> interface to ensure proper cleanup of allocated resources.
			/// </remarks>
			~SafeAllocCoTaskMem()
			{
				// Ändern Sie diesen Code nicht. Fügen Sie Bereinigungscode in der Methode "Dispose(bool disposing)" ein.
				Dispose(false);
			}
		}


		/// <summary>
		///     Represents a safe handle wrapper used for managing transfer keys in the Windows Cloud Files API (CfApi).
		///     This class ensures proper handling and disposal of file handles associated with a transfer key.
		/// </summary>
		/// <remarks>
		///     The <c>SafeTransferKey</c> class encapsulates the functionality for interacting with transfer keys
		///     retrieved from the Windows Cloud Files API. It provides constructors for initializing the transfer key
		///     using either a raw handle or a <c>SafeFileHandle</c>. Implicit conversion to the
		///     <c>CF_TRANSFER_KEY</c> structure is also supported for seamless integration with other API functionalities.
		/// </remarks>
		public class SafeTransferKey : IDisposable
		{
			private readonly HFILE handle;
			private readonly CldApi.CF_TRANSFER_KEY TransferKey;


			private bool disposedValue;


			/// <summary>
			///     Represents a wrapper for a transfer key retrieved from a file handle
			///     for use with the Cloud Filter API, ensuring safe handling and disposal of resources.
			/// </summary>
			/// <param name="handle">The file handle from which to retrieve the transfer key.</param>
			/// <exception cref="System.Exception">
			///     Throws an exception if the transfer key cannot be retrieved successfully.
			/// </exception>
			public SafeTransferKey(HFILE handle)
			{
				this.handle = handle;
				CldApi.CfGetTransferKey(this.handle, out TransferKey).ThrowIfFailed();
			}


			/// <summary>
			///     Represents a secure wrapper for a transfer key used in Cloud Filter API operations, ensuring proper resource management and disposal.
			/// </summary>
			public SafeTransferKey(SafeFileHandle safeHandle)
			{
				handle = safeHandle;
				CldApi.CfGetTransferKey(handle, out TransferKey).ThrowIfFailed();
			}


			/// <summary>
			///     Releases all resources used by the current instance of the <see cref="SafeTransferKey" /> class.
			/// </summary>
			/// <remarks>
			///     This method implements the <see cref="IDisposable.Dispose" /> interface and is intended to be called when the
			///     instance is no longer needed. It ensures that unmanaged resources are properly released and suppresses
			///     finalization by the garbage collector.
			/// </remarks>
			public void Dispose()
			{
				// Ändern Sie diesen Code nicht. Fügen Sie Bereinigungscode in der Methode "Dispose(bool disposing)" ein.
				Dispose(true);
				GC.SuppressFinalize(this);
			}


			/// <summary>
			///     Defines an implicit conversion operator to convert an instance of <see cref="SafeTransferKey" />
			///     to a <see cref="CldApi.CF_TRANSFER_KEY" />.
			/// </summary>
			/// <param name="instance">The instance of <see cref="SafeTransferKey" /> to be converted.</param>
			/// <returns>
			///     A <see cref="CldApi.CF_TRANSFER_KEY" /> representation of the specified <see cref="SafeTransferKey" /> instance.
			/// </returns>
			public static implicit operator CldApi.CF_TRANSFER_KEY(SafeTransferKey instance)
			{
				return instance.TransferKey;
			}


			/// <summary>
			///     Releases the resources used by the <see cref="SafeHandlers.SafeTransferKey" /> object.
			/// </summary>
			/// <param name="disposing">
			///     A boolean value indicating whether the method is called from managed code (true) to release both managed and unmanaged resources,
			///     or from a finalizer (false) to release only unmanaged resources.
			/// </param>
			protected virtual void Dispose(bool disposing)
			{
				if (!disposedValue)
				{
					if (disposing)
					{
					}

					if (!handle.IsInvalid)
					{
						CldApi.CfReleaseTransferKey(handle, TransferKey);
					}

					disposedValue = true;
				}
			}


			/// <summary>
			///     Represents a safe wrapper for transferring file keys in the Cloud Filter API.
			///     Provides mechanisms for safely managing and disposing resources associated with file transfer keys.
			/// </summary>
			/// <remarks>
			///     This class is primarily used to encapsulate and manage resources related to file transfer keys in a safe manner,
			///     ensuring proper resource cleanup and disposal.
			/// </remarks>
			~SafeTransferKey()
			{
				// Ändern Sie diesen Code nicht. Fügen Sie Bereinigungscode in der Methode "Dispose(bool disposing)" ein.
				Dispose(false);
			}
		}


		/// <summary>
		///     Represents a safe wrapper for an array of <c>CF_PLACEHOLDER_CREATE_INFO</c> structures, ensuring proper
		///     resource management and disposal of associated native resources.
		/// </summary>
		/// <remarks>
		///     This class extends <c>SafeNativeArray</c> and overrides the disposal mechanism to handle cleanup
		///     of each <c>CF_PLACEHOLDER_CREATE_INFO</c> element within the array. This includes disposing of the
		///     <c>FileIdentity</c> pointer in each element, preventing memory leaks. Intended for managing placeholder
		///     create information in virtualized file systems.
		/// </remarks>
		public class SafePlaceHolderList : SafeNativeArray<CldApi.CF_PLACEHOLDER_CREATE_INFO>
		{
			protected override void Dispose(bool disposing)
			{
				if (Elements != null)
				{
					foreach (var item in Elements)
					{
						new StringPtr(item.FileIdentity).Dispose();
					}
				}

				base.Dispose(disposing);
			}
		}
	}


	/// <summary>
	///     Represents a collection of disposable objects where each element implements the <see cref="IDisposable" />
	///     interface. This class ensures that all contained objects are disposed of when the collection itself is disposed.
	/// </summary>
	/// <remarks>
	///     The <c>AutoDisposeList&lt;T&gt;</c> class extends the generic <see cref="List{T}" /> class and provides
	///     automatic resource management of its elements. It is particularly useful in scenarios where a group
	///     of disposable objects needs to be collectively managed and disposed of to prevent resource leaks.
	/// </remarks>
	/// <typeparam name="T">
	///     The type of objects in the list. The type must implement the <see cref="IDisposable" /> interface
	///     to support proper disposal of its elements.
	/// </typeparam>
	public class AutoDisposeList<T> : List<T>, IDisposable where T : IDisposable
	{
		/// <summary>
		///     Releases all resources used by the instances of type <typeparamref name="T" /> contained in the list,
		///     and clears the list. Invokes the <see cref="IDisposable.Dispose" /> method on each element in the collection.
		/// </summary>
		/// <remarks>
		///     This method should be called when the <see cref="AutoDisposeList{T}" /> is no longer needed to ensure
		///     that resources used by the contained objects are properly released.
		/// </remarks>
		public void Dispose()
		{
			foreach (var obj in this)
			{
				obj.Dispose();
			}
		}
	}


	//public class CallbackSuspensionHelper : IDisposable
	//{
	//    private ConcurrentDictionary<string, int> suspendFileCallbackList = new();
	//    private bool disposedValue;

	//    public class SuspendedValue : IDisposable
	//    {
	//        private bool disposedValue;
	//        private readonly string value;
	//        private readonly ConcurrentDictionary<string, int> suspendFileCallbackList;

	//        public SuspendedValue(string value, ConcurrentDictionary<string, int> suspendFileCallbackList)
	//        {
	//            this.value = value;
	//            this.suspendFileCallbackList = suspendFileCallbackList;
	//        }

	//        protected virtual void Dispose(bool disposing)
	//        {
	//            if (!disposedValue)
	//            {
	//                if (disposing)
	//                {
	//                    int? ret = suspendFileCallbackList?.AddOrUpdate(value, 1, (k, v) => v--);

	//                    // TODO: Remove item from dictionary if <= 0
	//                    // Currently unknown how to do it thread safe

	//                    //if (ret <= 0)
	//                    //{
	//                    //   if ( suspendFileCallbackList.TryRemove(value, out ret))
	//                    //    {
	//                    //        if (ret >0)
	//                    //        {
	//                    //            suspendFileCallbackList.AddOrUpdate(value, ret, (k, v) => v++);
	//                    //        }
	//                    //    }
	//                    //}
	//                }

	//                disposedValue = true;
	//            }
	//        }
	//        public void Dispose()
	//        {
	//            // Ändern Sie diesen Code nicht. Fügen Sie Bereinigungscode in der Methode "Dispose(bool disposing)" ein.
	//            Dispose(disposing: true);
	//            GC.SuppressFinalize(this);
	//        }
	//    }

	//    public bool IsSuspended(string value)
	//    {
	//        if (!suspendFileCallbackList.TryGetValue(value, out int result))
	//        {
	//            return false;
	//        }

	//        return result > 0;
	//    }
	//    public SuspendedValue SetSuspension(string value)
	//    {
	//        return new SuspendedValue(value, suspendFileCallbackList);
	//    }

	//    protected virtual void Dispose(bool disposing)
	//    {
	//        if (!disposedValue)
	//        {
	//            if (disposing)
	//            {
	//                suspendFileCallbackList?.Clear();
	//                suspendFileCallbackList = null;
	//            }

	//            disposedValue = true;
	//        }
	//    }
	//    public void Dispose()
	//    {
	//        // Ändern Sie diesen Code nicht. Fügen Sie Bereinigungscode in der Methode "Dispose(bool disposing)" ein.
	//        Dispose(disposing: true);
	//        GC.SuppressFinalize(this);
	//    }
	//}

	#endregion
}
