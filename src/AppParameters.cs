using System;

namespace SystemSpinnerX64;

/// <summary>
/// Everything the program decides for itself: sizes, delays and limits that config.conf does not
/// offer. They are gathered here rather than scattered through the code so that the answer to
/// "where does this number come from" is one file — and so that changing one is a decision made
/// in the open, not an edit buried in a window.
///
/// What is not here: the constants of Windows itself — window styles, DWM attributes, ETW event
/// ids, registry paths. Those are not parameters, they are the shape of the calls that use them,
/// and they belong next to those calls.
/// </summary>
internal static class AppParameters
{
    /// <summary>Where the windows sit and how far from things.</summary>
    internal static class Layout
    {
        /// <summary>
        /// Where a window waits until it has been measured. Further out than any screen: on a
        /// multi-monitor machine the desktop can go negative, but not by thirty thousand pixels.
        /// </summary>
        public const int OffScreen = -32000;

        /// <summary>Gap between the status window and the work area edge, in WPF units.</summary>
        public const double StatusGap = 10;

        /// <summary>Gap between the chart window and the status window, in WPF units.</summary>
        public const double ChartGap = 8;

        /// <summary>Top of the temperature bars. No sensor shown there ever goes above a hundred.</summary>
        public const double TemperatureScale = 100;

        /// <summary>Width of the volume panel in WPF units. The same as the macOS version.</summary>
        public const double OsdWidth = 300;

        /// <summary>
        /// How many process icons the chart window keeps converted. Processes come and go, and
        /// past this the cache is dropped whole rather than pruned.
        /// </summary>
        public const int ChartIconCache = 128;
    }

    /// <summary>The panel over a game.</summary>
    internal static class Overlay
    {
        /// <summary>
        /// Value size in WPF units. Deliberately independent of the resolution: WPF already scales
        /// units by DPI, and a multiplier by screen height would scale everything twice.
        /// </summary>
        public const double BaseFontDip = 14;

        // One colour, differing only in density: a multicoloured panel over a game reads worse.
        public const double ValueDensity = 0.92;
        public const double TagDensity = 0.70;
        public const double UnitDensity = 0.55;
        public const double WarnDensity = 0.95;

        /// <summary>Above this the frame rate is shown as is rather than counted in thousands.</summary>
        public const double MaxShownFps = 999;
    }

    /// <summary>What the tray menu offers.</summary>
    internal static class Menu
    {
        /// <summary>
        /// Poll periods in the menu, milliseconds. The macOS version starts at half a second; the
        /// floor here is one, and not arbitrarily: walking the sensors wakes the kernel driver,
        /// and polling faster than a second becomes the very load the program is showing.
        /// </summary>
        public static readonly int[] Intervals = { 1000, 1500, 2000, 3000 };

        /// <summary>Adjustment step counts — the same four as the macOS version.</summary>
        public static readonly int[] Steps = { 8, 16, 24, 32 };
    }

    /// <summary>The tray animation and the frames it is built from.</summary>
    internal static class Spinning
    {
        /// <summary>No faster than 120 frames a second: past that the eye sees no difference.</summary>
        public const double MinIntervalSeconds = 1.0 / 120.0;

        /// <summary>
        /// How much the computed speed has to change before the timer is rebuilt. Without the
        /// tolerance the timer would be recreated on every poll — the load never stands still.
        /// </summary>
        public const double SpeedTolerance = 0.15;

        /// <summary>How solid a silhouette is. From the macOS version: pure white is too harsh.</summary>
        public const float SilhouetteAlpha = 0.8f;

        /// <summary>Below this a pixel counts as empty. Anti-aliased edges fade out to nothing.</summary>
        public const int OpaqueEnough = 8;
    }

    /// <summary>How often the machine is asked about itself.</summary>
    internal static class Polling
    {
        /// <summary>
        /// The floor for the poll period, milliseconds, whatever the config says. Each walk of the
        /// sensor tree wakes the driver, and three walks a second are the load being measured.
        /// </summary>
        public const int MinIntervalMs = 1000;

        /// <summary>How often the program re-checks which of its two faces fits. A second reads as instant.</summary>
        public const double ModeCheckSeconds = 1;

        /// <summary>
        /// How often the current readings go to the log at Info. A debugging aid; the value has
        /// never needed changing, which is why it is here and not in the config.
        /// </summary>
        public const double ReadingsLogSeconds = 10;

        /// <summary>How many process icons the poll keeps: as many as the window shows.</summary>
        public const int ProcessIconCache = 64;
    }

    /// <summary>The frame counter and its ETW session.</summary>
    internal static class Fps
    {
        /// <summary>Silence longer than this means the graphics interface changed.</summary>
        public const double SourceStaleSeconds = 2.0;

        /// <summary>Frames older than this no longer count towards the average.</summary>
        public const double StaleFramesSeconds = 2.0;

        /// <summary>How long a DxgKrnl task is watched before its rate is judged.</summary>
        public const double TaskProbeSeconds = 1.0;

        /// <summary>How often to say in the log that no task matched.</summary>
        public const double NoMatchReportSeconds = 5.0;

        /// <summary>Fewer events than this is not enough to complain about: nothing was drawn.</summary>
        public const int MinEventsToComplain = 300;

        /// <summary>How long events are counted before the tally goes to the log.</summary>
        public const double ProviderReportSeconds = 8.0;

        /// <summary>How long DXGI and D3D9 are given before Vulkan and OpenGL are tried.</summary>
        public static readonly TimeSpan FallbackCheckDelay = TimeSpan.FromSeconds(4);

        /// <summary>How often after that the choice is reconsidered.</summary>
        public static readonly TimeSpan FallbackCheckPeriod = TimeSpan.FromSeconds(3);
    }

    /// <summary>Finding out the address the world sees.</summary>
    internal static class Network
    {
        /// <summary>A plain text page that answers with the caller's address and nothing else.</summary>
        public static readonly Uri ExternalAddressUrl = new("https://checkip.dyndns.org");

        /// <summary>How long to wait for it. The address is a nicety, not a reading.</summary>
        public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

        /// <summary>How long to wait before asking again after a failure.</summary>
        public const int LookupDelaySeconds = 15;
    }

    /// <summary>The log file.</summary>
    internal static class Logging
    {
        /// <summary>Past this the log is rotated: one previous file is kept, the rest is dropped.</summary>
        public const long MaxBytes = 1_000_000;

        /// <summary>How often the size is checked, in lines written. Every line would be a stat call.</summary>
        public const int SizeCheckEvery = 100;
    }

    /// <summary>What the program requires of the machine.</summary>
    internal static class Requirements
    {
        /// <summary>Windows 11 and 10 are declared by the same manifest GUID; the build tells them apart.</summary>
        public const int Windows11Build = 22000;
    }
}
