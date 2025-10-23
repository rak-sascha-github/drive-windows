using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;


namespace RakutenDrive.Utils;

/// <summary>
///     Provides functionality to persist and restore the dimensions and state of a <see cref="Window" />
///     across application sessions.
/// </summary>
[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
public static class WindowBoundsPersistence
{
	// --------------------------------------------------------------------------------------------
	// PUBLIC METHODS
	// --------------------------------------------------------------------------------------------
	#region PUBLIC METHODS
	
	/// <summary>
	///     Saves the current position, size, and state of the specified <see cref="Window" />
	///     to persistent storage identified by a unique identifier.
	/// </summary>
	/// <param name="window">
	///     The <see cref="Window" /> whose dimensions and state are to be saved.
	/// </param>
	/// <param name="id">
	///     A unique identifier for the window instance, used for saving and retrieving its state.
	/// </param>
	public static void Save(Window window, string id)
	{
		try
		{
			// Use RestoreBounds (in DIPs) - robust for WPF + DPI
			var r = window.RestoreBounds;

			var data = new Saved
			{
				Left = r.Left,
				Top = r.Top,
				Width = Math.Max(r.Width, 100), // guard rails
				Height = Math.Max(r.Height, 80),
				IsMaximized = window.WindowState == WindowState.Maximized
			};
			Storage.Write(id, JsonSerializer.Serialize(data));
			Log.Debug($"Stored window bounds: {r.ToString()}");
		}
		catch
		{
			/* best effort */
		}
	}


	/// <summary>
	///     Restores the position, size, and state of the specified <see cref="Window" />
	///     from persistent storage identified by a unique identifier.
	/// </summary>
	/// <param name="window">
	///     The <see cref="Window" /> whose dimensions and state are to be restored.
	/// </param>
	/// <param name="id">
	///     A unique identifier for the window instance, used for retrieving its state.
	/// </param>
	public static void Restore(Window window, string id)
	{
		try
		{
			var json = Storage.Read(id);
			if (string.IsNullOrWhiteSpace(json))
			{
				return;
			}

			var data = JsonSerializer.Deserialize<Saved>(json);
			if (data is null)
			{
				return;
			}

			// Make sure WPF won't shrink us right after we set bounds.
			window.SizeToContent = SizeToContent.Manual;
			window.WindowStartupLocation = WindowStartupLocation.Manual;

			// Start from saved DIPs
			var desired = new Rect(data.Left, data.Top, data.Width, data.Height);

			// Clamp to a visible work area (handles unplugged/rearranged monitors)
			var visible = EnsureVisibleOnSomeMonitor(desired);

			window.Left = visible.Left;
			window.Top = visible.Top;
			window.Width = visible.Width;
			window.Height = visible.Height;

			// Restore maximized after we've applied size/pos
			if (data.IsMaximized)
			{
				// Do it after layout is ready so WPF doesn't fight us
				window.Dispatcher.BeginInvoke(new Action(() => { window.WindowState = WindowState.Maximized; }), DispatcherPriority.ApplicationIdle);
			}
		}
		catch
		{
			/* best effort */
		}
	}

	#endregion

	// --------------------------------------------------------------------------------------------
	// HELPERS
	// --------------------------------------------------------------------------------------------
	#region HELPERS

	/// <summary>
	///     Ensures that the specified rectangular region is entirely visible on at least one monitor.
	///     If the region is partially or fully outside all available monitors, it is adjusted to fit
	///     within the visible area of the closest monitor while respecting minimum size constraints.
	/// </summary>
	/// <param name="dipRect">
	///     The rectangular region, specified in device-independent pixels (DIPs), to be validated and adjusted.
	/// </param>
	/// <returns>
	///     A <see cref="Rect" /> object representing the adjusted rectangular area, which is entirely visible
	///     within at least one monitor's work area.
	/// </returns>
	private static Rect EnsureVisibleOnSomeMonitor(Rect dipRect)
	{
		Rect? union = null;

		MonitorEnumProc cb = (IntPtr hMon, IntPtr hdc, ref RECT lprc, IntPtr data) =>
		{
			var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
			if (!GetMonitorInfo(hMon, ref mi))
			{
				return true; // continue enumeration
			}

			// Per-monitor DPI - fallback to 96 if API not available or returns error
			uint dpiX = 96, dpiY = 96;
			var hr = GetDpiForMonitor(hMon, MDT_EFFECTIVE_DPI, out dpiX, out dpiY);
			if (hr != 0 || dpiX == 0 || dpiY == 0)
			{
				dpiX = dpiY = 96;
			}

			var work = mi.rcWork;
			var dipWork = new Rect(work.Left * 96.0 / dpiX, work.Top * 96.0 / dpiY, (work.Right - work.Left) * 96.0 / dpiX, (work.Bottom - work.Top) * 96.0 / dpiY);

			union = union.HasValue ? Rect.Union(union.Value, dipWork) : dipWork;
			return true; // keep enumerating
		};

		EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero);

		if (union is null)
		{
			// Fallback to primary work area in DIPs
			var wa = SystemParameters.WorkArea;
			union = new Rect(wa.Left, wa.Top, wa.Width, wa.Height);
		}

		var area = union.Value;

		double minW = 200, minH = 120;
		var w = Math.Max(minW, Math.Min(dipRect.Width, area.Width));
		var h = Math.Max(minH, Math.Min(dipRect.Height, area.Height));

		var left = Clamp(dipRect.Left, area.Left, area.Right - w);
		var top = Clamp(dipRect.Top, area.Top, area.Bottom - h);

		return new Rect(left, top, w, h);
	}


	private static double Clamp(double v, double min, double max)
	{
		return v < min ? min : v > max ? max : v;
	}
	
	#endregion

	// --------------------------------------------------------------------------------------------
	// Storage (HKCU\Software\<Company>\<Product>\WindowBounds\id)
	// --------------------------------------------------------------------------------------------

	private static class Storage
	{
		private static readonly RegistryKey Root = Registry.CurrentUser;
		private static string Company => Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? "Rakuten";
		private static string Product => Assembly.GetEntryAssembly()?.GetName().Name ?? "RakutenDrive";


		public static void Write(string id, string json)
		{
			using var key = Root.CreateSubKey($@"Software\{Company}\{Product}\WindowBounds");
			key?.SetValue(id, json, RegistryValueKind.String);
		}


		public static string? Read(string id)
		{
			using var key = Root.OpenSubKey($@"Software\{Company}\{Product}\WindowBounds");
			return key?.GetValue(id) as string;
		}
	}


	// --------------------------------------------------------------------------------------------
	// DTO
	// --------------------------------------------------------------------------------------------
	
	private sealed class Saved
	{
		public double Left { get; set; }
		public double Top { get; set; }
		public double Width { get; set; }
		public double Height { get; set; }
		public bool IsMaximized { get; set; }
	}


	// --------------------------------------------------------------------------------------------
	// Win32 (monitor + DPI)
	// --------------------------------------------------------------------------------------------

	private const int MDT_EFFECTIVE_DPI = 0;
	private delegate bool MonitorEnumProc(IntPtr hMon, IntPtr hdc, ref RECT lprc, IntPtr data);

	[DllImport("user32.dll")]
	private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

	[DllImport("shcore.dll")]
	private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

	[StructLayout(LayoutKind.Sequential)]
	private struct RECT
	{
		public int Left, Top, Right, Bottom;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct MONITORINFOEX
	{
		public int cbSize;
		public RECT rcMonitor;
		public RECT rcWork;
		public uint dwFlags;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
		public string szDevice;
	}
}
