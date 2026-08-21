using System;
using System.Windows;
using System.Windows.Interop;
using SystemSpinnerX64.Diagnostics;
using SystemSpinnerX64.Platform;

namespace SystemSpinnerX64.Views;

// Where to put a popup window. Three places need this — the volume OSD, the status window and the
// About box — and all of them run into the same thing: several monitors, each with its own scale,
// its own taskbar and its own place in one shared coordinate grid.
internal static class ScreenPlacement
{
    // The mouse pointer, in pixels. This is the grid everything here counts in.
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

    // Puts the window against the side the taskbar is on and centres it on the tray icon. The
    // anchor is the pointer as it was when the window opened: reading it afresh on every
    // re-placement would drag the window after the mouse.
    public static void PutNearTray(Window window, System.Drawing.Point anchor, double gap)
    {
        Screen screen = At(anchor);

        Put(window, TrayCorner(
            Box(screen.Bounds), Box(screen.Work),
            new Point(anchor.X, anchor.Y),
            PixelSize(window, screen.Scale),
            gap * screen.Scale));
    }

    // The arithmetic of the above, in pixels and without a window to it: which side the taskbar is
    // on, where on it the icon sits, and what of that still fits on the screen.
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

    // Puts the window beside another one, aligned to its bottom edge — the chart window next to the
    // status window.
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

    // The arithmetic of the above, in pixels: to the left if it fits, else to the right.
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

    // Puts the window in the middle of the screen at a given height above its bottom edge — the
    // OSD.
    public static void PutAboveBottom(Window window, double bottomInset)
    {
        Screen screen = At(Pointer());
        Size size = PixelSize(window, screen.Scale);
        System.Drawing.Rectangle bounds = screen.Bounds;

        Put(window, new Point(
            Math.Round(bounds.Left + (bounds.Width - size.Width) / 2),
            Math.Round(bounds.Bottom - bottomInset * screen.Scale - size.Height)));
    }

    // Centres the window on the screen under the pointer.
    public static void PutCentred(Window window)
    {
        Screen screen = At(Pointer());
        Size size = PixelSize(window, screen.Scale);
        System.Drawing.Rectangle work = screen.Work;

        Put(window, new Point(
            Math.Round(work.Left + (work.Width - size.Width) / 2),
            Math.Round(work.Top + (work.Height - size.Height) / 2)));
    }

    // A pixel rectangle as WPF states it — the form the arithmetic above is written in.
    private static Rect Box(System.Drawing.Rectangle rect) =>
        new(rect.Left, rect.Top, rect.Width, rect.Height);

    // One monitor: its rectangles in pixels and its scale.
    private readonly record struct Screen(
        System.Drawing.Rectangle Bounds,
        System.Drawing.Rectangle Work,
        double Scale);

    // The monitor a point in pixels falls on.
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

    // The size the window will have on the target screen, in pixels.
    private static Size PixelSize(Window window, double scale)
    {
        double width = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
        double height = window.ActualHeight > 0 ? window.ActualHeight : window.Height;

        if (double.IsNaN(width)) width = 0;
        if (double.IsNaN(height)) height = 0;

        return new Size(width * scale, height * scale);
    }

    // Where a window is now, in pixels.
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

    // Keeps the window inside the work area.
    private static Point Clamp(Point corner, Size window, Rect work, double gap)
    {
        double x = Math.Min(corner.X, work.Right - window.Width - gap);
        double y = Math.Min(corner.Y, work.Bottom - window.Height - gap);

        return new Point(Math.Max(work.Left + gap, x), Math.Max(work.Top + gap, y));
    }

    // Moves the window to a point in pixels, leaving its size and its order alone.
    private static void Put(Window window, Point corner)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        Win32.SetWindowPos(handle, IntPtr.Zero,
            (int)Math.Round(corner.X), (int)Math.Round(corner.Y), 0, 0,
            Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    }
}
