using System;
using System.Runtime.InteropServices;
using System.Text;

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
    public const uint SWP_SHOWWINDOW = 0x0040;

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
    // the desktop.
    public static bool TryFullscreenArea(out RECT work, out double scale)
    {
        work = default;
        scale = 1;

        IntPtr hwnd = GetForegroundWindow();
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

    // Whether the desktop of this session is being drawn by a remote client — RDP. Then the
    // session hangs on the virtual display of the remote adapter, and the monitors on the graphics
    // card belong to nobody: no handle from EnumDisplayMonitors leads to them, DDC/CI has no wire
    // to travel down, and the HDR switch has nothing to switch.
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
