using System;
using System.Runtime.InteropServices;


namespace RakutenDrive.Utils;

/// <summary>
///     Represents a managed wrapper around a native string pointer, providing controlled access
///     to an unmanaged string resource and ensuring proper memory management through the use of
///     the IDisposable pattern.
/// </summary>
public class StringPtr : IDisposable
{
	// --------------------------------------------------------------------------------------------
	// INIT
	// --------------------------------------------------------------------------------------------


	/// <summary>
	///     Represents a managed wrapper around a native string pointer, providing controlled access
	///     to an unmanaged string resource and ensuring proper memory management through the
	///     implementation of the IDisposable pattern.
	/// </summary>
	public StringPtr(string data)
	{
		Ptr = Marshal.StringToHGlobalUni(data);
		Size = (uint)((data.Length + 1) * 2);
	}


	/// <summary>
	///     Provides a managed wrapper for a native string pointer, enabling controlled access to
	///     unmanaged string resources, automatic size detection, and proper memory management through
	///     the implementation of the IDisposable pattern.
	/// </summary>
	public StringPtr(IntPtr ptr)
	{
		if (ptr == IntPtr.Zero)
		{
			Ptr = IntPtr.Zero;
			Size = 0;
		}
		else
		{
			try
			{
				Ptr = ptr;
				var data = Marshal.PtrToStringUni(Ptr) ?? throw new InvalidOperationException();
				Size = (uint)((data.Length + 1) * 2);
			}
			catch
			{
				Ptr = IntPtr.Zero;
				Size = 0;
			}
		}
	}
	// --------------------------------------------------------------------------------------------
	// PROPERTIES
	// --------------------------------------------------------------------------------------------


	public IntPtr Ptr { get; private set; }
	public uint Size { get; private set; }
	public bool IsValid => Ptr != IntPtr.Zero;

	/// <summary>
	///     Gets the string data pointed to by the native pointer associated with this instance.
	///     This property retrieves the managed string representation of the unmanaged string
	///     resource wrapped by the current object.
	///     If the pointer is invalid, an <see cref="InvalidOperationException" /> is thrown.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	///     Thrown if the pointer is invalid or if the unmanaged string cannot be converted to a managed string.
	/// </exception>
	public string Data
	{
		get
		{
			if (!IsValid)
			{
				throw new InvalidOperationException("StringPtr is not valid.");
			}

			var data = Marshal.PtrToStringUni(Ptr) ?? throw new InvalidOperationException();
			return data;
		}
	}


	// --------------------------------------------------------------------------------------------
	// PUBLIC METHODS
	// --------------------------------------------------------------------------------------------


	/// <summary>
	///     Releases all resources used by the instance of the class, including unmanaged resources,
	///     and resets the internal state to ensure the object is disposed of properly.
	/// </summary>
	public void Dispose()
	{
		if (Ptr != IntPtr.Zero)
		{
			Marshal.FreeHGlobal(Ptr);
			Ptr = IntPtr.Zero;
			Size = 0;
		}
	}
}
