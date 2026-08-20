using System;
using System.Windows;
using System.Windows.Interop;
using SystemSpinnerX64.Diagnostics;
using SystemSpinnerX64.Platform;

namespace SystemSpinnerX64.Views;

/// <summary>
/// Where to put a popup window. Three places need this — the volume OSD, the status window and
/// the About box — and all of them run into the same thing: several monitors, each with its own
/// scale, its own taskbar and its own place in one shared coordinate grid.
///
/// Everything here is counted in pixels of that shared grid and the window is moved with
/// SetWindowPos. WPF's own Left and Top are read in the units of the screen the window is on at
/// that moment, which — for a window about to be shown on a different monitor — is the screen it
/// is leaving: at 100 and 150 per cent side by side that lands it half a screen off.
///
/// The taskbar is the other half of it. It has a size, it can sit at any edge, and on a second
/// monitor it may not be there at all — hence the work area, the screen minus the docked bars,
/// rather than the screen itself.
/// </summary>
internal static class ScreenPlacement
{
    /// <summary>The mouse pointer, in pixels. This is the grid everything here counts in.</summary>
    public static System.Drawing.Point Pointer()
    {
        try
        {
            return System.Windows.Forms.Cursor.Position;
        }
        catch (Exception ex)
        {
            Log.Error("the pointer position was not read", ex);
            return System.Drawing.Point.Empty;
        }
    }

    /// <summary>
    /// Puts the window against the side the taskbar is on and centres it on the tray icon.
    ///
    /// Windows does not tell an application where the icon is — <c>NotifyIcon</c> gives up neither
    /// its window nor its id, and without those the system will not report the rectangle. But the
    /// window is opened by clicking the icon, and the pointer is right on it at that moment: that
    /// is the reference point.
    /// </summary>
    /// <param name="anchor">
    /// The pointer as it was when the window was opened, in pixels. Passed in rather than read
    /// here because the window is placed again whenever its height changes, and by then the
    /// pointer has moved: reading it afresh would drag the window along.
    /// </param>
    /// <param name="gap">Gap between the window and the work area edge, in WPF units.</param>
    public static void PutNearTray(Window window, System.Drawing.Point anchor, double gap)
    {
        Screen screen = At(anchor);

        Put(window, TrayCorner(
            Box(screen.Bounds), Box(screen.Work),
            new Point(anchor.X, anchor.Y),
            PixelSize(window, screen.Scale),
            gap * screen.Scale));
    }

    /// <summary>
    /// The arithmetic of the above, in pixels and without a window to it: which side the taskbar
    /// is on, where on it the icon sits, and what of that still fits on the screen.
    /// </summary>
    internal static Point TrayCorner(Rect bounds, Rect work, Point anchor, Size size, double gap)
    {
        // The taskbar is the difference between the screen and the work area. Whichever side it
        // bit more off is the side it sits on.
        double left = work.Left - bounds.Left;
        double top = work.Top - bounds.Top;
        double right = bounds.Right - work.Right;
        double bottom = bounds.Bottom - work.Bottom;

        double x, y;
        if (Math.Max(left, right) > Math.Max(top, bottom))
        {
            // Taskbar on the left or right: the window sits against it and is centred vertically
            // on the icon.
            x = left > right ? work.Left + gap : work.Right - size.Width - gap;
            y = anchor.Y - size.Height / 2;
        }
        else
        {
            // Taskbar on top or bottom, the usual case: the window sits against it and is centred
            // horizontally on the icon.
            x = anchor.X - size.Width / 2;
            y = top > bottom ? work.Top + gap : work.Bottom - size.Height - gap;
        }

        return Clamp(new Point(x, y), size, work, gap);
    }

    /// <summary>
    /// Puts the window beside another one, aligned to its bottom edge — the chart window next to
    /// the status window. To the left, unless that runs off the screen; then to the right.
    /// </summary>
    /// <param name="gap">Gap between the two windows, in WPF units.</param>
    public static void PutBeside(Window window, Window neighbour, double gap)
    {
        System.Drawing.Rectangle anchor = PixelBounds(neighbour);
        Screen screen = At(new System.Drawing.Point(anchor.Left + anchor.Width / 2,
                                                    anchor.Top + anchor.Height / 2));

        Put(window, BesideCorner(
            Box(anchor), Box(screen.Work),
            PixelSize(window, screen.Scale),
            gap * screen.Scale));
    }

    /// <summary>The arithmetic of the above, in pixels: to the left if it fits, else to the right.</summary>
    internal static Point BesideCorner(Rect anchor, Rect work, Size size, double gap)
    {
        double x = anchor.Left - size.Width - gap;
        if (x < work.Left) x = anchor.Right + gap;
        if (x + size.Width > work.Right) x = Math.Max(work.Left, work.Right - size.Width);

        double y = anchor.Bottom - size.Height;
        if (y < work.Top) y = work.Top;
        if (y + size.Height > work.Bottom) y = Math.Max(work.Top, work.Bottom - size.Height);

        return new Point(x, y);
    }

    /// <summary>
    /// Puts the window in the middle of the screen at a given height above its bottom edge — the
    /// OSD. From the screen, not the work area: the OSD also appears over a game, where there is
    /// no taskbar. The screen is the one under the pointer: a volume key is pressed while looking
    /// where the mouse is.
    /// </summary>
    /// <param name="bottomInset">Height above the bottom edge, in WPF units.</param>
    public static void PutAboveBottom(Window window, double bottomInset)
    {
        Screen screen = At(Pointer());
        Size size = PixelSize(window, screen.Scale);
        System.Drawing.Rectangle bounds = screen.Bounds;

        Put(window, new Point(
            Math.Round(bounds.Left + (bounds.Width - size.Width) / 2),
            Math.Round(bounds.Bottom - bottomInset * screen.Scale - size.Height)));
    }

    /// <summary>Centres the window on the screen under the pointer.</summary>
    public static void PutCentred(Window window)
    {
        Screen screen = At(Pointer());
        Size size = PixelSize(window, screen.Scale);
        System.Drawing.Rectangle work = screen.Work;

        Put(window, new Point(
            Math.Round(work.Left + (work.Width - size.Width) / 2),
            Math.Round(work.Top + (work.Height - size.Height) / 2)));
    }

    /// <summary>A pixel rectangle as WPF states it — the form the arithmetic above is written in.</summary>
    private static Rect Box(System.Drawing.Rectangle rect) =>
        new(rect.Left, rect.Top, rect.Width, rect.Height);

    /// <summary>One monitor: its rectangles in pixels and its scale.</summary>
    private readonly record struct Screen(
        System.Drawing.Rectangle Bounds,
        System.Drawing.Rectangle Work,
        double Scale);

    /// <summary>The monitor a point in pixels falls on. The nearest one when it falls off them all.</summary>
    private static Screen At(System.Drawing.Point point)
    {
        try
        {
            System.Windows.Forms.Screen screen = System.Windows.Forms.Screen.FromPoint(point);
            return new Screen(screen.Bounds, screen.WorkingArea, Win32.ScaleAt(point.X, point.Y));
        }
        catch (Exception ex)
        {
            Log.Error("the screen was not determined", ex);

            System.Windows.Forms.Screen fallback =
                System.Windows.Forms.Screen.PrimaryScreen ?? System.Windows.Forms.Screen.AllScreens[0];

            return new Screen(fallback.Bounds, fallback.WorkingArea, 1);
        }
    }

    /// <summary>
    /// The size the window will have on the target screen, in pixels. Its own units are those of
    /// the screen it is on now; moving it to one at another scale changes the pixel size to match,
    /// and that is the size to place by.
    /// </summary>
    private static Size PixelSize(Window window, double scale)
    {
        double width = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
        double height = window.ActualHeight > 0 ? window.ActualHeight : window.Height;

        if (double.IsNaN(width)) width = 0;
        if (double.IsNaN(height)) height = 0;

        return new Size(width * scale, height * scale);
    }

    /// <summary>Where a window is now, in pixels.</summary>
    private static System.Drawing.Rectangle PixelBounds(Window window)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero && Win32.TryGetWindowRect(handle, out Win32.RECT rect))
            return System.Drawing.Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);

        // The window has no handle yet — its own units are all there is to go on.
        return System.Drawing.Rectangle.FromLTRB(
            (int)window.Left, (int)window.Top,
            (int)(window.Left + window.ActualWidth), (int)(window.Top + window.ActualHeight));
    }

    /// <summary>
    /// Keeps the window inside the work area. The icon can sit right in a corner, and a window
    /// centred on it would hang half off the screen.
    /// </summary>
    private static Point Clamp(Point corner, Size window, Rect work, double gap)
    {
        double x = Math.Min(corner.X, work.Right - window.Width - gap);
        double y = Math.Min(corner.Y, work.Bottom - window.Height - gap);

        return new Point(Math.Max(work.Left + gap, x), Math.Max(work.Top + gap, y));
    }

    /// <summary>Moves the window to a point in pixels, leaving its size and its order alone.</summary>
    private static void Put(Window window, Point corner)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        Win32.SetWindowPos(handle, IntPtr.Zero,
            (int)Math.Round(corner.X), (int)Math.Round(corner.Y), 0, 0,
            Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    }
}
