//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SystemSpinnerX64.Diagnostics;

namespace SystemSpinnerX64.Devices;

// DDC/CI — the very channel the macOS version drives an external monitor through.
internal static class MonitorControl
{
    // Code for the volume of the built-in speakers.
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

    // Handles of every attached monitor, in the order the system gives them.
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

    // What the system says about a monitor: the name — "Dell U2720Q" — the name the rest of
    // Windows knows it by, and whether its connection can carry sound. All from one enumeration.
    public static (string Name, string GdiName, bool CarriesAudio) Describe(IntPtr monitor)
    {
        var info = new MonitorInfoEx { cbSize = Marshal.SizeOf<MonitorInfoEx>() };
        if (!GetMonitorInfoW(monitor, ref info)) return ("Display", string.Empty, false);

        // The display configuration knows the screen by the name shown in the Windows display
        // settings — "XV320QU LV". It is asked first: the older enumeration below answers for most
        // screens with the driver name, and that is "Generic PnP Monitor" for nearly all of them.
        if (DisplayConfig.ByGdiDevice().TryGetValue(info.DeviceName, out DisplayConfig.Screen screen))
            return (screen.Name, info.DeviceName, screen.CarriesAudio);

        var device = new DisplayDeviceInfo { cb = Marshal.SizeOf<DisplayDeviceInfo>() };
        if (EnumDisplayDevicesW(info.DeviceName, 0, ref device, 0) &&
            device.DeviceString is { Length: > 0 })
        {
            return (device.DeviceString, info.DeviceName, false);
        }

        return (info.DeviceName, info.DeviceName, false);
    }

    // Whether the handle belongs to a built-in laptop panel.
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

    // Opens the physical monitors behind a handle.
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

    // Brightness as the monitor itself calls it: VCP 0x10, Luminance.
    public const byte VcpLuminance = 0x10;

    // Monitor brightness in percent, or null when it does not answer the command.
    public static double? Brightness(IntPtr physical)
    {
        try
        {
            bool answered = GetMonitorBrightness(physical, out uint min, out uint current, out uint max);
            string why = answered ? "it reported no range" : LastError();

            if (answered && max > min) return (current - min) * 100.0 / (max - min);

            // GetMonitorBrightness is the polite way round and plenty of monitors refuse it: it
            // asks for the capabilities string first and gives up when that is malformed, missing
            // or too slow to arrive. The same panel usually answers command 0x10 without a murmur,
            // so the brightness is asked for a second time, directly.
            Log.Info($"the high-level brightness call failed ({why}), asking VCP 10 instead");
            return Feature(physical, VcpLuminance);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"the monitor brightness was not read: {ex.Message}");
            return null;
        }
    }

    // Sets the brightness in percent.
    public static bool SetBrightness(IntPtr physical, double percent)
    {
        try
        {
            if (GetMonitorBrightness(physical, out uint min, out uint _, out uint max) && max > min)
            {
                uint value = (uint)Math.Round(min + (max - min) * Math.Clamp(percent, 0, 100) / 100.0);
                return SetMonitorBrightness(physical, value);
            }

            // The same way round as reading it: what would not answer the high-level call is
            // driven by the command itself.
            return SetFeature(physical, VcpLuminance, percent);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"the monitor brightness was not set: {ex.Message}");
            return false;
        }
    }

    // What Windows blamed the last failed dxva2 call on — worth a line, since a monitor that
    // drives nothing is the thing people ask about.
    private static string LastError() =>
        new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message.Trim();

    // Value of an arbitrary command as a percentage of its maximum, or null.
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

    // Sets an arbitrary command by percentage of its maximum.
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
}
