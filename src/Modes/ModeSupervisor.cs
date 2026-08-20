using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using Microsoft.Win32;
using SystemSpinnerX64.Configuration;
using SystemSpinnerX64.Devices;
using SystemSpinnerX64.Diagnostics;
using SystemSpinnerX64.Localization;
using SystemSpinnerX64.Monitoring;
using SystemSpinnerX64.Osd;
using SystemSpinnerX64.Platform;
using SystemSpinnerX64.Spinner;
using SystemSpinnerX64.Startup;
using SystemSpinnerX64.Tray;
using SystemSpinnerX64.ViewModels;
using SystemSpinnerX64.Views;

namespace SystemSpinnerX64.Modes;

/// <summary>
/// Two faces of one program and the switch between them.
///
/// While a full-screen application is in front of you this is an overlay: rows of numbers over
/// the picture and a frame counter. The moment it is gone this is a tray icon: an animation whose
/// speed follows the CPU, a status window behind it and the custom volume and brightness panel.
///
/// The switch is not about looks alone. In a game nobody needs an icon hidden behind a full-screen
/// window, nor the process list; outside a game nobody needs the ETW session of the frame counter,
/// and that is the most expensive part of the work. So the idle half is not hidden but stopped.
/// </summary>
public sealed class ModeSupervisor : IDisposable
{
    private readonly AppConfig _cfg;
    private readonly HardwareMonitor _hardware;
    private readonly MetricsService _metrics;
    private readonly FpsCounter _fps;

    private readonly OverlayViewModel _overlayModel;
    private readonly OverlayWindow _overlay;

    private readonly TrayIcon _tray;
    private readonly SpinnerAnimator _animator = new();

    private readonly DisplayManager _displays;
    private readonly MediaKeyMonitor _keys = new();
    private readonly OsdController _osd;

    private readonly DispatcherTimer _modeTimer = new();
    private readonly DispatcherTimer _fpsTimer = new();

    private StatsWindow? _stats;

    private bool _inGame;

    // The screen the overlay was last put on. Kept to notice the game moving to another monitor.
    private Win32.RECT? _gameScreen;

    private string _fpsApi = "";
    private DateTime _lastReadingsLog = DateTime.MinValue;

    public ModeSupervisor(AppConfig cfg, HardwareMonitor hardware)
    {
        _cfg = cfg;
        _hardware = hardware;

        _metrics = new MetricsService(cfg, hardware);
        _fps = new FpsCounter(cfg.Fps);

        _overlayModel = new OverlayViewModel(cfg.Warn, cfg.Fans.Extra.Count);
        _overlay = new OverlayWindow(cfg, _overlayModel);

        _tray = new TrayIcon(cfg);

        _displays = new DisplayManager(cfg.Osd);
        _osd = new OsdController(cfg);

        _animator.FrameReady += _tray.ShowFrame;

        _metrics.Updated += OnMetrics;
        _metrics.Failed += error => _overlay.ShowNotice(Text.ReadError(error));

        WireTray();

        _modeTimer.Interval = TimeSpan.FromSeconds(AppParameters.Polling.ModeCheckSeconds);
        _modeTimer.Tick += (_, _) => UpdateMode();

        _fpsTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(AppParameters.Polling.MinIntervalMs, cfg.UpdateIntervalMs));
        _fpsTimer.Tick += (_, _) => UpdateFps();

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>Brings up both halves and switches on the one that fits the moment.</summary>
    public void Start()
    {
        // The overlay window is created at once but only shown in a game: parsing the markup as
        // the game starts would cost the first seconds of readings.
        _overlay.Show();
        _overlay.Visibility = Visibility.Hidden;

        // The status window likewise: built now, shown on a click. Building it at the click is
        // the pause between pressing the icon and seeing the numbers.
        _stats = CreateStats();
        _stats.Prepare();

        _tray.ShowAutoStart(AutoStart.IsEnabled());

        ReloadSpinner();

        _displays.Refresh();
        _tray.ShowDisplays(_displays.DisplayNames);

        StartMediaKeys();

        _metrics.Start();
        _modeTimer.Start();

        // The first check right away rather than in a second: the app may have been started from a game.
        UpdateMode();

        Log.Info("started");
    }

    // --- Switching faces ---

    private void UpdateMode()
    {
        Win32.RECT screen = default;
        double scale = 1;

        bool inGame = _cfg.ShowOverlayInGames && Win32.TryFullscreenArea(out screen, out scale);

        if (inGame == _inGame)
        {
            if (_inGame)
            {
                // The game can move to another monitor without ever ceasing to be full screen,
                // and the panel goes with it.
                if (!screen.Equals(_gameScreen)) MoveOverlay(screen, scale);

                // Some games and launchers reset the window order — the panel is brought back on top.
                _overlay.KeepOnTop();
            }
            return;
        }

        if (inGame) MoveOverlay(screen, scale);
        else _gameScreen = null;

        _inGame = inGame;
        Log.Info(inGame
            ? "a full-screen application is active — overlay shown, spinner stopped"
            : "no full-screen application — overlay hidden, spinner running");

        if (inGame) EnterGame();
        else EnterDesktop();
    }

    private void MoveOverlay(Win32.RECT screen, double scale)
    {
        _gameScreen = screen;
        _overlay.MoveToScreen(screen, scale);
        Log.Info($"overlay on the screen at {screen.Left}×{screen.Top}, scale {scale:0.##}");
    }

    private void EnterGame()
    {
        // The status window over a game means a minimised game: it takes focus.
        HideStats();

        _overlay.ApplyLayout();
        _overlay.Visibility = Visibility.Visible;
        _overlay.KeepOnTop();

        _fps.Start();
        if (_fps.Status is { Length: > 0 } status)
        {
            _overlay.ShowNotice(status);
            Log.Warn($"frame counter: {status}");
        }

        _fpsTimer.Start();

        // The tray animation is invisible behind a full-screen window while it changes the icon up
        // to a hundred times a second — in a game that is pure waste.
        _animator.Stop();
    }

    private void EnterDesktop()
    {
        _overlay.Visibility = Visibility.Hidden;
        _overlay.ShowNotice("");

        _fpsTimer.Stop();
        _fps.Stop();
        _fpsApi = "";

        // The frame values went stale the moment the game left — they must not pass for current.
        _overlayModel.ApplyFps(null, null);

        if (_cfg.SpinOnDesktop) _animator.UpdateSpeed(_metrics.Latest.Readings.CpuLoad ?? 0);
        else _animator.Rewind();
    }

    // --- Readings ---

    private void OnMetrics(MetricsSnapshot snapshot)
    {
        Readings r = snapshot.Readings;

        if (_inGame) _overlayModel.Apply(r);

        if (!_inGame && _cfg.SpinOnDesktop) _animator.UpdateSpeed(r.CpuLoad ?? 0);

        _tray.ShowTip(Tip(r));

        _stats?.Apply(snapshot);

        LogReadings(r);
    }

    private string Tip(Readings r)
    {
        string cpu = r.CpuLoad is double load ? $"CPU {load:0} %" : "CPU —";
        string gpu = r.GpuLoad is double gpuLoad ? $"GPU {gpuLoad:0} %" : "";
        string memory = r.MemLoadPercent is double mem ? $"MEM {mem:0} %" : "";

        return string.Join("  ", new[] { cpu, gpu, memory }.Where(part => part.Length > 0));
    }

    private void UpdateFps()
    {
        _overlayModel.ApplyFps(_fps.Average(), _fps.FrameTimeMs());

        if (_fps.Api != _fpsApi) Log.Info($"frame source: {_fpsApi = _fps.Api}");
    }

    // Writes to the log what the panel shows — for checking against Task Manager or HWiNFO.
    private void LogReadings(Readings r)
    {
        if (AppParameters.Polling.ReadingsLogSeconds <= 0) return;
        if (DateTime.UtcNow - _lastReadingsLog < TimeSpan.FromSeconds(AppParameters.Polling.ReadingsLogSeconds)) return;
        _lastReadingsLog = DateTime.UtcNow;

        Log.Info($"readings: CPU {Show(r.CpuLoad)} % {Show(r.CpuTempC)} °C {Show(r.CpuPowerW)} W " +
                 $"{Show(r.CpuClockMhz)} MHz {Show(r.SysMemUsedGb, 1)} GB " +
                 $"{Show(r.CpuFanRpm)}/{Show(r.AioFanRpm)} RPM | " +
                 $"GPU {Show(r.GpuLoad)} % {Show(r.GpuTempC)} °C {Show(r.GpuPowerW)} W " +
                 $"{Show(r.GpuClockMhz)} MHz {Show(r.GpuMemUsedGb, 1)} GB {Show(r.GpuFanRpm)} RPM");

        static string Show(double? value, int decimals = 0) =>
            value is null ? "—" : value.Value.ToString("F" + decimals, CultureInfo.InvariantCulture);
    }

    // --- The tray icon ---

    private void ReloadSpinner()
    {
        SpinnerStyle style = SpinnerCatalog.Validate(_cfg.Spinner.Style);

        _animator.Invert = _cfg.Spinner.InvertRotation;
        _animator.Load(style, _cfg.Spinner.Effect,
                       System.Windows.Forms.SystemInformation.SmallIconSize.Width,
                       Theme.IsTaskbarLight());

        if (!_animator.HasFrames)
        {
            Log.Warn($"spinner \"{style.Name}\" has no frames — the app icon stays in the tray");
            return;
        }

        if (_inGame || !_cfg.SpinOnDesktop) _animator.Stop();
    }

    // --- The status window ---

    private void ToggleStats()
    {
        if (_stats is { IsVisible: true })
        {
            HideStats();
            return;
        }

        _stats ??= CreateStats();

        // The detailed poll starts before the window is shown: the first process load is
        // a difference against the previous poll, and without it the list would be empty.
        _metrics.Detailed = true;
        _stats.ShowStats();
        _stats.Apply(_metrics.Latest);
    }

    private StatsWindow CreateStats()
    {
        var window = new StatsWindow(_cfg);
        window.Hidden += () => _metrics.Detailed = false;
        return window;
    }

    private void HideStats()
    {
        _stats?.HideStats();
        _metrics.Detailed = false;
    }

    // --- Media keys ---

    private void StartMediaKeys()
    {
        if (!_cfg.Osd.Enabled)
        {
            Log.Info("media keys are left to Windows: Osd.Enabled = false");
            return;
        }

        _keys.Handler = OnMediaKey;
        _keys.StartVolumeKeys();

        HotKey? up = HotKey.Parse(_cfg.Osd.BrightnessUpKey, out string? upProblem);
        HotKey? down = HotKey.Parse(_cfg.Osd.BrightnessDownKey, out string? downProblem);

        foreach (string? problem in new[] { upProblem, downProblem })
            if (problem is { Length: > 0 }) Log.Warn($"Osd brightness key: {problem}");

        if (_keys.StartBrightnessKeys(up, down) is { Length: > 0 } taken)
        {
            Log.Warn($"brightness keys: {taken}");
            _tray.Notify(Text.HotKeyRefused(taken));
        }
    }

    private MediaKeyResult OnMediaKey(MediaKey key)
    {
        MediaKeyResult result;
        double value;
        OsdKind kind;

        switch (key)
        {
            case MediaKey.VolumeUp:
            case MediaKey.VolumeDown:
                result = _displays.AdjustVolume(key == MediaKey.VolumeUp, out value);
                kind = OsdKind.Volume;
                break;

            case MediaKey.Mute:
                result = _displays.ToggleMute(out value);
                kind = OsdKind.Volume;
                break;

            case MediaKey.BrightnessUp:
            case MediaKey.BrightnessDown:
                result = _displays.AdjustBrightness(key == MediaKey.BrightnessUp, out value);
                kind = OsdKind.Brightness;
                break;

            default:
                return MediaKeyResult.PassThrough;
        }

        // There was nothing to drive — the key goes to the system and it shows its own panel.
        // Showing ours on top of the system one would mean showing two.
        if (result == MediaKeyResult.Consumed) _osd.Show(value, kind);

        return result;
    }

    // --- The tray menu ---

    private void WireTray()
    {
        _tray.StatsRequested += ToggleStats;
        _tray.AutoStartToggled += SetAutoStart;
        _tray.SpinnerChanged += ReloadSpinner;

        _tray.IntervalChanged += () =>
        {
            _metrics.SetInterval(_cfg.UpdateIntervalMs);
            _fpsTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1000, _cfg.UpdateIntervalMs));
        };

        _tray.OverlayChanged += UpdateMode;

        _tray.OsdChanged += () =>
        {
            _displays.Refresh();
            _tray.ShowDisplays(_displays.DisplayNames);
        };

        _tray.DisplayRefreshRequested += () =>
        {
            _displays.Refresh();
            _tray.ShowDisplays(_displays.DisplayNames);
            _tray.Notify(Text.DisplaysFound(_displays.DisplayNames.Count));
        };

        // The language changes the titles of everything already built — the menu is rebuilt, and
        // the status window re-reads its captions the next time it is shown.
        _tray.LanguageChanged += () =>
        {
            _tray.Rebuild();
            _tray.ShowDisplays(_displays.DisplayNames);
        };

        _tray.ExitRequested += () => System.Windows.Application.Current.Shutdown();
    }

    // The state lives in Task Scheduler — after the attempt the tick is checked against what is there.
    private void SetAutoStart(bool enabled)
    {
        string? problem = enabled ? AutoStart.Enable() : AutoStart.Disable();

        _tray.Notify(problem is { Length: > 0 }
            ? Text.AutoStartFailed(problem)
            : enabled ? Text.AutoStartOn : Text.AutoStartOff);

        _tray.ShowAutoStart(AutoStart.IsEnabled());
    }

    // --- System changes ---

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        _overlay.Dispatcher.BeginInvoke(() =>
        {
            // A monitor was attached, detached or rescaled: the screen the panel was put on may be
            // gone. The saved one is dropped so that the next check places the panel again.
            _gameScreen = null;
            _overlay.ForgetScreen();
            _overlay.ApplyLayout();
            UpdateMode();

            // The status window can be left hanging over the edge of what remains.
            _stats?.Reposition();

            _displays.Refresh();
            _tray.ShowDisplays(_displays.DisplayNames);
        });

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        // General arrives on a theme change too: there is no separate event for it.
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color)) return;

        _overlay.Dispatcher.BeginInvoke(() =>
        {
            ReloadSpinner();
            _tray.ApplyTheme();
            _osd.ApplyTheme();
            _stats?.ApplyTheme();
        });
    }

    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

        _modeTimer.Stop();
        _fpsTimer.Stop();

        _stats?.Close();
        _overlay.Close();

        _keys.Dispose();
        _osd.Dispose();
        _displays.Dispose();
        DdcQueue.Stop();

        _animator.Dispose();
        _tray.Dispose();

        _metrics.Dispose();
        _fps.Dispose();
        _hardware.Dispose();

        Log.Finish("exit from the tray menu");
    }
}
