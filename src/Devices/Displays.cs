//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using SystemSpinnerX64.Configuration;
using SystemSpinnerX64.Diagnostics;
using SystemSpinnerX64.Platform;

namespace SystemSpinnerX64.Devices;

// The Windows display configuration: the paths from a graphics adapter to a screen. This is where
// the names shown in the display settings live — "XV320QU LV" rather than "Generic PnP Monitor".
internal static class DisplayConfig
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DeviceInfoHeader
    {
        public uint Type;
        public uint Size;
        public Luid AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PathSourceInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PathTargetInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public uint RefreshNumerator;
        public uint RefreshDenominator;
        public uint ScanLineOrdering;
        public int TargetAvailable;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PathInfo
    {
        public PathSourceInfo SourceInfo;
        public PathTargetInfo TargetInfo;
        public uint Flags;
    }

    // Only asked for because QueryDisplayConfig refuses to answer without a buffer for them.
    [StructLayout(LayoutKind.Sequential, Size = 64)]
    private struct ModeInfo
    {
        public uint InfoType;
        public uint Id;
        public Luid AdapterId;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TargetDeviceName
    {
        public DeviceInfoHeader Header;
        public uint Flags;
        public uint OutputTechnology;
        public ushort EdidManufactureId;
        public ushort EdidProductCodeId;
        public uint ConnectorInstance;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string MonitorFriendlyDeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string MonitorDevicePath;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SourceDeviceName
    {
        public DeviceInfoHeader Header;

        // The name the rest of Windows knows the screen by: "\\.\DISPLAY1".
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ViewGdiDeviceName;
    }

    private const uint OnlyActivePaths = 0x00000002;

    private const uint GetSourceName = 1;
    private const uint GetTargetName = 2;

    internal const int Success = 0;

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] PathInfo[] paths,
        ref uint modeCount, [Out] ModeInfo[] modes, IntPtr currentTopology);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref TargetDeviceName request);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref SourceDeviceName request);

    // The paths in use — one per screen showing a picture.
    public static PathInfo[] ActivePaths()
    {
        try
        {
            if (GetDisplayConfigBufferSizes(OnlyActivePaths, out uint pathCount, out uint modeCount) != Success)
                return Array.Empty<PathInfo>();

            var paths = new PathInfo[pathCount];
            var modes = new ModeInfo[modeCount];

            if (QueryDisplayConfig(OnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != Success)
                return Array.Empty<PathInfo>();

            // The counts come back as what was actually filled, and that can be fewer than was asked for.
            Array.Resize(ref paths, (int)pathCount);
            return paths;
        }
        catch (Exception ex)
        {
            Log.Error("the display configuration was not read", ex);
            return Array.Empty<PathInfo>();
        }
    }

    // The screen name as Windows shows it in the display settings: "XV320QU LV". Empty when the
    // screen does not give one — a virtual adapter or a remote desktop session.
    public static string TargetName(Luid adapter, uint target)
    {
        var request = new TargetDeviceName
        {
            Header = Header(GetTargetName, Marshal.SizeOf<TargetDeviceName>(), adapter, target),
            MonitorFriendlyDeviceName = string.Empty,
            MonitorDevicePath = string.Empty
        };

        if (DisplayConfigGetDeviceInfo(ref request) != Success) return string.Empty;

        return request.MonitorFriendlyDeviceName ?? string.Empty;
    }

    // What is known about a screen from the display configuration: the name Windows shows for it,
    // and whether the wire it hangs on can carry sound at all.
    internal readonly record struct Screen(string Name, bool CarriesAudio);

    // Screens by their GDI name — "\\.\DISPLAY1" to "XV320QU LV". That GDI name is what a monitor
    // handle gives, and it is the only thing tying the two enumerations together.
    public static Dictionary<string, Screen> ByGdiDevice()
    {
        var screens = new Dictionary<string, Screen>(StringComparer.OrdinalIgnoreCase);

        foreach (PathInfo path in ActivePaths())
        {
            string gdi = SourceName(path.SourceInfo.AdapterId, path.SourceInfo.Id);
            if (gdi.Length == 0) continue;

            string friendly = TargetName(path.TargetInfo.AdapterId, path.TargetInfo.Id);
            if (friendly.Length == 0) continue;

            // A duplicated desktop puts several screens on one source. The first is kept: there is
            // only one line in the menu to put it on.
            screens.TryAdd(gdi, new Screen(friendly, CarriesAudio(path.TargetInfo.OutputTechnology)));
        }

        return screens;
    }

    // Whether sound can travel down this kind of connection: HDMI and DisplayPort carry it, DVI
    // and VGA do not, however much the monitor's name looks like the sound device's.
    private static bool CarriesAudio(uint technology) => technology switch
    {
        Hdmi or DisplayPortExternal or DisplayPortEmbedded or UsbTunnel => true,
        _ => false
    };

    private const uint Hdmi = 5;
    private const uint DisplayPortExternal = 10;
    private const uint DisplayPortEmbedded = 11;

    // A screen over USB-C or a dock: the picture is tunnelled, sound with it. Windows numbers it
    // 18, past the older block; 13 is UDI, and a docked monitor was told it had no speakers.
    private const uint UsbTunnel = 18;

    // The name the rest of Windows knows the screen by: "\\.\DISPLAY1".
    public static string SourceName(Luid adapter, uint source)
    {
        var request = new SourceDeviceName
        {
            Header = Header(GetSourceName, Marshal.SizeOf<SourceDeviceName>(), adapter, source),
            ViewGdiDeviceName = string.Empty
        };

        if (DisplayConfigGetDeviceInfo(ref request) != Success) return string.Empty;

        return request.ViewGdiDeviceName ?? string.Empty;
    }

    public static DeviceInfoHeader Header(uint type, int size, Luid adapter, uint target) => new()
    {
        Type = type,
        Size = (uint)size,
        AdapterId = adapter,
        Id = target
    };
}

// One attached screen and what can be driven on it.
internal sealed class DisplayDevice : IDisposable
{
    private readonly MonitorControl.PhysicalMonitor[] _physical;

    private DisplayDevice(IntPtr monitor, string name, string gdiName, bool isInternal,
                          bool carriesAudio, MonitorControl.PhysicalMonitor[] physical)
    {
        Monitor = monitor;
        Name = name;
        GdiName = gdiName;
        IsInternal = isInternal;
        CarriesAudio = carriesAudio;
        _physical = physical;
    }

    public IntPtr Monitor { get; }

    // The name as the system gives it: "Dell U2720Q" or "Generic PnP Monitor".
    public string Name { get; }

    // The name the rest of Windows knows the screen by: "\\.\DISPLAY1".
    public string GdiName { get; }

    public bool IsInternal { get; }

    // HDMI and DisplayPort carry sound; DVI and VGA do not, whatever the screen is called.
    public bool CarriesAudio { get; }

    // Decided when the screen is opened: a monitor does not grow a control halfway through the day.
    public bool ControlsBrightness { get; private set; }

    public bool ControlsSpeakerVolume { get; private set; }

    // Written from the DDC thread, read from the key handler: a double lands in one piece where a
    // nullable one would be two writes with a gap in the middle.
    public double Brightness { get; private set; }

    public double SpeakerVolume { get; private set; }

    // Opens the screen and finds out what it can do.
    public static DisplayDevice Open(IntPtr monitor)
    {
        (string name, string gdiName, bool carriesAudio) = MonitorControl.Describe(monitor);
        bool isInternal = MonitorControl.IsInternal(monitor);

        MonitorControl.PhysicalMonitor[] physical = isInternal
            ? Array.Empty<MonitorControl.PhysicalMonitor>()
            : MonitorControl.Open(monitor);

        var device = new DisplayDevice(monitor, name, gdiName, isInternal, carriesAudio, physical);

        double? brightness = isInternal
            ? InternalBrightness.Get()
            : physical.Select(p => MonitorControl.Brightness(p.Handle)).FirstOrDefault(v => v is not null);

        double? volume = isInternal
            ? null
            : physical.Select(p => MonitorControl.Feature(p.Handle, MonitorControl.VcpSpeakerVolume))
                      .FirstOrDefault(v => v is not null);

        device.ControlsBrightness = brightness is not null;
        device.Brightness = brightness ?? 0;

        device.ControlsSpeakerVolume = volume is not null;
        device.SpeakerVolume = volume ?? 0;

        // "DDC/CI" is what the monitor answered to, not what it is called: a handle is no answer,
        // Windows hands one out for a remote session's virtual display that takes no command.
        string link = isInternal ? "built-in panel"
                      : physical.Length == 0 ? "external, no DDC/CI"
                      : brightness is null && volume is null ? "external, DDC/CI unanswered"
                      : "external, DDC/CI";

        Log.Info($"display \"{name}\": {link}, " +
                 $"brightness {Show(brightness)}, monitor speakers {Show(volume)}" +
                 $"{(carriesAudio ? ", carries audio" : "")}");

        // The handle was given out and the monitor still answered nothing worth having: on most
        // panels a switch in their own menu, off from the factory, that nothing here can turn on.
        if (!isInternal && physical.Length > 0 && brightness is null && volume is null)
            Log.Info($"\"{name}\" is on a wire that carries DDC/CI but answers none of it: look " +
                     "for DDC/CI (HP calls it \"DDC/CI\", LG \"DDC/CI\" or \"Auto\", Dell \"DDC/CI\") " +
                     "in the monitor's own menu and turn it on. A dock, a KVM or a DP/HDMI adapter " +
                     "in the way can swallow it just as thoroughly.");

        return device;

        static string Show(double? value) => value is null ? "no" : $"{value.Value:0} %";
    }

    // Asks the monitor what it stands at: the numbers held here are only what the app last set,
    // and the buttons on the monitor answer to nobody.
    public void Reread()
    {
        if (IsInternal)
        {
            if (ControlsBrightness)
                DdcQueue.Run(Job("read-brightness"), () => Brightness = InternalBrightness.Get() ?? Brightness);

            return;
        }

        foreach (MonitorControl.PhysicalMonitor screen in _physical)
        {
            IntPtr handle = screen.Handle;

            if (ControlsBrightness)
            {
                DdcQueue.Run(Job("read-brightness", handle),
                             () => Brightness = MonitorControl.Brightness(handle) ?? Brightness);
            }

            if (ControlsSpeakerVolume)
            {
                DdcQueue.Run(Job("read-volume", handle),
                             () => SpeakerVolume = MonitorControl.Feature(handle, MonitorControl.VcpSpeakerVolume)
                                                   ?? SpeakerVolume);
            }
        }
    }

    // What makes two commands the same thing in the queue: this screen, that value.
    private string Job(string what, IntPtr handle = default) => $"{Monitor}:{handle}:{what}";

    // Sets the brightness. The value in memory changes at once while the command goes to the queue:
    // the caller is the key hook and cannot wait for the monitor to answer.
    public void SetBrightness(double percent)
    {
        if (!ControlsBrightness) return;

        percent = Math.Clamp(percent, 0, 100);
        Brightness = percent;

        if (IsInternal)
        {
            DdcQueue.Run(Job("brightness"), () => InternalBrightness.Set(percent));
            return;
        }

        foreach (MonitorControl.PhysicalMonitor physical in _physical)
        {
            IntPtr handle = physical.Handle;
            DdcQueue.Run(Job("brightness", handle), () => MonitorControl.SetBrightness(handle, percent));
        }
    }

    // Sets the monitor speaker volume over DDC.
    public void SetSpeakerVolume(double percent)
    {
        if (!ControlsSpeakerVolume) return;

        percent = Math.Clamp(percent, 0, 100);
        SpeakerVolume = percent;

        foreach (MonitorControl.PhysicalMonitor physical in _physical)
        {
            IntPtr handle = physical.Handle;
            DdcQueue.Run(Job("volume", handle),
                         () => MonitorControl.SetFeature(handle, MonitorControl.VcpSpeakerVolume, percent));
        }
    }

    public void Dispose() => MonitorControl.Close(_physical);
}

// One queue for every monitor command: DDC is serial and each exchange is too slow for the key
// hook to wait on. A newer command replaces an older one waiting under the same name.
internal static class DdcQueue
{
    private static readonly ConcurrentDictionary<string, Action> Pending = new();
    private static readonly BlockingCollection<string> Waiting = new();
    private static readonly Lazy<Thread> Worker = new(StartWorker, LazyThreadSafetyMode.ExecutionAndPublication);

    // The name is what makes two commands the same thing. Reads and writes carry different ones,
    // or a read would swallow the write it was meant to follow.
    public static void Run(string name, Action command)
    {
        _ = Worker.Value;

        Pending[name] = command;

        try { Waiting.Add(name); }
        catch (InvalidOperationException) { /* queue closed: the app is exiting */ }
    }

    private static Thread StartWorker()
    {
        var thread = new Thread(Pump)
        {
            IsBackground = true, // exiting must not wait for a monitor
            Name = "DDC"
        };
        thread.Start();
        return thread;
    }

    private static void Pump()
    {
        foreach (string name in Waiting.GetConsumingEnumerable())
        {
            // Nothing under that name: the stale ticket of a command that was overtaken.
            if (!Pending.TryRemove(name, out Action? command)) continue;

            try
            {
                command();
            }
            catch (Exception ex)
            {
                // One stubborn monitor must not stall the queue for the rest.
                Log.Error("a monitor command failed", ex);
            }
        }
    }

    // Closes the queue: what is already in it still runs, nothing new is accepted.
    public static void Stop()
    {
        try { Waiting.CompleteAdding(); }
        catch (ObjectDisposedException) { /* already closed */ }
    }
}

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

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, ref Win32.RECT rect, IntPtr data);

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
        public Win32.RECT Monitor;
        public Win32.RECT Work;
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
        bool Collect(IntPtr monitor, IntPtr dc, ref Win32.RECT rect, IntPtr data)
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

        // The display configuration knows the screen by the name Windows settings show. Asked
        // first: the older enumeration below answers "Generic PnP Monitor" for nearly all.
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

            // GetMonitorBrightness asks for the capabilities string first and gives up when that
            // is malformed or slow. The same panel usually answers command 0x10 without a murmur.
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

// Brightness of the built-in laptop panel.
internal static class InternalBrightness
{
    private const string Scope = @"root\WMI";

    // Seconds Windows gives the panel to fade.
    private const uint Instant = 0;

    // Current brightness in percent, or null when there is no built-in panel.
    public static double? Get()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(Scope, "SELECT * FROM WmiMonitorBrightness");
            using ManagementObjectCollection results = searcher.Get();

            foreach (ManagementBaseObject item in results)
            {
                using (item)
                {
                    // CurrentBrightness is already a percentage — the same number the slider shows.
                    if (item["CurrentBrightness"] is byte value) return value;
                }
            }
        }
        catch (ManagementException ex)
        {
            // The class is missing on a desktop: an ordinary case, not a failure.
            System.Diagnostics.Debug.WriteLine($"WmiMonitorBrightness is unavailable: {ex.Message}");
        }
        catch (Exception ex)
        {
            Log.Error("the built-in panel brightness was not read", ex);
        }

        return null;
    }

    // Sets the built-in panel brightness.
    public static bool Set(double percent)
    {
        byte value = (byte)Math.Clamp(Math.Round(percent), 0, 100);

        try
        {
            using var searcher = new ManagementObjectSearcher(Scope, "SELECT * FROM WmiMonitorBrightnessMethods");
            using ManagementObjectCollection results = searcher.Get();

            bool applied = false;
            foreach (ManagementBaseObject item in results)
            {
                if (item is not ManagementObject method) continue;

                using (method)
                {
                    method.InvokeMethod("WmiSetBrightness", new object[] { Instant, value });
                    applied = true;
                }
            }

            return applied;
        }
        catch (ManagementException ex)
        {
            System.Diagnostics.Debug.WriteLine($"WmiSetBrightness is unavailable: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Log.Error("the built-in panel brightness was not set", ex);
            return false;
        }
    }
}

// What to do with a pressed volume or brightness key: by how much, to what, and whether to take it
// on at all.
public sealed class DisplayManager : IDisposable
{
    private readonly OsdConfig _cfg;
    private readonly List<DisplayDevice> _displays = new();

    public DisplayManager(OsdConfig cfg)
    {
        _cfg = cfg;
    }

    // For the tray menu: the name, and whether the screen answers at all. One that does not is
    // marked "(off)", since a greyed name alone reads as our fault rather than a switch to find.
    public IReadOnlyList<(string Name, bool Controllable)> DisplayNames =>
        _displays.Select(d => (Label(d), d.ControlsBrightness || d.ControlsSpeakerVolume)).ToList();

    private static string Label(DisplayDevice display)
    {
        if (display.IsInternal) return display.Name + " (built-in)";

        return display.ControlsBrightness || display.ControlsSpeakerVolume
            ? display.Name
            : display.Name + " (off)";
    }

    public bool HasBrightnessControl => _displays.Any(d => d.ControlsBrightness);

    // What the custom OSD is for. Without a monitor driven over DDC, Windows does the job itself
    // and shows its own panel — ours would only repeat it — so the keys are left alone.
    private bool DrivesBrightnessOverDdc =>
        _cfg.ControlExternalBrightness && _displays.Any(d => !d.IsInternal && d.ControlsBrightness);

    private bool DrivesVolumeOverDdc => _cfg.ControlExternalVolume && MonitorSpeakers() is not null;

    // The numbers held here are what the app last set; the buttons on the monitor answer to nobody.
    // The reading goes to the DDC queue and lands when it lands.
    public void RereadValues()
    {
        foreach (DisplayDevice display in _displays) display.Reread();
    }

    // Polls the attached screens again.
    public void Refresh()
    {
        Release();

        foreach (IntPtr monitor in MonitorControl.Handles())
        {
            try
            {
                _displays.Add(DisplayDevice.Open(monitor));
            }
            catch (Exception ex)
            {
                Log.Error("a display was not opened", ex);
            }
        }

        // The screens are re-polled hourly and on every sound device change, so the same line
        // would repeat all day. Only a different reading is worth keeping without Debug.
        string summary = $"displays: {_displays.Count} found ({string.Join(", ", DisplayNames.Select(d => d.Name))}), " +
                         $"brightness control {(HasBrightnessControl ? "available" : "unavailable")}";

        if (summary == _lastSummary) Log.Info(summary);
        else Log.Event(_lastSummary is null ? summary : summary + " — this is a change");

        _lastSummary = summary;
    }

    // What Refresh() reported last time, to tell a change from the hourly look.
    private string? _lastSummary;

    private double Step => 100.0 / Math.Clamp(_cfg.AdjustmentSteps,
                                              AppParameters.Limits.MinAdjustmentSteps,
                                              AppParameters.Limits.MaxAdjustmentSteps);

    // Rounding to the step grid is the whole point of the "adjustment steps" setting: without it
    // the first press after someone else moved the value would land on a fractional tick.
    private double Next(double current, bool up)
    {
        double stepped = Math.Round(current / Step) * Step + (up ? Step : -Step);
        return Math.Clamp(stepped, 0, 100);
    }

    // Moves the brightness of one screen: the one the pointer is on.
    public MediaKeyResult AdjustBrightness(bool up, out double shown)
    {
        shown = 0;

        if (!MediaKeyRules.Takes(DrivesBrightnessOverDdc, _cfg.AlwaysUseCustomOsd))
            return MediaKeyResult.PassThrough;

        DisplayDevice? screen = BrightnessTarget();
        MediaKeyResult result = MediaKeyRules.Brightness(DrivesBrightnessOverDdc, _cfg.AlwaysUseCustomOsd,
                                                         screen is not null);

        // Only with the full record: a line per press, and it is what says which way the decision
        // went when the keys do something other than what was expected.
        Log.Info($"brightness key: ddc={Yes(DrivesBrightnessOverDdc)} always={Yes(_cfg.AlwaysUseCustomOsd)} " +
                 $"target=\"{screen?.GdiName ?? "none"}\" -> {result}");

        if (result != MediaKeyResult.Consumed || screen is null) return result;

        shown = Next(screen.Brightness, up);
        screen.SetBrightness(shown);

        return result;
    }

    // The screen under the pointer where it can be driven, otherwise the first that can.
    private DisplayDevice? BrightnessTarget()
    {
        IntPtr active = Win32.MonitorUnderPointer();

        return _displays.FirstOrDefault(d => d.Monitor == active && Drivable(d))
               ?? _displays.FirstOrDefault(Drivable);

        bool Drivable(DisplayDevice display) =>
            display.ControlsBrightness && (display.IsInternal || _cfg.ControlExternalBrightness);
    }

    // The sound passes through two attenuators, the Windows mixer and the monitor's own volume,
    // moved together to the same number: one alone leaves a second nothing on screen shows.
    public MediaKeyResult AdjustVolume(bool up, out double shown)
    {
        shown = 0;

        DisplayDevice? speakers = _cfg.ControlExternalVolume ? MonitorSpeakers() : null;

        if (!MediaKeyRules.Takes(speakers is not null, _cfg.AlwaysUseCustomOsd))
            return MediaKeyResult.PassThrough;

        double? mixer = AudioEndpoint.Volume();

        // The step is taken from the mixer: the finer scale, and the one everything else agrees
        // with. Found apart, the two are brought together by the first press.
        double current = mixer ?? speakers?.SpeakerVolume ?? 0;

        shown = Next(current, up);

        bool moved = mixer is not null && AudioEndpoint.SetVolume(shown);

        if (speakers is not null)
        {
            speakers.SetSpeakerVolume(shown);
            moved = true;
        }

        return MediaKeyRules.Volume(moved, _cfg.AlwaysUseCustomOsd);
    }

    // Toggles mute. The OSD then shows zero, or the previous volume.
    public MediaKeyResult ToggleMute(out double shown)
    {
        shown = 0;

        if (!MediaKeyRules.Takes(DrivesVolumeOverDdc, _cfg.AlwaysUseCustomOsd)) return MediaKeyResult.PassThrough;

        bool? muted = AudioEndpoint.ToggleMute();
        if (muted is null) return MediaKeyRules.Volume(false, _cfg.AlwaysUseCustomOsd);

        // Muted means zero on the scale, or "unchanged but inaudible" is what it would read as.
        shown = muted.Value ? 0 : AudioEndpoint.Volume() ?? 0;
        return MediaKeyResult.Consumed;
    }

    // The monitor the sound goes to. The name before the brackets is matched whole, or a screen
    // called "PC" would match half the sound devices; the connection has to carry sound too.
    private DisplayDevice? MonitorSpeakers()
    {
        string output = AudioEndpoint.DefaultDeviceName();
        if (output.Length == 0) return null;

        string label = ScreenLabel(output);

        return _displays.FirstOrDefault(d => Speaks(d) && d.Name.Equals(label, StringComparison.OrdinalIgnoreCase))
               // Not every driver names its output that way: one name inside the other is still
               // worth trying, now that a silent connection can no longer be what it lands on.
               ?? _displays.FirstOrDefault(d => Speaks(d) &&
                                                (output.Contains(d.Name, StringComparison.OrdinalIgnoreCase) ||
                                                 d.Name.Contains(output, StringComparison.OrdinalIgnoreCase)));

        static bool Speaks(DisplayDevice display) => display.ControlsSpeakerVolume && display.CarriesAudio;
    }

    private static string Yes(bool value) => value ? "1" : "0";

    private static string ScreenLabel(string output)
    {
        int bracket = output.LastIndexOf('(');
        return bracket > 0 ? output[..bracket].Trim() : output.Trim();
    }

    private void Release()
    {
        foreach (DisplayDevice display in _displays) display.Dispose();
        _displays.Clear();
    }

    public void Dispose() => Release();
}
