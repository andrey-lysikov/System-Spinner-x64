//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using SystemSpinnerX64.Configuration;
using SystemSpinnerX64.Diagnostics;
using SystemSpinnerX64.Platform;
using SystemSpinnerX64.Views;

namespace SystemSpinnerX64.Osd;

// The custom volume and brightness panel: the same bar as in the macOS version, only the blur comes
// from Windows 11 rather than from Liquid Glass.
public partial class OsdWindow : Window
{
    private readonly AppConfig _cfg;

    private IntPtr _handle;
    private bool _acrylic;
    private bool _dark;

    public OsdWindow(AppConfig cfg)
    {
        InitializeComponent();
        _cfg = cfg;
        Opacity = 0;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _handle = new WindowInteropHelper(this).Handle;

        // The panel takes neither clicks nor focus: it appears over games and full-screen video,
        // and focus taken from those means a minimised game.
        Win32.SetClickThrough(_handle, true);
        Win32.ForceTopmost(_handle);

        ApplyTheme();
    }

    // Re-reads the theme and repaints the window.
    public void ApplyTheme()
    {
        _dark = Theme.AreWindowsDark();
        _acrylic = Dwm.ApplyAcrylic(_handle, _dark);

        Color foreground = _dark ? Colors.White : Colors.Black;

        // Without acrylic the window is its own background: a translucent film over wallpaper does
        // not read, so the colour is noticeably denser.
        Color background = _dark ? Color.FromRgb(0x1C, 0x1C, 0x1C) : Color.FromRgb(0xF3, 0xF3, 0xF3);
        Pill.Background = new SolidColorBrush(background) { Opacity = _acrylic ? 0.30 : 0.92 };
        Pill.BorderBrush = new SolidColorBrush(foreground) { Opacity = 0.10 };

        Glyph.Foreground = new SolidColorBrush(foreground) { Opacity = 0.85 };
        BarTrack.Background = new SolidColorBrush(foreground) { Opacity = 0.25 };
        BarFill.Background = new SolidColorBrush(foreground);

        _scaleForeground = foreground;
        _scaleSteps = -1; // the ticks are redrawn on the next show
    }

    private Color _scaleForeground = Colors.White;
    private int _scaleSteps = -1;

    // Shows the panel with a value in percent.
    public void Show(double percent, OsdKind kind, int steps)
    {
        Glyph.Text = GlyphFor(kind, percent);
        DrawScale(steps);

        // The scale is built from the actual markup width: the window can be stretched by the
        // screen scale, and the ticks have to line up with the fill exactly.
        double width = BarTrack.ActualWidth > 0 ? BarTrack.ActualWidth : AppParameters.Layout.OsdWidth - 92;
        BarFill.Width = Math.Max(0, width * Math.Clamp(percent, 0, 100) / 100.0);

        // Shown before it is placed, and it is placed by its window handle — which the first show
        // is what creates. Nothing flashes: the panel comes up transparent and is only made
        // visible once it stands where it belongs.
        if (!IsVisible)
        {
            base.Show();
            Win32.ForceTopmost(_handle);
        }

        PlaceOnActiveScreen();
        Opacity = 1;
    }

    // Hides the panel. The window stays built.
    public void HideOsd()
    {
        if (!IsVisible) return;

        Opacity = 0;
        Hide();
    }

    // Glyphs from the system Segoe Fluent Icons font — the ones Windows draws in its own panel.
    private static string GlyphFor(OsdKind kind, double percent) => kind switch
    {
        OsdKind.Brightness => "",
        _ => percent switch
        {
            <= 0 => "",   // sound off
            < 33 => "",
            < 66 => "",
            _ => ""
        }
    };

    // The ticks are redrawn only when their number changes: drawing thirty-two of them on every
    // key press would be for nothing.
    private void DrawScale(int steps)
    {
        steps = Math.Clamp(steps,
                           AppParameters.Limits.MinAdjustmentSteps,
                           AppParameters.Limits.MaxAdjustmentSteps);
        if (steps == _scaleSteps) return;
        _scaleSteps = steps;

        Scale.Children.Clear();

        double width = BarTrack.ActualWidth > 0 ? BarTrack.ActualWidth : AppParameters.Layout.OsdWidth - 92;
        var brush = new SolidColorBrush(_scaleForeground) { Opacity = 0.8 };

        for (int i = 0; i <= steps; i++)
        {
            // Every fourth tick is longer: the scale reads without counting the small ones.
            double height = i % 4 == 0 ? 6 : 3;

            var tick = new Rectangle
            {
                Width = 1,
                Height = height,
                Fill = brush
            };

            Canvas.SetLeft(tick, Math.Round(width * i / steps));
            Canvas.SetTop(tick, 7 - height);
            Scale.Children.Add(tick);
        }
    }

    // Puts the panel at the bottom of the screen the pointer is on.
    private void PlaceOnActiveScreen() => ScreenPlacement.PutAboveBottom(this, AppParameters.Osd.BottomInset);
}
