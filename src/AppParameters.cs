//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Reflection;

namespace SystemSpinnerX64;

// Everything the program decides for itself: sizes, delays and limits config.conf does not offer.
// Constants of Windows itself — window styles, DWM attributes, event ids — stay with their calls.
internal static class AppParameters
{
    // What the program is called by everything that has to name it: the files it writes, the
    // scheduled task, the ETW session, the window titles.
    internal static class Identity
    {
        // The name shown to the user: the tray tooltip, the About window, the log header.
        public const string Name = "System-Spinner";

        // The settings file, next to the exe or in the per-user folder below.
        public const string ConfigFile = "config.conf";

        // The log, always beside the settings file.
        public const string LogFile = "System-Spinner.log";

        // The folder under %LOCALAPPDATA% used when the exe sits somewhere it must not write —
        // Program Files, say.
        public const string AppFolder = "System-Spinner";

        // The task in Task Scheduler that starts the app with Windows.
        public const string TaskName = "System-Spinner";

        // The ETW session the frame counter raises.
        public const string EtwSession = "System-Spinner-Frames";

        // The mutex that keeps a second copy from starting.
        public const string SingleInstanceMutex = "System-Spinner.SingleInstance";

        // The invisible window that receives the raw input — the brightness keys of the keyboard.
        public const string MessageWindow = "System-Spinner.Messages";

        // The icon inside the assembly — the tray and the About window read it from there rather
        // than from a file beside the exe.
        public const string IconResource = "System-Spinner.icon.ico";

        // The version of the running exe, three numbers. The assembly always carries four — .NET
        // adds the revision itself — and the fourth means nothing here: the tags, the changelog
        // and the update check all speak in three.
        public static string Version { get; } = ReadVersion();

        private static string ReadVersion()
        {
            System.Version? version = Assembly.GetExecutingAssembly().GetName().Version;

            return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    // Where to send someone whose machine is missing something, and where the app looks for
    // a newer copy of itself.
    internal static class Links
    {
        // The driver the temperature, power and fan sensors need.
        public const string SensorDriver = "https://pawnio.eu";

        // The newest release, as JSON: only the tag is read from it.
        public const string LatestReleaseApi =
            "https://api.github.com/repos/andrey-lysikov/System-Spinner-x64/releases/latest";

        // The same release as a page — this is what the notification opens.
        public const string LatestRelease =
            "https://github.com/andrey-lysikov/System-Spinner-x64/releases/latest";

        // Where the program lives: the button in the About window opens this.
        public const string Project = "https://github.com/andrey-lysikov/System-Spinner-x64";
    }

    // Keeping up with the screens and the sound device.
    internal static class Displays
    {
        // The screens are polled again now and then just in case: a monitor can be switched on,
        // a KVM can hand one over, and nothing tells the app about it.
        public static readonly TimeSpan RescanPeriod = TimeSpan.FromHours(1);

        // How often the default sound device is looked at. Windows has no event for it here, and
        // plugging in headphones changes where the volume goes.
        public static readonly TimeSpan AudioCheckPeriod = TimeSpan.FromMinutes(5);

        // How long to wait after the machine wakes up. Windows says "resumed" before the screens
        // have come back: asked straight away, a monitor answers nothing over DDC.
        public static readonly TimeSpan ResumeDelay = TimeSpan.FromSeconds(5);

        // How many times a screen that answers nothing over DDC is asked again after a mode change.
        public const int SettleTries = 4;
    }

    // Looking for a newer version.
    internal static class Updates
    {
        // The first check waits: at startup the network may not be up, and the app has better
        // things to do than talk to GitHub.
        public static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(10);

        // Then once a day, as in the macOS version.
        public static readonly TimeSpan CheckPeriod = TimeSpan.FromDays(1);
    }

    // Where the windows sit and how far from things.
    internal static class Layout
    {
        // Where a window waits until it has been measured.
        public const int OffScreen = -32000;

        // Gap between the status window and the work area edge, in WPF units.
        public const double StatusGap = 10;

        // Gap between the chart window and the status window, in WPF units.
        public const double ChartGap = 8;

        // Top of the temperature bars. No sensor shown there ever goes above a hundred.
        public const double TemperatureScale = 100;

        // Width of the volume panel in WPF units.
        public const double OsdWidth = 300;

        // How many process icons the chart window keeps converted.
        public const int ChartIconCache = 128;
    }

    // The panel over a game.
    internal static class Overlay
    {
        // Value size in WPF units. Deliberately independent of the resolution: WPF already scales
        // units by DPI, and a multiplier by screen height would scale everything twice.
        public const double BaseFontDip = 14;

        // One colour, differing only in density: a multicoloured panel over a game reads worse.
        public const double ValueDensity = 0.92;
        public const double TagDensity = 0.70;
        public const double UnitDensity = 0.55;
        public const double WarnDensity = 0.95;

        // Above this the frame rate is shown as is rather than counted in thousands.
        public const double MaxShownFps = 999;
    }

    // What the tray menu offers.
    internal static class Menu
    {
        // Poll periods in the menu, milliseconds.
        public static readonly int[] Intervals = { 1000, 1500, 2000, 3000 };

        // Adjustment step counts — the same four as the macOS version.
        public static readonly int[] Steps = { 8, 16, 24, 32 };
    }

    // The tray animation and the frames it is built from.
    internal static class Spinning
    {
        // No faster than 120 frames a second: past that the eye sees no difference.
        public const double MinIntervalSeconds = 1.0 / 120.0;

        // How much the computed speed has to change before the timer is rebuilt.
        public const double SpeedTolerance = 0.15;

        // How solid a silhouette is. From the macOS version: pure white is too harsh.
        public const float SilhouetteAlpha = 0.8f;

        // Below this a pixel counts as empty.
        public const int OpaqueEnough = 8;

        // The set shown when the config names one that is not there.
        public const string FallbackName = "Loader";
    }

    // How often the machine is asked about itself.
    internal static class Polling
    {
        // The floor for the poll period, milliseconds, whatever the config says.
        public const int MinIntervalMs = 1000;

        // How often the program re-checks which of its two faces fits.
        public const double ModeCheckSeconds = 1;

        // How often the current readings go to the log at Info.
        public const double ReadingsLogSeconds = 10;

        // How many process icons the poll keeps: as many as the window shows.
        public const int ProcessIconCache = 64;
    }

    // The frame counter and its ETW session.
    internal static class Fps
    {
        // Silence longer than this means the graphics interface changed.
        public const double SourceStaleSeconds = 2.0;

        // Frames older than this no longer count towards the average.
        public const double StaleFramesSeconds = 2.0;

        // How long a DxgKrnl task is watched before its rate is judged.
        public const double TaskProbeSeconds = 1.0;

        // How often to say in the log that no task matched.
        public const double NoMatchReportSeconds = 5.0;

        // Fewer events than this is not enough to complain about: nothing was drawn.
        public const int MinEventsToComplain = 300;

        // How long events are counted before the tally goes to the log.
        public const double ProviderReportSeconds = 8.0;

        // How long DXGI and D3D9 are given before Vulkan and OpenGL are tried.
        public static readonly TimeSpan FallbackCheckDelay = TimeSpan.FromSeconds(4);

        // How often after that the choice is reconsidered.
        public static readonly TimeSpan FallbackCheckPeriod = TimeSpan.FromSeconds(3);

        // The window the frame rate is averaged over, seconds. Shorter reacts faster and jumps;
        // longer is steadier and lags behind what the eye sees.
        public const double AverageWindowSeconds = 1.0;

        // An event arriving more often than this fires several times per frame and cannot be
        // a frame counter.
        public const double MaxPlausibleFps = 1500;

        // DxgKrnl events in order of preference; the first one the game actually sends wins.
        public static readonly string[] DxgKrnlTasks =
            { "PresentHistoryDetailed", "PresentHistory", "Present", "Flip" };
    }

    // The volume and brightness panel.
    internal static class Osd
    {
        // How long it stays up after the last key press, seconds.
        public const double VisibleSeconds = 2.5;

        // Distance from the bottom edge of the screen, in WPF units.
        public const double BottomInset = 140;
    }

    // Sensor names the config does not offer.
    internal static class Sensors
    {
        // What excludes the efficient cores from the clock when there are no explicit P-cores.
        public const string ClockExclude = "E-Core";
    }

    // Finding out the address the world sees.
    internal static class Network
    {
        // A plain text page that answers with the caller's address and nothing else.
        public static readonly Uri ExternalAddressUrl = new("https://checkip.dyndns.org");

        // How long to wait for it. The address is a nicety, not a reading.
        public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

        // How long to wait before asking after the network changed: the route may not be up yet.
        public const int LookupDelaySeconds = 15;

        // How long to wait before trying again when the request failed. Without it a single
        // timeout would leave the address blank until the network changes or the app restarts.
        public static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(15);

        // How many times to try again. After that the address stays as the local one: the service
        // is down, the machine is behind something, or nobody asked for an answer that badly.
        public const int MaxRetries = 2;
    }

    // The log file.
    internal static class Logging
    {
        // Past this the log is rotated: one previous file is kept, the rest is dropped.
        public const long MaxBytes = 1_000_000;

        // How often the size is checked, in lines written.
        public const int SizeCheckEvery = 100;
    }

    // What the config is allowed to ask for.
    internal static class Limits
    {
        // Overlay font scale, per cent of the base size.
        public const double MinFontScalePercent = 50;
        public const double MaxFontScalePercent = 300;

        // The resulting font size in WPF units, whatever the scale works out to.
        public const double MinFontDip = 8;
        public const double MaxFontDip = 48;

        // Size of the unit labels, per cent of the value size.
        public const double MinUnitSizePercent = 30;
        public const double MaxUnitSizePercent = 100;

        // Distance from the overlay to the screen edge, in WPF units.
        public const double MinOverlayMargin = 0;
        public const double MaxOverlayMargin = 200;

        // How faint the overlay text may be made.
        public const double MinTextOpacity = 0.15;
        public const double MaxTextOpacity = 1.0;

        // Blur radius of the shadow under the overlay text.
        public const double MinShadowBlur = 0;
        public const double MaxShadowBlur = 20;

        // Steps a volume or brightness key moves through from zero to full.
        public const int MinAdjustmentSteps = 2;
        public const int MaxAdjustmentSteps = 100;

        // Points kept for the chart in the status window.
        public const int MinHistoryPoints = 10;
        public const int MaxHistoryPoints = 10_000;

        // Rows in the process list.
        public const int MinTopProcesses = 1;
        public const int MaxTopProcesses = 100;
    }

    // What the program requires of the machine.
    internal static class Requirements
    {
        // Windows 11 and 10 are declared by the same manifest GUID; the build tells them apart.
        public const int Windows11Build = 22000;
    }
}
