//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using SystemSpinnerX64.Diagnostics;
using SystemSpinnerX64.Platform;

namespace SystemSpinnerX64.Views;

// The chrome the three popup windows share: an acrylic backdrop and one pair of colours. Kept in
// one place because a theme that differed between them would be visible the moment two are open.
internal static class WindowTheme
{
    // The dark flag of the backdrop is part of the theme too: without repeating this call after a
    // switch the acrylic keeps its old tint and the window looks dirty.
    public static bool ApplyBackdrop(Window window)
    {
        bool dark = Theme.AreWindowsDark();
        Dwm.ApplyAcrylic(new WindowInteropHelper(window).Handle, dark);
        return dark;
    }

    public static Color Foreground(bool dark) =>
        dark ? Colors.White : Color.FromRgb(0x11, 0x11, 0x11);

    public static Color Background(bool dark) =>
        dark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF7, 0xF7, 0xF7);
}

// The two windows that float over a game: the overlay and the volume OSD.
internal static class OverlayChrome
{
    // Neither clicks nor focus: they appear over games and full-screen video, and focus taken
    // from one of those means a minimised game. Kept above everything else while they are up.
    public static IntPtr MakeClickThrough(Window window)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;

        Win32.SetClickThrough(handle, true);
        Win32.ForceTopmost(handle);

        return handle;
    }
}

// Where to put a popup window: the volume OSD, the status window and the About box all meet the
// same thing, several monitors with their own scale, taskbar and place in one shared grid.
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

    // Puts the window against the taskbar side and centres it on the tray icon. The anchor is the
    // pointer as it was when the window opened, or re-placement would drag it after the mouse.
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

// A scale of separate segments — the one the macOS version dropped the system indicator for:
// drawing it by hand took the GPU load there from seventy per cent to eight.
public sealed class SegmentedLevelControl : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(SegmentedLevelControl),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.None, OnValueChanged));

    public static readonly DependencyProperty SegmentCountProperty = DependencyProperty.Register(
        nameof(SegmentCount), typeof(int), typeof(SegmentedLevelControl),
        new FrameworkPropertyMetadata(20, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CriticalLevelProperty = DependencyProperty.Register(
        nameof(CriticalLevel), typeof(double), typeof(SegmentedLevelControl),
        new FrameworkPropertyMetadata(90.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillBrushProperty = DependencyProperty.Register(
        nameof(FillBrush), typeof(Brush), typeof(SegmentedLevelControl),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EmptyBrushProperty = DependencyProperty.Register(
        nameof(EmptyBrush), typeof(Brush), typeof(SegmentedLevelControl),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CriticalBrushProperty = DependencyProperty.Register(
        nameof(CriticalBrush), typeof(Brush), typeof(SegmentedLevelControl),
        new FrameworkPropertyMetadata(Brushes.OrangeRed, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public int SegmentCount
    {
        get => (int)GetValue(SegmentCountProperty);
        set => SetValue(SegmentCountProperty, value);
    }

    // The value the scale turns red at.
    public double CriticalLevel
    {
        get => (double)GetValue(CriticalLevelProperty);
        set => SetValue(CriticalLevelProperty, value);
    }

    public Brush FillBrush
    {
        get => (Brush)GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    public Brush EmptyBrush
    {
        get => (Brush)GetValue(EmptyBrushProperty);
        set => SetValue(EmptyBrushProperty, value);
    }

    public Brush CriticalBrush
    {
        get => (Brush)GetValue(CriticalBrushProperty);
        set => SetValue(CriticalBrushProperty, value);
    }

    private int _drawnSegments = -1;
    private bool _drawnCritical;

    private static void OnValueChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var control = (SegmentedLevelControl)sender;

        int filled = control.FilledSegments;
        bool critical = control.IsCritical;

        if (filled == control._drawnSegments && critical == control._drawnCritical) return;

        control._drawnSegments = filled;
        control._drawnCritical = critical;
        control.InvalidateVisual();
    }

    private bool IsCritical => CriticalLevel > 0 && Value >= CriticalLevel;

    private int FilledSegments =>
        (int)Math.Round(Math.Clamp(Value, 0, 100) / 100.0 * SegmentCount);

    protected override void OnRender(DrawingContext context)
    {
        int segments = SegmentCount;
        if (segments <= 0 || ActualWidth <= 0 || ActualHeight <= 0) return;

        const double spacing = 1;
        double width = (ActualWidth - (segments - 1) * spacing) / segments;
        if (width <= 0) return;

        double radius = Math.Min(2, Math.Min(width / 2, ActualHeight / 2));
        int filled = FilledSegments;
        Brush active = IsCritical ? CriticalBrush : FillBrush;

        for (int i = 0; i < segments; i++)
        {
            var box = new Rect(i * (width + spacing), 0, width, ActualHeight);
            context.DrawRoundedRectangle(i < filled ? active : EmptyBrush, null, box, radius, radius);
        }
    }
}

// History chart: an area filled from zero to the value.
public sealed class SparklineControl : FrameworkElement
{
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points), typeof(IReadOnlyList<double>), typeof(SparklineControl),
        new FrameworkPropertyMetadata(Array.Empty<double>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(
        nameof(LineBrush), typeof(Brush), typeof(SparklineControl),
        new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AreaBrushProperty = DependencyProperty.Register(
        nameof(AreaBrush), typeof(Brush), typeof(SparklineControl),
        new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(
        nameof(GridBrush), typeof(Brush), typeof(SparklineControl),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double> Points
    {
        get => (IReadOnlyList<double>)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public Brush LineBrush
    {
        get => (Brush)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public Brush AreaBrush
    {
        get => (Brush)GetValue(AreaBrushProperty);
        set => SetValue(AreaBrushProperty, value);
    }

    public Brush GridBrush
    {
        get => (Brush)GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    // Reduces the history to the given number of columns, taking the largest value in each.
    internal static double[] Reduce(IReadOnlyList<double> points, int columns)
    {
        if (columns <= 0) return Array.Empty<double>();
        if (points.Count == 0) return Array.Empty<double>();
        if (points.Count <= columns) return System.Linq.Enumerable.ToArray(points);

        var result = new double[columns];
        for (int i = 0; i < columns; i++)
        {
            int from = (int)((long)i * points.Count / columns);
            int to = (int)((long)(i + 1) * points.Count / columns);
            if (to <= from) to = from + 1;

            double max = double.NegativeInfinity;
            for (int j = from; j < to && j < points.Count; j++)
                if (points[j] > max) max = points[j];

            result[i] = double.IsNegativeInfinity(max) ? 0 : max;
        }

        return result;
    }

    protected override void OnRender(DrawingContext context)
    {
        double width = ActualWidth;
        double height = ActualHeight;
        if (width <= 1 || height <= 1) return;

        // Quarter grid lines: without them forty per cent and sixty look the same on the chart.
        var grid = new Pen(GridBrush, 1);
        grid.Freeze();
        for (int i = 1; i < 4; i++)
        {
            double y = Math.Round(height * i / 4) + 0.5;
            context.DrawLine(grid, new Point(0, y), new Point(width, y));
        }

        double[] values = Reduce(Points, (int)Math.Max(2, Math.Round(width)));
        if (values.Length < 2) return;

        var figure = new PathFigure { StartPoint = new Point(0, height), IsClosed = true, IsFilled = true };
        var line = new PathFigure { StartPoint = new Point(0, Y(values[0], height)) };

        for (int i = 0; i < values.Length; i++)
        {
            double x = width * i / (values.Length - 1);
            var point = new Point(x, Y(values[i], height));

            figure.Segments.Add(new LineSegment(point, isStroked: false));
            if (i > 0) line.Segments.Add(new LineSegment(point, isStroked: true));
        }

        figure.Segments.Add(new LineSegment(new Point(width, height), isStroked: false));

        var area = new PathGeometry(new[] { figure });
        area.Freeze();
        context.DrawGeometry(AreaBrush, null, area);

        var stroke = new PathGeometry(new[] { line });
        stroke.Freeze();

        var pen = new Pen(LineBrush, 1.4);
        pen.Freeze();
        context.DrawGeometry(null, pen, stroke);
    }

    private static double Y(double value, double height) =>
        height - height * Math.Clamp(value, 0, 100) / 100.0;
}
