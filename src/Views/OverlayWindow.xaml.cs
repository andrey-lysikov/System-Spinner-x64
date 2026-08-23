//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using SystemSpinnerX64.Configuration;
using SystemSpinnerX64.Diagnostics;
using SystemSpinnerX64.Monitoring;
using SystemSpinnerX64.Platform;
using SystemSpinnerX64.ViewModels;

namespace SystemSpinnerX64.Views;

// The panel over a game: CPU, GPU and FPS rows.
public partial class OverlayWindow : Window
{
    private readonly AppConfig _cfg;
    private readonly OverlayViewModel _vm;

    private IntPtr _handle;

    // The screen the game is on, in pixels, and its scale. Null until something goes full screen.
    private Win32.RECT? _screen;
    private double _screenScale = 1;

    public OverlayWindow(AppConfig cfg, OverlayViewModel vm)
    {
        InitializeComponent();

        _cfg = cfg;
        _vm = vm;
        DataContext = vm;

        ApplyAppearance();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _handle = new WindowInteropHelper(this).Handle;
        Win32.SetClickThrough(_handle, true);
        Win32.ForceTopmost(_handle);

        // The screen scale is only known once the window exists, so the layout is set here.
        ApplyLayout();

        double font = TextBlock.GetFontSize(Readout);
        Log.Info($"overlay: font {font:0.#}, corner {Left:0}×{Top:0}");
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        ApplyLayout();
    }

    // Brings the panel back on top: some games and launchers reset the window order.
    public void KeepOnTop()
    {
        if (_handle != IntPtr.Zero) Win32.ForceTopmost(_handle);
    }

    // --- Layout ---

    // Recomputes the font size and position.
    public void ApplyLayout()
    {
        ApplyFontSize();
        PlaceAtCorner();
    }

    private void ApplyFontSize()
    {
        double percent = Math.Clamp(_cfg.Appearance.FontScalePercent,
                                    AppParameters.Limits.MinFontScalePercent,
                                    AppParameters.Limits.MaxFontScalePercent) / 100.0;
        double size = Math.Clamp(AppParameters.Overlay.BaseFontDip * percent,
                                 AppParameters.Limits.MinFontDip,
                                 AppParameters.Limits.MaxFontDip);

        Readout.SetValue(TextBlock.FontSizeProperty, size);

        double unitSize = Math.Round(size * Math.Clamp(_cfg.Appearance.UnitSizePercent,
                                                     AppParameters.Limits.MinUnitSizePercent,
                                                     AppParameters.Limits.MaxUnitSizePercent) / 100.0, 1);
        Resources["SmallFontSize"] = unitSize;

        // A larger size leaves more room below the baseline — the unit labels are lifted by the
        // difference, or rows aligned to the bottom would sit on different lines.
        double unitGap = size * 0.12;
        Resources["UnitMargin"] = new Thickness(unitGap, 0, 0, Descent(size) - Descent(unitSize));

        // Zeros rather than any digits: in Impact they are the widest, so the margin is guaranteed.
        _vm.Layout(
            slots => Measure(new string('0', slots), size),
            unit => Measure(unit, unitSize),
            unitGap,
            columnGap: size * 0.55);
    }

    // Measured in that very font: Impact is not monospaced, and "RPM/AIO" is twice as wide as "%".
    private double Measure(string text, double fontSize) =>
        Format(text, fontSize).WidthIncludingTrailingWhitespace;

    private double Descent(double fontSize)
    {
        FormattedText formatted = Format("0", fontSize);
        return formatted.Height - formatted.Baseline;
    }

    private FormattedText Format(string text, double fontSize)
    {
        var face = (FontFamily)FindResource("Face");
        var typeface = new Typeface(face, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        return new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }

    // The screen the panel belongs on: the one the game covers. Work area in pixels, and the
    // scale of that screen — monitors can differ in it.
    internal void MoveToScreen(Win32.RECT work, double scale)
    {
        _screen = work;
        _screenScale = scale > 0 ? scale : 1;
        ApplyLayout();
    }

    // Forgets the screen it was put on — that screen may have just been detached.
    public void ForgetScreen() => _screen = null;

    // Counted from the work area rather than the screen, or the panel slides under the taskbar.
    private void PlaceAtCorner()
    {
        double margin = Math.Clamp(_cfg.Appearance.Margin,
                                     AppParameters.Limits.MinOverlayMargin,
                                     AppParameters.Limits.MaxOverlayMargin);

        // Nothing is full screen yet — the main screen will do until something is.
        if (_screen is not Win32.RECT work || _handle == IntPtr.Zero)
        {
            Left = SystemParameters.WorkArea.Left + margin;
            Top = SystemParameters.WorkArea.Top + margin;
            return;
        }

        // Placed in pixels rather than by Left/Top: those are read in the units of the screen the
        // window is on now, and with two monitors at different scales that is the screen it is
        // leaving. The move brings a scale change of its own, and the layout is redone then.
        int inset = (int)Math.Round(margin * _screenScale);

        Win32.SetWindowPos(_handle, IntPtr.Zero, work.Left + inset, work.Top + inset, 0, 0,
            Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    }

    // --- Look ---

    // The resources are swapped at run time — the markup references them through DynamicResource.
    private void ApplyAppearance()
    {
        AppearanceConfig look = _cfg.Appearance;

        Resources["Face"] = new FontFamily(look.FontFamily);

        Color text = ParseColor(look.TextColor, Colors.White, nameof(look.TextColor));
        Resources["Value"] = Paint(text, AppParameters.Overlay.ValueDensity);
        Resources["Tag"] = Paint(text, AppParameters.Overlay.TagDensity);
        Resources["Unit"] = Paint(text, AppParameters.Overlay.UnitDensity);

        Color warn = ParseColor(_cfg.Warn.Color, Color.FromRgb(0xFF, 0x6A, 0x52), nameof(_cfg.Warn.Color));
        Resources["Warn"] = Paint(warn, AppParameters.Overlay.WarnDensity);

        Readout.Opacity = Math.Clamp(look.TextOpacity,
                                        AppParameters.Limits.MinTextOpacity,
                                        AppParameters.Limits.MaxTextOpacity);

        // Without a backdrop this is the only thing separating the digits from bright frames.
        Readout.Effect = look.ShadowBlur <= 0
            ? null
            : new DropShadowEffect
            {
                Color = Colors.Black,
                Opacity = Math.Clamp(look.ShadowOpacity, 0.0, 1.0),
                BlurRadius = Math.Clamp(look.ShadowBlur,
                                          AppParameters.Limits.MinShadowBlur,
                                          AppParameters.Limits.MaxShadowBlur),
                ShadowDepth = 0
            };

        Shell.Background = look.ShowPanel
            ? Paint(ParseColor(look.PanelColor, Color.FromRgb(0x0B, 0x0D, 0x12), nameof(look.PanelColor)),
                    Math.Clamp(look.PanelOpacity, 0.0, 1.0))
            : null;
    }

    private static SolidColorBrush Paint(Color color, double opacity)
    {
        var brush = new SolidColorBrush(color) { Opacity = opacity };
        brush.Freeze();
        return brush;
    }

    // A malformed colour is no reason not to start: take the default and say so in the log.
    private static Color ParseColor(string value, Color fallback, string parameter)
    {
        try
        {
            if (ColorConverter.ConvertFromString(value) is Color parsed) return parsed;
        }
        catch (Exception ex)
        {
            Log.Warn($"Appearance.{parameter}: \"{value}\" is not a colour ({ex.Message}), using the default");
            return fallback;
        }

        Log.Warn($"Appearance.{parameter}: \"{value}\" is not a colour, using the default");
        return fallback;
    }

    // The notice line at the bottom: sensor errors, frame counter state.
    public void ShowNotice(string text)
    {
        _vm.Notice = text;
        NoticeText.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
    }
}
