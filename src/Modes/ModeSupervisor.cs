//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
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

// Two faces of one program and the switch between them.
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
    private readonly DispatcherTimer _updateTimer = new();
    private readonly DispatcherTimer _displayTimer = new();
    private readonly DispatcherTimer _wakeTimer = new();

    private StatsWindow? _stats;

    private bool _inGame;

    // The screen the overlay was last put on. Kept to notice the game moving to another monitor.
    private Win32.RECT? _gameScreen;

    private string _fpsApi = "";
    private string _audioDevice = "";

    // How many screens are attached. Read when the screens are looked at rather than every poll:
    // asking the system is a walk of the monitor list.
    private int _screenCount = 1;
    private DateTime _lastRescan = DateTime.MinValue;

    // Why the screens are being asked again, and how many more times to ask while they stay silent.
    private string _settleReason = "";
    private int _settleTries;
    private DateTime _lastReadingsLog = DateTime.MinValue;

    public ModeSupervisor(AppConfig cfg, HardwareMonitor hardware)
    {
        _cfg = cfg;
        _hardware = hardware;

        _metrics = new MetricsService(cfg, hardware);
        _fps = new FpsCounter();

        _overlayModel = new OverlayViewModel(cfg.Warn, cfg.Appearance.Rows, cfg.Fans.Extra.Count);
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

        // The screens are looked at again now and then: the sound device every few minutes, the
        // whole list once an hour. Neither has an event of its own that Windows raises here.
        _displayTimer.Interval = AppParameters.Displays.AudioCheckPeriod;
        _displayTimer.Tick += (_, _) => WatchDisplays();

        // A screen that has just changed mode — woken up, come out of HDR — answers nothing over
        // DDC for a few seconds, and one asked too early is written off as unable until the hourly
        // look. So it is asked again after a pause, and again while it stays silent.
        _wakeTimer.Interval = AppParameters.Displays.ResumeDelay;
        _wakeTimer.Tick += (_, _) =>
        {
            _wakeTimer.Stop();
            _audioDevice = AudioEndpoint.DefaultDeviceName();
            RescanDisplays(_settleReason);

            if (--_settleTries > 0 && !_displays.HasBrightnessControl) _wakeTimer.Start();
        };

        // The first check waits, then one a day — the same rhythm as the macOS version.
        _updateTimer.Interval = AppParameters.Updates.FirstCheckDelay;
        _updateTimer.Tick += (_, _) =>
        {
            _updateTimer.Interval = AppParameters.Updates.CheckPeriod;
            CheckForUpdates(announceEither: false);
        };

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    // Brings up both halves and switches on the one that fits the moment.
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

        RescanDisplays("the first look");
        _audioDevice = AudioEndpoint.DefaultDeviceName();

        StartMediaKeys();

        _metrics.Start();
        _modeTimer.Start();
        _updateTimer.Start();
        _displayTimer.Start();

        // The first check right away rather than in a second: the app may have been started from a game.
        UpdateMode();

        Log.Info("started");
    }

    // --- Switching faces ---

    private void UpdateMode()
    {
        Win32.RECT screen = default;
        double scale = 1;

        // Full screen is decided on its own, without asking whether the panel is wanted: the tray
        // icon is hidden behind a game either way, and an animation nobody can see is pure waste.
        bool inGame = Win32.TryFullscreenArea(out screen, out scale);

        if (inGame == _inGame)
        {
            if (_inGame && _cfg.ShowOverlayInGames)
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
            ? "a full-screen application is active — spinner stopped"
            : "no full-screen application — spinner running");

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

        if (TrayHidden) _animator.Stop();

        ShowOverlay();
    }

    // Whether the tray icon is out of sight. It is only when a game covers the one screen there
    // is: with a second monitor the icon stays visible, and so should the animation.
    private bool TrayHidden => _inGame && _screenCount <= 1;

    private void EnterDesktop()
    {
        HideOverlay();

        if (_cfg.SpinOnDesktop) _animator.UpdateSpeed(_metrics.Latest.Readings.BusiestLoad);
        else _animator.Rewind();
    }

    // The panel and the frame counter go together: counting frames is the expensive half, and
    // with the panel switched off there is nowhere to show them.
    private void ShowOverlay()
    {
        if (!_cfg.ShowOverlayInGames) return;

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
    }

    private void HideOverlay()
    {
        _overlay.Visibility = Visibility.Hidden;
        _overlay.ShowNotice("");

        _fpsTimer.Stop();
        _fps.Stop();
        _fpsApi = "";

        // The frame values went stale the moment the panel left — they must not pass for current.
        _overlayModel.ApplyFps(null, null);
    }

    // --- Readings ---

    private void OnMetrics(MetricsSnapshot snapshot)
    {
        Readings r = snapshot.Readings;

        if (_inGame && _cfg.ShowOverlayInGames) _overlayModel.Apply(r);

        // Checked every poll rather than at the change of face alone: a monitor can be unplugged
        // mid-game, and then the icon goes behind the game after all.
        if (TrayHidden) _animator.Stop();
        else if (_cfg.SpinOnDesktop) _animator.UpdateSpeed(r.BusiestLoad);

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
        TimeSpan period = TimeSpan.FromSeconds(AppParameters.Polling.ReadingsLogSeconds);
        if (DateTime.UtcNow - _lastReadingsLog < period) return;

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
        _keys.Handler = OnMediaKey;
        // Every key into the log goes with the full record: it is a line per press, and someone
        // who asked for the whole course of work asked for this too.
        _keys.Trace = _cfg.Debug ?? false;
        _keys.StartVolumeKeys();

        // The brightness keys of the keyboard. Windows makes no virtual key of them and acts on
        // them nowhere, so they are read as raw HID input — the only place they show up at all.
        // Whether a press then changes anything is decided where the volume keys decide it: by
        // what there is to drive, not by a switch of its own.
        _keys.StartMediaUsages();

        // The panel Windows draws for the same keys. It is only ever touched right after a press
        // we have already answered with our own panel.
        ShellFlyout.Watch();

        // Said out loud: someone reading a log full of KEY lines has to know where they come from
        // and how to stop them.
        if (_keys.Trace)
            Log.Info("every key press goes into the log as a KEY line while Debug is on");
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

        // Nothing to drive: the key goes to the system, which shows its own panel. Silent means it
        // is ours but there is nothing true to show — a monitor in HDR ignores brightness commands.
        if (result == MediaKeyResult.Consumed) _osd.Show(value, kind);

        return result;
    }

    // --- The tray menu ---

    private void WireTray()
    {
        _tray.StatsRequested += ToggleStats;
        _tray.UpdateRequested += () => CheckForUpdates(announceEither: true);
        _tray.AutoStartToggled += SetAutoStart;
        _tray.SpinnerChanged += ReloadSpinner;

        _tray.IntervalChanged += () =>
        {
            _metrics.SetInterval(_cfg.UpdateIntervalMs);
            _fpsTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1000, _cfg.UpdateIntervalMs));
        };

        // The switch takes effect at once, even mid-game: the panel appears or goes, and the
        // frame counter with it. Whether the tray animation runs does not depend on it.
        _tray.OverlayChanged += () =>
        {
            if (!_inGame) return;

            if (_cfg.ShowOverlayInGames) ShowOverlay();
            else HideOverlay();
        };

        _tray.OsdChanged += () => RescanDisplays("the OSD settings changed");

        // The language changes the titles of everything already built — the menu is rebuilt, and
        // the status window re-reads its captions the next time it is shown.
        _tray.LanguageChanged += () =>
        {
            _tray.Rebuild();
            _tray.ShowDisplays(_displays.DisplayNames);
            _tray.ShowHdr();
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

    // --- Screens and sound ---

    // A changed sound device means the volume goes somewhere else — the monitor speakers, the
    // headphones — and the hourly pass catches a monitor that was switched on, handed over by
    // a KVM, or woken up on its own.
    private void WatchDisplays()
    {
        string device = AudioEndpoint.DefaultDeviceName();
        bool soundChanged = device != _audioDevice;
        _audioDevice = device;

        bool due = DateTime.UtcNow - _lastRescan >= AppParameters.Displays.RescanPeriod;

        if (soundChanged) RescanDisplays($"the sound device is now \"{device}\"");
        else if (due) RescanDisplays("the hourly look");

        // Between the full rescans the screens are only asked what they stand at. That is cheap
        // next to opening them all again, and it is what keeps the OSD honest when the brightness
        // was moved by the buttons on the monitor.
        else _displays.RereadValues();
    }

    private void RescanDisplays(string why)
    {
        _lastRescan = DateTime.UtcNow;
        _screenCount = System.Windows.Forms.Screen.AllScreens.Length;

        _displays.Refresh();
        _tray.ShowDisplays(_displays.DisplayNames);

        // HDR is switched elsewhere too — in the display settings, by a game, by the screen being
        // swapped — so the ticks are read anew along with the screens rather than remembered.
        _tray.ShowHdr();

        Log.Info($"displays rescanned: {why}");
    }

    // --- Updates ---

    // Asks GitHub for the newest release. The daily check only speaks up when there is something
    // new; the one from the menu answers either way — it was asked a question.
    private void CheckForUpdates(bool announceEither)
    {
        _ = Task.Run(async () =>
        {
            UpdateChecker.Result? result = await UpdateChecker.Check();

            // The answer can arrive after Quit: a dispatcher on its way out would throw, and the
            // exception would surface as a crash in the log at every exit.
            if (_overlay.Dispatcher.HasShutdownStarted) return;

            _ = _overlay.Dispatcher.BeginInvoke(() =>
            {
                if (result is null)
                {
                    if (announceEither) _tray.Notify(Text.UpdateCheckFailed);
                    return;
                }

                if (result.IsNewer)
                    _tray.Notify(Text.UpdateAvailable(result.Latest), AppParameters.Links.LatestRelease);
                else if (announceEither)
                    _tray.Notify(Text.UpdateUpToDate(result.Current));
            });
        });
    }

    // --- System changes ---

    // Waking from sleep or hibernation. The monitors come back a moment later than the message,
    // and one that was asked too early answers nothing over DDC — hence the pause.
    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;

        // One timer for every wake-up rather than a new one each time: a machine can report
        // resuming twice in a row, and each of those would otherwise leave a timer behind.
        _ = _overlay.Dispatcher.BeginInvoke(() => Settle("the machine woke up"));
    }

    // Asks the screens again in a while, and goes on asking while they answer nothing over DDC.
    private void Settle(string why)
    {
        _settleReason = why;
        _settleTries = AppParameters.Displays.SettleTries;

        _wakeTimer.Stop();
        _wakeTimer.Start();
    }

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

            RescanDisplays("the screen configuration changed");

            // The mode may still be settling — coming out of HDR a monitor goes quiet over DDC for
            // a few seconds — so the screens are asked again in a while.
            Settle("the screen configuration settled");
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
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;

        _modeTimer.Stop();
        _fpsTimer.Stop();
        _updateTimer.Stop();
        _displayTimer.Stop();
        _wakeTimer.Stop();

        _stats?.Close();
        _overlay.Close();

        ShellFlyout.Stop();
        _keys.Dispose();
        _osd.Dispose();
        _displays.Dispose();
        DdcQueue.Stop();

        // The tray goes first: the animator frees the icon handles, and the icon must not be
        // showing one of them by then.
        _tray.Dispose();
        _animator.Dispose();

        _metrics.Dispose();
        _fps.Dispose();
        _hardware.Dispose();

        Log.Finish("exit from the tray menu");
    }
}
