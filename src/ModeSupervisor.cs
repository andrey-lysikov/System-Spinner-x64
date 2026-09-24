//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using SystemSpinnerX64.Configuration;
using SystemSpinnerX64.Devices;
using SystemSpinnerX64.Diagnostics;
using SystemSpinnerX64.Lighting;
using SystemSpinnerX64.Localization;
using SystemSpinnerX64.Monitoring;
using SystemSpinnerX64.Osd;
using SystemSpinnerX64.Platform;
using SystemSpinnerX64.Spinner;
using SystemSpinnerX64.Startup;
using SystemSpinnerX64.Tray;
using SystemSpinnerX64.ViewModels;
using SystemSpinnerX64.Views;
using Microsoft.Win32;

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
    private readonly DispatcherTimer _skyTimer = new();
    private readonly DispatcherTimer _themeTimer = new();

    private StatsWindow? _stats;

    // The motherboard lighting, when the machine has an Aura controller.
    private AuraSunlight? _aura;

    // The earliest word of a graphics driver reloading. Made in Start, on the UI thread.
    private GpuDriverWatch? _gpuWatch;

    // What the Sun & Moon spinner last drew, so the minute tick only redraws on a change.
    private SkyIcon.State? _skyShown;

    private bool _inGame;

    // The screen the overlay was last put on. Kept to notice the game moving to another monitor.
    private Win32.RECT? _gameScreen;

    // The full-screen window last named, and whether it is one the panel is kept away from.
    private IntPtr _gameWindow;
    private bool _overlayExcluded;

    // BlackListApplications from the config, compiled once at startup.
    private readonly List<ProcessPattern> _excludedApps;

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
        _excludedApps = ProcessPattern.Compile(cfg.Appearance.BlackListApplications);

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

        _modeTimer.Interval = AppParameters.Polling.ModeCheck;
        _modeTimer.Tick += (_, _) => UpdateMode();

        _fpsTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(AppParameters.Polling.MinIntervalMs, cfg.UpdateIntervalMs));
        _fpsTimer.Tick += (_, _) => UpdateFps();

        // The screens are looked at again now and then: the sound device every few minutes, the
        // whole list once an hour. Neither has an event of its own that Windows raises here.
        _displayTimer.Interval = AppParameters.Displays.AudioCheckPeriod;
        _displayTimer.Tick += (_, _) => WatchDisplays();

        // A screen that has just woken or switched resolution answers nothing over DDC for a few
        // seconds, and one asked too early is written off until the hourly look. So: ask again.
        _wakeTimer.Interval = AppParameters.Displays.Settle;
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

        // The sun and the moon move slowly: a look once a minute, and a redraw only when the
        // picture would actually differ.
        _skyTimer.Interval = AppParameters.Sky.Refresh;
        _skyTimer.Tick += (_, _) => RefreshSky();

        _themeTimer.Interval = AppParameters.Spinning.ThemeSettle;
        _themeTimer.Tick += (_, _) => ApplyTheme();

        Sky.Configure(cfg.Spinner);

        SystemEvents.DisplaySettingsChanging += OnDisplaySettingsChanging;
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

        // Before the first poll: a driver can be reloading at boot as well.
        try
        {
            _gpuWatch = new GpuDriverWatch();
            _gpuWatch.AdapterChanged += reason => _hardware.PauseGpu(reason);
        }
        catch (Exception ex)
        {
            Log.Error("the display adapter watch did not start", ex);
        }

        _metrics.Start();
        _modeTimer.Start();
        _updateTimer.Start();
        _displayTimer.Start();

        DetectAura();

        if (_excludedApps.Count > 0)
            Log.Info("the panel is kept away from: " + string.Join(", ", _excludedApps.Select(p => p.Text)));

        // The first check right away rather than in a second: the app may have been started from a game.
        UpdateMode();

        Log.Event("started");
    }

    // --- Switching faces ---

    private void UpdateMode()
    {
        // Full screen is decided on its own, without asking whether the panel is wanted: the tray
        // icon is hidden behind a game either way, and an animation nobody can see is pure waste.
        bool inGame = Win32.TryFullscreenArea(out Win32.RECT screen, out double scale, out IntPtr window);

        if (inGame == _inGame)
        {
            if (!_inGame) return;

            // Alt-Tab from one full-screen application to another: one may be on the list.
            if (window != _gameWindow && LookAtGame(window) != _overlayExcluded)
            {
                _overlayExcluded = !_overlayExcluded;
                if (_overlayExcluded) HideOverlay();
                else ShowOverlay();
            }

            if (OverlayWanted)
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

        // What actually happened, not what usually happens: with a second monitor the tray icon
        // stays in sight, and a line claiming otherwise sends the next reader after a phantom.
        Log.Event(inGame
            ? "a full-screen application is active — spinner " +
              (TrayHidden ? "stopped: the tray icon is covered"
                          : $"still running: the tray icon is in sight on {_screenCount} screens")
            : "no full-screen application — spinner running");

        _overlayExcluded = inGame && LookAtGame(window);
        if (!inGame) _gameWindow = IntPtr.Zero;

        if (inGame) EnterGame();
        else EnterDesktop();
    }

    // Names the full-screen application in the log — that is where the name for the list
    // comes from — and says whether the panel is kept away from it.
    private bool LookAtGame(IntPtr window)
    {
        _gameWindow = window;

        string? path = Win32.ProcessPathOf(window);
        if (path is null)
        {
            Log.Event("the full-screen application would not tell its name");
            return false;
        }

        ProcessPattern? hit = ProcessPattern.Find(_excludedApps, path);

        Log.Event(hit is null
            ? $"full-screen application: {path}"
            : $"full-screen application: {path} — on the black list as \"{hit.Text}\", no panel over it");

        return hit is not null;
    }

    // Whether the panel is up: over a game, switched on, and not over an application on the list.
    private bool OverlayWanted => _inGame && _cfg.ShowOverlayInGames && !_overlayExcluded;

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
        // Said once and out loud: a log that shows a game being entered and then nothing about
        // the panel leaves it unclear whether it failed or was never asked for.
        if (!_cfg.ShowOverlayInGames)
        {
            Log.Event("the panel is off in the config — nothing is shown over the game");
            return;
        }

        if (_overlayExcluded)
        {
            Log.Event("the application is on the black list — nothing is shown over it");
            return;
        }

        _overlay.ApplyLayout();
        _overlay.Visibility = Visibility.Visible;
        _overlay.KeepOnTop();

        // The other half of the pair above: a log that says the panel was not shown, and never
        // says when it was, answers only one of the two questions ever asked about it.
        Log.Event("the panel is up over the game");

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

        if (OverlayWanted) _overlayModel.Apply(r);

        // Checked every poll rather than at the change of face alone: a monitor can be unplugged
        // mid-game, and then the icon goes behind the game after all.
        if (TrayHidden) _animator.Stop();
        else if (_cfg.SpinOnDesktop) _animator.UpdateSpeed(r.BusiestLoad);

        _tray.ShowTip(Tip(r));

        if (_aura is not null)
        {
            WarnConfig w = _cfg.Warn;
            double heat = w.EnableWarnColor ? WarnHeat.Of(r.CpuTempC, w.CpuTemp, r.GpuTempC, w.GpuTemp) : 0;
            _aura.Feed(heat, r.BusiestLoad / 100.0);
        }

        _stats?.Apply(snapshot);

        LogReadings(r);
    }

    private string Tip(Readings r)
    {
        string cpu = r.CpuLoad is double load ? $"CPU {load:0} %" : "CPU —";
        string gpu = r.GpuLoad is double gpuLoad ? $"GPU {gpuLoad:0} %" : "";
        string memory = r.MemLoadPercent is double mem ? $"MEM {mem:0} %" : "";

        string tip = string.Join("  ", new[] { cpu, gpu, memory }.Where(part => part.Length > 0));

        // With the lighting on, how bright the sky is and what the lamps give, on a line of its own.
        if (_aura is { IsEnabled: true } aura)
            tip += $"\n[{SkyBrightness()}, Light: {aura.Light * 100:0}%]";

        return tip;
    }

    // By day how bright the sun is: full from DimAbove up, where the lighting is off, fading to the
    // horizon. By night how much of the moon is lit, full at full moon.
    private static string SkyBrightness()
    {
        DateTime utc = DateTime.UtcNow;
        double elevation = Sky.Elevation(utc);

        return elevation > 0
            ? $"Sun {(1 - Sun.Factor(elevation, Sky.Config)) * 100:0}%"
            : $"Moon {Moon.Illumination(Moon.Phase(utc)) * 100:0}%";
    }

    private void UpdateFps()
    {
        _overlayModel.ApplyFps(_fps.Average(), _fps.FrameTimeMs());

        if (_fps.Api != _fpsApi) Log.Event($"frame source: {_fpsApi = _fps.Api}");
    }

    // Writes to the log what the panel shows — for checking against Task Manager or HWiNFO.
    private void LogReadings(Readings r)
    {
        TimeSpan period = AppParameters.Polling.ReadingsLog;
        if (DateTime.UtcNow - _lastReadingsLog < period) return;

        _lastReadingsLog = DateTime.UtcNow;

        // The case fans join the line as they are: without them a log cannot tell an empty
        // ExtraFan list from a fan that is named there and reads nothing.
        string sys = r.ExtraFanRpm.Count == 0
            ? ""
            : " SYS " + string.Join("·", r.ExtraFanRpm.Select(rpm => Show(rpm)));

        Log.Info($"readings: CPU {Show(r.CpuLoad)} % {Show(r.CpuTempC)} °C {Show(r.CpuPowerW)} W " +
                 $"{Show(r.CpuClockMhz)} MHz {Show(r.SysMemUsedGb, 1)} GB " +
                 $"{Show(r.CpuFanRpm)}/{Show(r.AioFanRpm)} RPM{sys} | " +
                 $"GPU {Show(r.GpuLoad)} % {Show(r.GpuTempC)} °C {Show(r.GpuPowerW)} W " +
                 $"{Show(r.GpuClockMhz)} MHz {Show(r.GpuMemUsedGb, 1)}/{Show(r.GpuMemTotalGb, 1)} GB " +
                 $"{Show(r.GpuFanRpm)} RPM");

        static string Show(double? value, int decimals = 0) =>
            value is null ? "—" : value.Value.ToString("F" + decimals, CultureInfo.InvariantCulture);
    }

    // --- The tray icon ---

    private void ReloadSpinner()
    {
        SpinnerStyle style = SpinnerCatalog.Validate(_cfg.Spinner.Style);

        if (style.Drawn)
        {
            // The place is looked up once; until then the sun is placed by the time zone alone.
            // The weather is asked for only here, for this spinner: with another one chosen
            // nothing goes out on its account.
            Sky.EnsureResolved();
            Sky.EnsureWeather();
            _skyShown = SkyIcon.Now();
            if (!_skyTimer.IsEnabled) _skyTimer.Start();
        }
        else
        {
            _skyShown = null;
            _skyTimer.Stop();
        }

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

    // The Sun & Moon spinner follows the sky: new pictures only when the haze, the phase, the
    // weather or the choice between the sun and the moon would change.
    private void RefreshSky()
    {
        if (_skyShown is null) return;

        Sky.EnsureResolved();
        Sky.EnsureWeather();
        if (SkyIcon.Now() != _skyShown) ReloadSpinner();
    }

    // --- The Aura lighting ---

    // Looked for off the UI thread: asking a HID device that is not the controller a driver
    // expects waits out a timeout, and the tray must not freeze meanwhile. The menu item appears
    // for any controller a driver knows.
    private void DetectAura()
    {
        int ledsPerChannel = _cfg.Aura.LedsPerChannel;

        _ = Task.Run(() =>
        {
            ILightDevice? device = LightDevices.Open(ledsPerChannel);

            if (_overlay.Dispatcher.HasShutdownStarted)
            {
                device?.Dispose();
                return;
            }

            _ = _overlay.Dispatcher.BeginInvoke(() =>
            {
                if (device is null)
                {
                    Log.Info("lighting: no supported controller — the Aura Sunlight item stays out of the menu");
                    return;
                }

                Log.Event($"lighting: controller found — {device.Describe()}");

                _aura = new AuraSunlight(_cfg.Aura, device);
                if (_cfg.Aura.Enable) _aura.Enable();

                _tray.ShowAura(true);
            });
        });
    }

    // Restart, shutdown or logoff: the lamps fade out before Windows ends the process, which would
    // leave them on whatever the last frame was.
    public void Darken(string reason) => _aura?.Darken(reason);

    private void SetAura(bool enabled)
    {
        if (_aura is null) return;

        if (enabled) _aura.Enable();
        else _aura.Disable();
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
        _keys.StartVolumeKeys();

        // Windows makes no virtual key of the brightness keys and acts on them nowhere, so they
        // are read as raw HID input. What a press then drives is decided as for the volume keys.
        _keys.StartMediaUsages();

        StartBrightnessKeys();

        // The panel Windows draws for the same keys. It is only ever touched right after a press
        // we have already answered with our own panel.
        ShellFlyout.Watch();
    }

    // A keyboard without brightness keys leaves the screen with no way to be dimmed from it at
    // all: the pair from the config stands in until the real keys turn up.
    private void StartBrightnessKeys()
    {
        string wanted = _cfg.Osd.BrightnessKeys;

        if (wanted.Trim().Equals(OsdConfig.NativeKeys, StringComparison.OrdinalIgnoreCase))
        {
            Log.Event("brightness keys: the keyboard's own");
            return;
        }

        HotKeySpec? spec = HotKeySpec.Parse(wanted, out string? problem);

        if (problem is not null)
        {
            Log.Warn($"[General] BrightnessKeys: {problem} — no keys are registered");
            return;
        }

        if (spec is null)
        {
            Log.Event("brightness keys: turned off");
            return;
        }

        _keys.NativeKeysSeen = OnNativeBrightnessKeys;

        if (_keys.StartBrightnessKeys(spec.Value))
            Log.Event($"brightness keys: {spec.Value.Describe}, until the keyboard's own are pressed");
    }

    // The keyboard has the real keys after all. The combination is already given back; what is
    // left is to remember it, so the next run does not hold it aside again.
    private void OnNativeBrightnessKeys()
    {
        _keys.NativeKeysSeen = null;
        _cfg.Osd.BrightnessKeys = OsdConfig.NativeKeys;

        Log.Event("the keyboard has brightness keys of its own: the stand-in keys are released");

        if (_cfg.SaveSomewhere() is null)
            Log.Warn("could not write the config — the stand-in keys will be registered again next time");
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

        // Nothing to drive: the key goes to the system, which shows its own panel.
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
        _tray.AuraToggled += SetAura;
        _tray.AuraLookChanged += () => _aura?.Restyle();
        _tray.AuraBrightnessChanged += () => _aura?.Rescale();

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

    // A changed sound device means the volume goes somewhere else, and the hourly pass catches a
    // monitor switched on, handed over by a KVM, or woken up on its own.
    private void WatchDisplays()
    {
        string device = AudioEndpoint.DefaultDeviceName();
        bool soundChanged = device != _audioDevice;
        _audioDevice = device;

        bool due = DateTime.UtcNow - _lastRescan >= AppParameters.Displays.RescanPeriod;

        if (soundChanged) RescanDisplays($"the sound device is now \"{device}\"");
        else if (due) RescanDisplays("the hourly look");

        // Between full rescans the screens are only asked what they stand at: cheap, and it keeps
        // the OSD honest when the brightness was moved by the buttons on the monitor.
        else _displays.RereadValues();
    }

    private void RescanDisplays(string why)
    {
        _lastRescan = DateTime.UtcNow;
        _screenCount = System.Windows.Forms.Screen.AllScreens.Length;

        _displays.Refresh();
        _tray.ShowDisplays(_displays.DisplayNames);

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
        // Asleep, the sky is not looked at; on waking the spinner is rebuilt, and with it the timer
        // starts again if Sun & Moon is still the one chosen.
        if (e.Mode == PowerModes.Suspend)
        {
            _ = _overlay.Dispatcher.BeginInvoke(() => _skyTimer.Stop());

            // Here on the system events thread and waited for: the machine goes to sleep once the
            // message has been answered, and the lamps must be dark by then.
            _aura?.Darken("the machine is going to sleep");
            return;
        }
        if (e.Mode != PowerModes.Resume) return;

        _aura?.Relight("the machine woke up");

        _ = _overlay.Dispatcher.BeginInvoke(() => ReloadSpinner());

        // The graphics driver starts over on a wake, much as it does on a reload.
        _hardware.PauseGpu("the machine woke up");

        // One timer for every wake-up rather than a new one each time: a machine can report
        // resuming twice in a row, and each of those would otherwise leave a timer behind.
        _ = _overlay.Dispatcher.BeginInvoke(() => Settle("the machine woke up"));

        // The controller comes back on its own saved effect and has to be taken over again.
        _aura?.Recover("the machine woke up");
    }

    // Asks the screens again in a while, and goes on asking while they answer nothing over DDC.
    private void Settle(string why)
    {
        _settleReason = why;
        _settleTries = AppParameters.Displays.SettleTries;

        _wakeTimer.Stop();
        _wakeTimer.Start();
    }

    // A graphics driver reload changes the screen configuration too. The card is let alone at once,
    // here on the system events thread: the queue to the UI thread could let one more poll through.
    private void OnDisplaySettingsChanging(object? sender, EventArgs e) =>
        _hardware.PauseGpu("the screen configuration is changing");

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        _hardware.PauseGpu("the screen configuration changed");
        OnDisplaySettingsSettled();
    }

    private void OnDisplaySettingsSettled() =>
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

            // The mode may still be settling — a monitor just switched goes quiet over DDC for
            // a few seconds — so the screens are asked again in a while.
            Settle("the screen configuration settled");
        });

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        // General arrives on a theme change too: there is no separate event for it.
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color)) return;

        // It also arrives in bursts — four in a second when a remote session connects — and each
        // would rebuild every frame of the spinner. The timer starts over with every one of them,
        // so the rebuild happens once, after the last.
        _overlay.Dispatcher.BeginInvoke(() =>
        {
            _themeTimer.Stop();
            _themeTimer.Start();
        });
    }

    private void ApplyTheme()
    {
        _themeTimer.Stop();

        ReloadSpinner();
        _tray.ApplyTheme();
        _osd.ApplyTheme();
        _stats?.ApplyTheme();
    }

    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanging -= OnDisplaySettingsChanging;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;

        _modeTimer.Stop();
        _fpsTimer.Stop();
        _updateTimer.Stop();
        _displayTimer.Stop();
        _wakeTimer.Stop();
        _skyTimer.Stop();
        _themeTimer.Stop();

        _gpuWatch?.Dispose();
        _aura?.Dispose();

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
    }
}
