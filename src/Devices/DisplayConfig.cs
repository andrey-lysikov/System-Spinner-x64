using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SystemSpinnerX64.Diagnostics;

namespace SystemSpinnerX64.Devices;

// The Windows display configuration: the paths from a graphics adapter to a screen. This is where
// the names shown in the display settings live — "XV320QU LV" rather than "Generic PnP Monitor" —
// and where the HDR switch is kept.
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

    // Whether sound can travel down this kind of connection. HDMI and DisplayPort carry it; DVI,
    // VGA and the rest do not, and a monitor on one of those has no speakers to drive however much
    // its name looks like the name of the sound device.
    private static bool CarriesAudio(uint technology) => technology switch
    {
        Hdmi or DisplayPortExternal or DisplayPortEmbedded or UsbTunnel => true,
        _ => false
    };

    private const uint Hdmi = 5;
    private const uint DisplayPortExternal = 10;
    private const uint DisplayPortEmbedded = 11;

    // A screen over USB-C or a docking station: the picture is tunnelled, sound with it.
    private const uint UsbTunnel = 13;

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
