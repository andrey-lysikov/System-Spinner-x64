using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using SystemSpinnerX64.Diagnostics;

namespace SystemSpinnerX64.Devices;

/// <summary>
/// DDC/CI — the very channel the macOS version drives an external monitor through. The commands
/// travel the same wires as the picture: HDMI, DisplayPort, DVI and USB-C. The monitor decides
/// what it can do: almost all support brightness, only those that have speakers support their
/// volume.
///
/// In Windows this is <c>dxva2.dll</c>: the monitor handle gives out "physical monitors" (one
/// handle can have several — when cloning, for instance), and each of them is sent the command.
/// The handles have to be released: while they are open the DDC channel is held.
/// </summary>
internal static class MonitorControl
{
    /// <summary>Brightness code in the monitor command set. The same as <c>luminance</c> on macOS.</summary>
    public const byte VcpBrightness = 0x10;

    /// <summary>Code for the volume of the built-in speakers.</summary>
    public const byte VcpSpeakerVolume = 0x62;

    private const int MonitorNameLength = 128;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PhysicalMonitor
    {
        public IntPtr Handle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MonitorNameLength)]
        public string Description;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, ref Rect rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count,
        [Out] PhysicalMonitor[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool DestroyPhysicalMonitors(uint count, [In] PhysicalMonitor[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetMonitorBrightness(IntPtr monitor, out uint minimum, out uint current, out uint maximum);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool SetMonitorBrightness(IntPtr monitor, uint value);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr monitor, byte code,
        out int type, out uint current, out uint maximum);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool SetVCPFeature(IntPtr monitor, byte code, uint value);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int cbSize;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfoEx info);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDeviceInfo
    {
        public int cb;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public uint StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevicesW(string? device, uint index,
        ref DisplayDeviceInfo info, uint flags);

    /// <summary>Handles of every attached monitor, in the order the system gives them.</summary>
    public static List<IntPtr> Handles()
    {
        var handles = new List<IntPtr>();

        // The list is collected through a closure rather than the data parameter: passing a managed
        // object into an unmanaged call would need pinning, and there is nothing to gain.
        bool Collect(IntPtr monitor, IntPtr dc, ref Rect rect, IntPtr data)
        {
            handles.Add(monitor);
            return true;
        }

        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Collect, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Log.Error("the monitors were not enumerated", ex);
        }

        return handles;
    }

    /// <summary>
    /// The monitor name as the system gives it: "Dell U2720Q". Many monitors report nothing but
    /// "Generic PnP Monitor" — then the name of the video output is returned.
    /// </summary>
    public static string DescribeMonitor(IntPtr monitor)
    {
        var info = new MonitorInfoEx { cbSize = Marshal.SizeOf<MonitorInfoEx>() };
        if (!GetMonitorInfoW(monitor, ref info)) return "Display";

        var device = new DisplayDeviceInfo { cb = Marshal.SizeOf<DisplayDeviceInfo>() };
        if (EnumDisplayDevicesW(info.DeviceName, 0, ref device, 0) &&
            device.DeviceString is { Length: > 0 })
        {
            return device.DeviceString;
        }

        return info.DeviceName;
    }

    /// <summary>Whether the handle belongs to a built-in laptop panel.</summary>
    public static bool IsInternal(IntPtr monitor)
    {
        var info = new MonitorInfoEx { cbSize = Marshal.SizeOf<MonitorInfoEx>() };
        if (!GetMonitorInfoW(monitor, ref info)) return false;

        var device = new DisplayDeviceInfo { cb = Marshal.SizeOf<DisplayDeviceInfo>() };

        // EDD_GET_DEVICE_INTERFACE_NAME: DeviceID then holds the device path, and on built-in
        // panels it always contains INTERNAL or LCD — that is how they are told from external ones.
        if (!EnumDisplayDevicesW(info.DeviceName, 0, ref device, 0x00000001)) return false;

        return device.DeviceID.Contains("INTERNAL", StringComparison.OrdinalIgnoreCase) ||
               device.DeviceID.Contains("LCD", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Opens the physical monitors behind a handle. They must be closed through
    /// <see cref="Close"/>: while they are open the DDC channel is busy.
    /// </summary>
    public static PhysicalMonitor[] Open(IntPtr monitor)
    {
        try
        {
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out uint count) || count == 0)
                return Array.Empty<PhysicalMonitor>();

            var monitors = new PhysicalMonitor[count];
            return GetPhysicalMonitorsFromHMONITOR(monitor, count, monitors)
                ? monitors
                : Array.Empty<PhysicalMonitor>();
        }
        catch (Exception ex)
        {
            // dxva2 ships with every Windows, but over a remote desktop the call can fail.
            Log.Error("the physical monitors were not opened", ex);
            return Array.Empty<PhysicalMonitor>();
        }
    }

    public static void Close(PhysicalMonitor[] monitors)
    {
        if (monitors.Length == 0) return;

        try { DestroyPhysicalMonitors((uint)monitors.Length, monitors); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"DDC handles were not closed: {ex.Message}"); }
    }

    /// <summary>Monitor brightness in percent, or null when it does not answer the command.</summary>
    public static double? Brightness(IntPtr physical)
    {
        try
        {
            if (!GetMonitorBrightness(physical, out uint min, out uint current, out uint max)) return null;
            if (max <= min) return null;

            return (current - min) * 100.0 / (max - min);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"the monitor brightness was not read: {ex.Message}");
            return null;
        }
    }

    /// <summary>Sets the brightness in percent. false means the monitor has no DDC/CI.</summary>
    public static bool SetBrightness(IntPtr physical, double percent)
    {
        try
        {
            if (!GetMonitorBrightness(physical, out uint min, out uint _, out uint max)) return false;
            if (max <= min) return false;

            uint value = (uint)Math.Round(min + (max - min) * Math.Clamp(percent, 0, 100) / 100.0);
            return SetMonitorBrightness(physical, value);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"the monitor brightness was not set: {ex.Message}");
            return false;
        }
    }

    /// <summary>Value of an arbitrary command as a percentage of its maximum, or null.</summary>
    public static double? Feature(IntPtr physical, byte code)
    {
        try
        {
            if (!GetVCPFeatureAndVCPFeatureReply(physical, code, out int _, out uint current, out uint max))
                return null;

            return max == 0 ? null : current * 100.0 / max;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"VCP {code:X2} was not read: {ex.Message}");
            return null;
        }
    }

    /// <summary>Sets an arbitrary command by percentage of its maximum.</summary>
    public static bool SetFeature(IntPtr physical, byte code, double percent)
    {
        try
        {
            if (!GetVCPFeatureAndVCPFeatureReply(physical, code, out int _, out uint _, out uint max))
                return false;
            if (max == 0) return false;

            return SetVCPFeature(physical, code, (uint)Math.Round(max * Math.Clamp(percent, 0, 100) / 100.0));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"VCP {code:X2} was not set: {ex.Message}");
            return false;
        }
    }

    /// <summary>Line for the log: what the monitor can actually do.</summary>
    public static string Describe(IntPtr physical)
    {
        var text = new StringBuilder();
        text.Append("brightness ").Append(Brightness(physical) is double b ? $"{b:0} %" : "no");
        text.Append(", volume ").Append(Feature(physical, VcpSpeakerVolume) is double v ? $"{v:0} %" : "no");
        return text.ToString();
    }
}
