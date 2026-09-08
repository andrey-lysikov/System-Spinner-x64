//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using SystemSpinnerX64.Diagnostics;
using Microsoft.Win32;

namespace SystemSpinnerX64.Platform;

// The user32 calls the overlay cannot do without: click-through, topmost, foreground window.
internal static class Win32
{
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int WS_EX_TOOLWINDOW = 0x00000080;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;

    public static readonly IntPtr HWND_TOPMOST = new(-1);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    // Hands activation to a window. The tray menu needs it to close on a click elsewhere.
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    // Frees an HICON obtained from Bitmap.GetHicon().
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    // Makes the window transparent to mouse and keyboard — clicks go to the game.
    public static void SetClickThrough(IntPtr hWnd, bool enabled)
    {
        int ex = GetWindowLong(hWnd, GWL_EXSTYLE);
        ex = enabled
            ? ex | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_LAYERED | WS_EX_TOOLWINDOW
            : (ex & ~WS_EX_TRANSPARENT & ~WS_EX_NOACTIVATE) | WS_EX_LAYERED | WS_EX_TOOLWINDOW;
        SetWindowLong(hWnd, GWL_EXSTYLE, ex);
    }

    // Brings the window back to the top without taking focus.
    public static void ForceTopmost(IntPtr hWnd) =>
        SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT : IEquatable<RECT>
    {
        public int Left, Top, Right, Bottom;

        // Compared to tell one screen from another: the game can move to a second monitor without
        // ever ceasing to be full screen.
        public bool Equals(RECT other) =>
            Left == other.Left && Top == other.Top && Right == other.Right && Bottom == other.Bottom;

        public override bool Equals(object? other) => other is RECT rect && Equals(rect);

        public override int GetHashCode() => HashCode.Combine(Left, Top, Right, Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    // Where a window is, in pixels — the one grid all the screens share.
    public static bool TryGetWindowRect(IntPtr hWnd, out RECT rect) => GetWindowRect(hWnd, out rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    // Shell windows: the desktop, the taskbar, the Start menu and search.
    private static readonly string[] ShellClasses =
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
        "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow"
    };

    // Whether the foreground window covers its whole monitor — how the overlay tells a game from
    // the desktop. The window itself is handed out too: the panel is kept away from some of them.
    public static bool TryFullscreenArea(out RECT work, out double scale, out IntPtr hwnd)
    {
        work = default;
        scale = 1;

        hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;

        // The desktop and the taskbar formally cover the screen but are not games.
        var className = new StringBuilder(64);
        if (GetClassName(hwnd, className, className.Capacity) > 0)
        {
            string name = className.ToString();
            foreach (string shell in ShellClasses)
                if (string.Equals(name, shell, StringComparison.Ordinal)) return false;
        }

        if (!GetWindowRect(hwnd, out RECT window)) return false;

        IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info)) return false;

        // Not "equal" but "no smaller": some games make the window a pixel larger than the screen.
        RECT screen = info.rcMonitor;
        bool covered = window.Left <= screen.Left && window.Top <= screen.Top &&
                       window.Right >= screen.Right && window.Bottom >= screen.Bottom;

        if (!covered) return false;

        work = info.rcWork;
        scale = ScaleOf(monitor);
        return true;
    }

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    // The full path of the exe behind a window, or null when the system keeps it back.
    // Limited rights are enough here, so a protected process still names its file.
    public static string? ProcessPathOf(IntPtr hWnd)
    {
        if (GetWindowThreadProcessId(hWnd, out uint pid) == 0 || pid == 0) return null;

        IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero) return null;

        try
        {
            var path = new StringBuilder(1024);
            uint size = (uint)path.Capacity;
            return QueryFullProcessImageName(process, 0, path, ref size) && size > 0 ? path.ToString() : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    // The scale of one monitor: 1.5 at 150 per cent.
    public static double ScaleOf(IntPtr monitor)
    {
        try
        {
            if (GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0)
                return dpiX / 96.0;
        }
        catch (DllNotFoundException)
        {
            // shcore.dll is there on every Windows 11; the guard is for the sake of never
            // bringing the app down over a placement detail.
        }

        return 1;
    }

    // The scale of the monitor a point in pixels falls on.
    public static double ScaleAt(int x, int y) =>
        ScaleOf(MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST));

    // Whether this desktop is being drawn by a remote client. Then the session hangs on a virtual
    // display, no handle from EnumDisplayMonitors reaches the real monitors, and DDC/CI has no wire.
    public static bool IsRemoteSession => GetSystemMetrics(SM_REMOTESESSION) != 0;

    private const int SM_REMOTESESSION = 0x1000;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    // The monitor the pointer is on — the screen being looked at.
    public static IntPtr MonitorUnderPointer() =>
        GetCursorPos(out POINT pointer)
            ? MonitorFromPoint(pointer, MONITOR_DEFAULTTONEAREST)
            : IntPtr.Zero;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    private const int MDT_EFFECTIVE_DPI = 0;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);
}

// The blurred backdrop of Windows 11: the window has neither a background of its own nor an
// image, so the volume panel stays readable on a light desktop and on a dark one.
internal static class Dwm
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;

    // DWMSBT_TRANSIENTWINDOW — acrylic, the backdrop of popup windows.
    private const int BackdropAcrylic = 3;

    // DWMWCP_ROUND — the large rounding the system uses for popup menus.
    private const int CornerRound = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    // Asks the system for an acrylic backdrop, rounded corners and the dark or light set of system
    // colours.
    public static bool ApplyAcrylic(IntPtr hwnd, bool dark)
    {
        if (hwnd == IntPtr.Zero) return false;

        int darkMode = dark ? 1 : 0;
        Set(hwnd, DwmwaUseImmersiveDarkMode, ref darkMode);

        int corner = CornerRound;
        Set(hwnd, DwmwaWindowCornerPreference, ref corner);

        int backdrop = BackdropAcrylic;
        return Set(hwnd, DwmwaSystemBackdropType, ref backdrop);
    }

    private static bool Set(IntPtr hwnd, int attribute, ref int value)
    {
        try
        {
            return DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int)) == 0;
        }
        catch (Exception ex)
        {
            // dwmapi ships with every Windows 11, but an older build may lack the attribute.
            System.Diagnostics.Debug.WriteLine($"DWM attribute {attribute} was refused: {ex.Message}");
            return false;
        }
    }
}

// Whether the taskbar is light or dark.
internal static class Theme
{
    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    // Whether the taskbar is light. Unknown counts as dark: that is the default.
    public static bool IsTaskbarLight()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);

            // SystemUsesLightTheme specifically: AppsUseLightTheme covers windows, not the taskbar.
            return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"the taskbar theme was not read: {ex.Message}");
            return false;
        }
    }

    // Whether windows are dark — the switch the custom OSD is painted by.
    public static bool AreWindowsDark()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is not int value || value == 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"the app theme was not read: {ex.Message}");
            return true;
        }
    }
}

// Whether Windows will show a notification at all: with notifications off, or Do not disturb on,
// a tray balloon never appears and says nothing about it. What must be told goes to a dialog.
internal static class Notifications
{
    private const string PushKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\PushNotifications";

    public static bool AreEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PushKey);

            // No value means nobody has switched them off.
            return key?.GetValue("ToastEnabled") is not int enabled || enabled != 0;
        }
        catch (Exception ex)
        {
            Log.Warn($"the notification setting was not read: {ex.Message}");
            return true;
        }
    }
}

// LibreHardwareMonitor 0.9.6 carries no driver inside it the way WinRing0 did: it expects PawnIO,
// a separate signed driver with its own installer, and merely loads its modules into it.
internal static class SensorDriver
{
    // The PawnIO installer leaves an entry in the list of installed programs.
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";

    // true means PawnIO is installed, null means it could not be established.
    public static bool? IsPawnIoInstalled()
    {
        try
        {
            // The key is written to the 64-bit view of the registry — opened explicitly, or
            // a 32-bit process would not find it.
            using RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using RegistryKey? key = root.OpenSubKey(UninstallKey);
            return key is not null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Explanation for the log, or null when the driver is in place.
    public static string? DescribeIfMissing()
    {
        if (IsPawnIoInstalled() != false) return null;

        return "THE PawnIO DRIVER IS NOT INSTALLED — the app will not start without it.\n" +
               $"Installer: {AppParameters.Links.SensorDriver}\n" +
               "LibreHardwareMonitor uses this driver to read the CPU MSRs and the motherboard " +
               "monitoring chip. Without it there is no CPU temperature or power, and no fan " +
               "speeds for the CPU cooler, the pump or the case fans — half of both the overlay " +
               "and the statistics window would be dashes.\n" +
               "PawnIO is signed and works with Memory Integrity enabled, so there is no need " +
               "to turn it off. Start the app again once installed.";
    }
}

// The app targets Windows 11 x64 and an Intel or AMD processor.
internal static class PlatformGuard
{
    // Describes the Windows version mismatch, or null when it is fine.
    public static string? DescribeOs()
    {
        Version v = Environment.OSVersion.Version;
        if (v.Major > 10 || (v.Major == 10 && v.Build >= AppParameters.Requirements.Windows11Build)) return null;

        return $"Windows 11 or newer is required, but this is Windows {v.Major}, build {v.Build}.\n\n" +
               "On Windows 10 some sensors are named differently, the Present events used to count " +
               "frames work another way, and the tray has no per-monitor scaling for its icons.";
    }

    // Names come from LibreHardwareMonitor: "Intel Core Ultra 7 265K", "AMD Ryzen 9 7950X".
    public static string? DescribeHardware(string? cpuName, string? gpuName)
    {
        // Empty means the sensors did not open; a separate message already says so.
        if (string.IsNullOrWhiteSpace(cpuName)) return null;

        if (!IsIntel(cpuName) && !IsAmd(cpuName))
        {
            return $"The CPU was detected as \"{cpuName}\" — this build covers Intel and AMD.\n\n" +
                   "Temperature, power and the per-core clock are read by sensor name, and the " +
                   "defaults here are the ones those two report, so the values would stay empty.";
        }

        // The card is only reported: it can be any, and that is no reason to refuse.
        _ = gpuName;
        return null;
    }

    public static bool IsIntel(string cpuName) =>
        cpuName.IndexOf("intel", StringComparison.OrdinalIgnoreCase) >= 0;

    public static bool IsAmd(string cpuName) =>
        cpuName.IndexOf("amd", StringComparison.OrdinalIgnoreCase) >= 0 ||
        cpuName.IndexOf("ryzen", StringComparison.OrdinalIgnoreCase) >= 0 ||
        cpuName.IndexOf("threadripper", StringComparison.OrdinalIgnoreCase) >= 0;

    // How AMD sensor names are recognised: that processor reports its temperature only as Tctl,
    // Tdie or CCD, and Intel never uses those names.
    private static readonly Regex AmdTempName = new(@"Tctl|Tdie|CCD",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Whether the configured names suit the processor that was found. A config carried over from
    // another machine is the usual way to end up with a dash where the temperature should be.
    public static string? DescribeSensorNames(string? cpuName, IReadOnlyList<string> cpuTemp)
    {
        if (cpuName is not { Length: > 0 }) return null;

        // An empty list is a deliberate "do not show the temperature", like "Aio =" for the pump.
        // The check catches foreign names, not their absence.
        if (cpuTemp.Count == 0) return null;

        bool intel = IsIntel(cpuName);
        bool amd = IsAmd(cpuName);

        // Anything else was already refused, and a name we do not know is no reason to complain.
        if (!intel && !amd) return null;

        // Intel needs at least one name without Tctl/Tdie/CCD; AMD needs at least one with them.
        bool suits = intel
            ? cpuTemp.Any(name => !AmdTempName.IsMatch(name))
            : cpuTemp.Any(name => AmdTempName.IsMatch(name));

        if (suits) return null;

        string wanted = intel
            ? "    CpuTemp = CPU Package, Core Max, CPU Cores"
            : "    CpuTemp = Core (Tctl/Tdie), CPU Package, Core (Tctl)";

        return $"The CPU was detected as \"{cpuName}\", but CpuTemp in the config holds only " +
               $"{(intel ? "AMD" : "Intel")} sensor names — the temperature would show a dash.\n" +
               $"Configured names: {string.Join(", ", cpuTemp)}\n\n" +
               "Add a name that fits, for example:\n" +
               wanted + "\n" +
               "or delete the CpuTemp line — the defaults cover both. Deleting config.conf works " +
               "too: it will be created anew.";
    }
}
