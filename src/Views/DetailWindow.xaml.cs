using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SystemSpinnerX64.Configuration;
using SystemSpinnerX64.Localization;
using SystemSpinnerX64.Monitoring;
using SystemSpinnerX64.Platform;

namespace SystemSpinnerX64.Views;

/// <summary>What the chart window shows: processor load history or used memory.</summary>
public enum DetailKind
{
    Cpu,
    Memory
}

/// <summary>
/// History and processes — the second window, the one that slides out from under the chart icon
/// in the macOS version. A chart of the last quarter of an hour and the list of the hungriest.
///
/// A separate window rather than a section of the first: the process list costs a walk of the
/// process table with icons read from disk, and a window opened for a second must not pay that.
/// </summary>
public partial class DetailWindow : Window
{
    private readonly AppConfig _cfg;

    // Process icons are converted to WPF brushes once each: the conversion copies the bitmap, and
    // doing that every second for a dozen rows is noticeable work for nothing.
    private readonly Dictionary<int, ImageSource?> _icons = new();

    public DetailWindow(AppConfig cfg)
    {
        InitializeComponent();

        // Arabic reads right to left: the whole window is mirrored rather than each label,
        // or the numbers would end up on the wrong side of their captions.
        FlowDirection = Text.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        _cfg = cfg;

        ApplyTheme();

        // A click elsewhere closes both windows at once: they opened as one thing. The check is
        // deferred — at the moment of the event the status window has not become active yet.
        Deactivated += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(CloseOnClickAway));
    }

    /// <summary>
    /// A click anywhere outside this window closes it, and if it did not land on the status
    /// window either, that one closes too — the pair opened as one thing and goes away as one.
    ///
    /// Which window took the click is read from the status window rather than from where focus
    /// ends up afterwards: hiding an owned window hands activation back to its owner, so by then
    /// the owner looks active whatever was clicked.
    /// </summary>
    private void CloseOnClickAway()
    {
        // The check is deferred to idle, and by then the click may have come back here.
        if (IsActive) return;

        bool clickedTheStatusWindow = Owner is { IsActive: true };

        HideDetail();
        if (!clickedTheStatusWindow) (Owner as StatsWindow)?.HideStats();
    }

    /// <summary>What is shown now. A click on the same icon closes the window by this.</summary>
    public DetailKind Kind { get; private set; } = DetailKind.Cpu;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Dwm.ApplyAcrylic(new WindowInteropHelper(this).Handle, Theme.AreWindowsDark());
    }

    public void ApplyTheme()
    {
        bool dark = Theme.AreWindowsDark();

        // The dark flag of the backdrop is part of the theme too: without repeating this call the
        // acrylic would keep its old tint.
        Dwm.ApplyAcrylic(new WindowInteropHelper(this).Handle, dark);

        Color foreground = dark ? Colors.White : Color.FromRgb(0x11, 0x11, 0x11);
        Color background = dark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF7, 0xF7, 0xF7);

        Shell.Background = new SolidColorBrush(background) { Opacity = 0.90 };
        Shell.BorderBrush = new SolidColorBrush(foreground) { Opacity = 0.12 };
        System.Windows.Documents.TextElement.SetForeground(Body, new SolidColorBrush(foreground));

        Chart.GridBrush = new SolidColorBrush(foreground) { Opacity = 0.10 };
        Chart.AreaBrush = new SolidColorBrush(foreground) { Opacity = 0.18 };
        Chart.LineBrush = new SolidColorBrush(foreground) { Opacity = 0.55 };

        var axis = new SolidColorBrush(foreground) { Opacity = 0.45 };
        foreach (System.Windows.Controls.TextBlock label in new[] { Axis100, Axis75, Axis50, Axis25, Axis0 })
            label.Foreground = axis;
    }

    /// <summary>Shows the window next to the status window — its owner.</summary>
    public void ShowDetail(DetailKind kind, MetricsSnapshot snapshot)
    {
        Kind = kind;

        Caption.Text = kind == DetailKind.Cpu ? Text.DetailCpu : Text.DetailMemory;
        HeadPid.Text = Text.DetailColumnPid;
        HeadName.Text = Text.DetailColumnName;
        HeadUsage.Text = Text.DetailColumnUsage;

        // Shown before it is filled: Apply() does nothing on a hidden window — otherwise it would
        // build the chart and the process list while nobody is looking. The other order would
        // leave the window empty until the next poll, a whole second.
        //
        // It appears off screen and transparent: otherwise Windows opens it wherever it sees fit
        // and it jumps to its place in front of the user.
        Opacity = 0;
        Left = AppParameters.Layout.OffScreen;
        Top = AppParameters.Layout.OffScreen;
        Show();

        Apply(snapshot);
        UpdateLayout();
        Place();

        Activate();
        Opacity = 1;
    }

    /// <summary>Follows the status window when that one is placed again.</summary>
    public void Reposition()
    {
        if (IsVisible) Place();
    }

    public void HideDetail()
    {
        if (IsVisible) Hide();
    }

    /// <summary>New readings. Called only while the window is open.</summary>
    public void Apply(MetricsSnapshot snapshot)
    {
        if (!IsVisible) return;

        Chart.Points = Kind == DetailKind.Cpu ? snapshot.CpuHistory : snapshot.MemoryHistory;

        IEnumerable<ProcessUsage> sorted = Kind == DetailKind.Cpu
            ? snapshot.Processes.OrderByDescending(p => p.CpuPercent)
            : snapshot.Processes.OrderByDescending(p => p.MemoryMb);

        Processes.ItemsSource = sorted.Select(p => new
        {
            p.Pid,
            Icon = IconFor(p),
            p.Name,
            Usage = Kind == DetailKind.Cpu
                ? p.CpuPercent.ToString("0.0", CultureInfo.InvariantCulture) + " %"
                : p.MemoryMb.ToString("0", CultureInfo.InvariantCulture) + " MB"
        }).ToList();
    }

    private ImageSource? IconFor(ProcessUsage process)
    {
        if (_icons.TryGetValue(process.Pid, out ImageSource? cached)) return cached;

        ImageSource? source = null;
        try
        {
            if (process.Icon is not null)
            {
                source = Imaging.CreateBitmapSourceFromHIcon(
                    process.Icon.Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"the icon of process {process.Pid} was not converted: {ex.Message}");
        }

        // The dictionary does not grow without bound: the list is short and processes come and go.
        if (_icons.Count > AppParameters.Layout.ChartIconCache) _icons.Clear();

        _icons[process.Pid] = source;
        return source;
    }

    // To the left of the status window and along its bottom edge — to the right of that one is
    // the screen edge. If it does not fit on the left, it goes to the right of it instead. The
    // screen is taken from the status window, not from the pointer: by then the mouse may already
    // be on the other monitor.
    private void Place()
    {
        if (Owner is not null) ScreenPlacement.PutBeside(this, Owner, AppParameters.Layout.ChartGap);
    }
}
