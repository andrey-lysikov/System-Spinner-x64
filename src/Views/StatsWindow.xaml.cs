//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using SystemSpinnerX64.Configuration;
using SystemSpinnerX64.Localization;
using SystemSpinnerX64.Monitoring;
using SystemSpinnerX64.Platform;

namespace SystemSpinnerX64.Views;

// The status window — what a click on the menu bar icon opens in the macOS version.
public partial class StatsWindow : Window
{
    private readonly AppConfig _cfg;

    private MetricsSnapshot _latest = MetricsSnapshot.Empty;

    // Where the pointer was when the window was opened — that is where the tray icon is. Kept
    // because the window is placed again on every height change, and by then the pointer is
    // elsewhere: the page file arrives a poll later than the rest and adds a row.
    private System.Drawing.Point _anchor;
    private DetailWindow? _detail;
    private bool _prepared;

    // The window was hidden — time to switch the detailed poll off.
    public event Action? Hidden;

    public StatsWindow(AppConfig cfg)
    {
        InitializeComponent();

        // Arabic reads right to left: the whole window is mirrored rather than each label,
        // or the numbers would end up on the wrong side of their captions.
        FlowDirection = Text.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        _cfg = cfg;

        ApplyTheme();
        ApplyLabels();

        // A click elsewhere closes the window, like the macOS popover. The check is deferred: at
        // the moment of the event the chart window has not become active yet, and without the
        // delay a click on it would close both.
        Deactivated += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() =>
            {
                if (IsActive || _detail is { IsActive: true }) return;
                HideStats();
            }));
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Dwm.ApplyAcrylic(new WindowInteropHelper(this).Handle, Theme.AreWindowsDark());
    }

    // Re-reads the theme and repaints the window.
    public void ApplyTheme()
    {
        bool dark = Theme.AreWindowsDark();

        // The dark flag of the backdrop is part of the theme too: without repeating this call the
        // acrylic would keep its old tint and the window would look dirty on a light theme.
        Dwm.ApplyAcrylic(new WindowInteropHelper(this).Handle, dark);

        Color foreground = dark ? Colors.White : Color.FromRgb(0x11, 0x11, 0x11);
        Color background = dark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF7, 0xF7, 0xF7);

        Shell.Background = new SolidColorBrush(background) { Opacity = 0.88 };
        Shell.BorderBrush = new SolidColorBrush(foreground) { Opacity = 0.12 };

        // The text colour is set once for the whole content: the inherited property reaches every
        // caption, so there is no reason to paint them one by one.
        System.Windows.Documents.TextElement.SetForeground(Body, new SolidColorBrush(foreground));

        var fill = new SolidColorBrush(foreground) { Opacity = 0.85 };
        var empty = new SolidColorBrush(foreground) { Opacity = 0.14 };
        var critical = new SolidColorBrush(Color.FromRgb(0xFF, 0x6A, 0x52));
        var glyph = new SolidColorBrush(foreground) { Opacity = 0.55 };
        var line = new SolidColorBrush(foreground) { Opacity = 0.12 };

        foreach (SegmentedLevelControl level in Levels())
        {
            level.FillBrush = fill;
            level.EmptyBrush = empty;
            level.CriticalBrush = critical;
        }

        foreach (Path chart in new[] { CpuChartGlyph, MemChartGlyph }) chart.Stroke = glyph;
        foreach (Separator separator in new[] { Line1, Line2, Line3 }) separator.Background = line;

        _detail?.ApplyTheme();
    }

    private IEnumerable<SegmentedLevelControl> Levels() =>
        new[] { CpuLevel, GpuLevel, CpuTempLevel, GpuTempLevel, MemLevel, GpuMemLevel, SwapLevel };

    private void ApplyLabels()
    {
        // The same thresholds as the in-game panel: no reason to keep a second set of numbers
        // for the same hardware. Temperatures are converted to a share of the scale, which runs
        // to a hundred degrees; the memory ones are already percentages.
        CpuTempLevel.CriticalLevel = _cfg.Warn.CpuTemp > 0 ? _cfg.Warn.CpuTemp / AppParameters.Layout.TemperatureScale * 100 : 0;
        GpuTempLevel.CriticalLevel = _cfg.Warn.GpuTemp > 0 ? _cfg.Warn.GpuTemp / AppParameters.Layout.TemperatureScale * 100 : 0;

        MemLevel.CriticalLevel = _cfg.Warn.SysMem;
        GpuMemLevel.CriticalLevel = _cfg.Warn.GpuMem;
        SwapLevel.CriticalLevel = _cfg.Warn.SwapMem;

        // Load has no threshold in the config: a highlight that is on all the time says nothing.
        CpuLevel.CriticalLevel = _cfg.Warn.CpuUsage;
        GpuLevel.CriticalLevel = _cfg.Warn.GpuUsage;
    }

    // Shows the window by the taskbar and asks for the detailed poll.
    public void ShowStats()
    {
        ApplyLabels();
        Apply(_latest);

        // The height is only known after the first layout — rows without a sensor are hidden. So:
        // shown off screen and transparent, then placed, or it would flash and jump to the tray.
        Opacity = 0;
        Left = AppParameters.Layout.OffScreen;
        Top = AppParameters.Layout.OffScreen;
        _anchor = ScreenPlacement.Pointer();

        Show();
        UpdateLayout();
        Place();

        Activate();
        Opacity = 1;
    }

    // Builds the window without showing it: the handle, the first layout and the backdrop.
    public void Prepare()
    {
        if (IsVisible || _prepared) return;
        _prepared = true;

        Opacity = 0;
        Left = AppParameters.Layout.OffScreen;
        Top = AppParameters.Layout.OffScreen;

        Show();
        UpdateLayout();
        Hide();
    }

    public void HideStats()
    {
        if (!IsVisible) return;

        _detail?.HideDetail();
        Hide();
        Hidden?.Invoke();
    }

    // New readings. Called only while the window is open.
    public void Apply(MetricsSnapshot snapshot)
    {
        _latest = snapshot;
        Readings r = snapshot.Readings;

        CpuTitle.Text = Headline(Text.StatsCpu, r.CpuLoad, "%");
        CpuLevel.Value = r.CpuLoad ?? 0;
        Note(CpuNote, Join(Unit(r.CpuClockMhz, "MHz"), Unit(r.CpuPowerW, "W")));

        // Fan speeds get a line of their own with tags: there can be three or four of them, and
        // without a tag there is no telling the cooler from the pump. The card fan stays with the
        // card: there is only one there, and nothing to tag.
        Note(FanNote, Fans(r));

        GpuTitle.Text = Headline(Text.StatsGpu, r.GpuLoad, "%");
        GpuLevel.Value = r.GpuLoad ?? 0;

        // Integrated graphics have nothing of their own to put under the bar: the clock that does
        // arrive belongs to the processor package and would read as the card own. Power or a fan
        // means a card of its own even when the memory sensor stays silent.
        bool ownCard = r.GpuHasOwnMemory || r.GpuPowerW is not null || r.GpuFanRpm is not null;

        Note(GpuNote, ownCard
            ? Join(Unit(r.GpuClockMhz, "MHz"), Unit(r.GpuPowerW, "W"), Unit(r.GpuFanRpm, Text.Rpm))
            : "");

        Row(CpuTempTitle, CpuTempLevel, Text.StatsCpuTemp, r.CpuTempC, "°C", Percent(r.CpuTempC));
        Row(GpuTempTitle, GpuTempLevel, Text.StatsGpuTemp, r.GpuTempC, "°C", Percent(r.GpuTempC));

        MemTitle.Text = Headline(Text.StatsMemory, r.MemLoadPercent, "%");
        MemLevel.Value = r.MemLoadPercent ?? 0;

        // Shared memory means no row: the same gigabytes already stand above, under Memory.
        Row(GpuMemTitle, GpuMemLevel, Text.StatsGpuMemory,
            r.GpuHasOwnMemory ? r.GpuMemLoadPercent : null, "%", r.GpuMemLoadPercent ?? 0);

        // The page file is read a poll later than everything else, and a machine can have none at
        // all. The row stays either way, at zero until a figure arrives: a scale that comes and
        // goes changes the height of the window, and the window jumps as it is opening.
        double swap = r.SwapLoadPercent ?? 0;
        SwapTitle.Text = Headline(Text.StatsSwap, swap, "%");
        SwapLevel.Value = swap;

        NetAddress.Text = snapshot.Network.Address is { Length: > 0 } address
            ? $"ip: {address}"
            : Text.StatsNoAddress;
        NetSpeed.Text = $"▼ {snapshot.Network.Inbound.Describe()}  |  ▲ {snapshot.Network.Outbound.Describe()}";

        _detail?.Apply(snapshot);
    }

    // A row with a bar that may not exist at all: no sensor, no row.
    private static void Row(TextBlock title, SegmentedLevelControl level,
                            string caption, double? value, string unit, double share)
    {
        if (value is null)
        {
            title.Visibility = Visibility.Collapsed;
            level.Visibility = Visibility.Collapsed;
            return;
        }

        title.Visibility = Visibility.Visible;
        level.Visibility = Visibility.Visible;
        title.Text = Headline(caption, value, unit);
        level.Value = share;
    }

    private static void Note(TextBlock note, string? text)
    {
        note.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        note.Text = text ?? "";
    }

    private static string Headline(string caption, double? value, string unit) =>
        value is null
            ? $"{caption} — {Text.StatsWaiting}"
            : $"{caption} {value.Value.ToString("0", CultureInfo.InvariantCulture)} {unit}";

    // The temperature scale is a share of a hundred degrees: unlike load it has no percentage.
    private static double Percent(double? temperature) =>
        temperature is null ? 0 : Math.Clamp(temperature.Value / AppParameters.Layout.TemperatureScale * 100, 0, 100);

    private static string? Unit(double? value, string unit, int decimals = 0) =>
        value is null ? null : value.Value.ToString("F" + decimals, CultureInfo.InvariantCulture) + " " + unit;

    private static string? Join(params string?[] parts)
    {
        string joined = string.Join(" · ", parts.Where(p => !string.IsNullOrEmpty(p)));
        return joined.Length == 0 ? null : joined;
    }

    // Fan speeds with tags: "CPU 903 · AIO 2210 · SYS 1200".
    private static string? Fans(Readings r)
    {
        var found = new List<(string Tag, double Rpm)>();

        if (r.CpuFanRpm is double cpu) found.Add(("CPU", cpu));
        if (r.AioFanRpm is double aio) found.Add(("AIO", aio));

        for (int i = 0; i < r.ExtraFanRpm.Count; i++)
        {
            if (r.ExtraFanRpm[i] is not double extra) continue;

            // One extra fan is just SYS; several get a number, or they cannot be told apart.
            found.Add((r.ExtraFanRpm.Count > 1 ? $"SYS {i + 1}" : "SYS", extra));
        }

        if (found.Count == 0) return null;

        // All zeros means the fans are stopped on purpose, and that is what it says: "0 rpm" looks
        // like a silent sensor when the hardware is simply not warm.
        if (found.All(f => f.Rpm < 1)) return Text.StatsFansStopped;

        return Text.StatsFans(string.Join(" · ", found.Select(
            f => $"{f.Tag} {f.Rpm.ToString("0", CultureInfo.InvariantCulture)}")));
    }

    // --- The chart window ---

    private void OnCpuChartClicked(object sender, MouseButtonEventArgs e) => ShowDetail(DetailKind.Cpu);

    private void OnMemChartClicked(object sender, MouseButtonEventArgs e) => ShowDetail(DetailKind.Memory);

    private void ShowDetail(DetailKind kind)
    {
        _detail ??= new DetailWindow(_cfg) { Owner = this };

        // Clicking the same icon again closes it: this is a toggle, not a button, and there is no
        // other way to close it without moving the mouse away.
        if (_detail.IsVisible && _detail.Kind == kind)
        {
            _detail.HideDetail();
            return;
        }

        _detail.ShowDetail(kind, _latest);
    }

    // Places the window again — after a screen was attached, detached or rescaled.
    public void Reposition() => Place();

    private void Place()
    {
        if (!IsVisible) return;

        ScreenPlacement.PutNearTray(this, _anchor, AppParameters.Layout.StatusGap);

        // The chart window is aligned to the bottom edge of this one, so it moves with it.
        _detail?.Reposition();
    }

    // The window was moved to a screen at another scale — its size in pixels changed with it, so
    // the corner it was placed by is no longer where it was put.
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Place();
    }

    // The height changes as it runs: a row without a sensor hides, fan speeds appear, the address
    // takes two lines instead of one.
    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        if (info.HeightChanged) Place();
    }
}
