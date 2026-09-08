//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SystemSpinnerX64.Localization;
using SystemSpinnerX64.Monitoring;
using SystemSpinnerX64.Spinner;

namespace SystemSpinnerX64.Configuration;

// Every setting the app has; parameter descriptions live in sample.conf.
public sealed class AppConfig
{
    public Language Language { get; set; } = Language.Auto;

    // How often the sensors are polled and the panel redrawn, milliseconds.
    public int UpdateIntervalMs { get; set; } = 1000;

    // Which GPU to show when there are several.
    public int GpuIndex { get; set; }

    // Show the overlay over full-screen apps — Enable under [FullScreenOverlay].
    public bool ShowOverlayInGames { get; set; } = true;

    // Keep the tray icon spinning outside full-screen apps too.
    public bool SpinOnDesktop { get; set; } = true;

    public SensorNamesConfig Sensors { get; set; } = new();

    // Detailed logging. null means the parameter was absent — the very first run, which is
    // logged in full and then writes the switch off into the file it creates.
    public bool? Debug { get; set; }

    public FanConfig Fans { get; set; } = new();
    public WarnConfig Warn { get; set; } = new();
    public AppearanceConfig Appearance { get; set; } = new();
    public SpinnerConfig Spinner { get; set; } = new();
    public OsdConfig Osd { get; set; } = new();
    public StatsConfig Stats { get; set; } = new();

    // Where the config will land, without reading it: the log needs this before parsing starts.
    // Not named Directory so it does not shadow System.IO.Directory here.
    public static string ResolveDirectory()
    {
        string[] candidates = Candidates();
        string path = candidates.FirstOrDefault(File.Exists) ?? candidates[0];

        return System.IO.Path.GetDirectoryName(path) ?? AppContext.BaseDirectory;
    }

    // Fallback folder for the log when the main one is not writable.
    public static string FallbackDirectory =>
        System.IO.Path.GetDirectoryName(UserPath) ?? AppContext.BaseDirectory;

    public string Path { get; private set; } = UserPath;

    public bool LoadedFromFile { get; private set; }

    // Sections the file has not got — an older file, or one trimmed by hand. Written back at startup.
    public IReadOnlyList<string> MissingSections { get; internal set; } = Array.Empty<string>();

    // The file exists but could not be read — that has to be said, not silently ignored.
    public string? LoadError { get; private set; }

    // The config next to the exe wins, so the folder can be carried around.
    public static string PortablePath =>
        System.IO.Path.Combine(AppContext.BaseDirectory, AppParameters.Identity.ConfigFile);

    public static string UserPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppParameters.Identity.AppFolder, AppParameters.Identity.ConfigFile);

    // The test is not «can it be written» but «does it belong here»: the app runs as administrator
    // and writing to Program Files succeeds, so checking for failure never triggered.
    public static bool PortableAllowed => !IsSystemFolder(AppContext.BaseDirectory);

    // By whole path segment: Windows and Program Files are not only on drive C, and comparing
    // segments keeps «Program Files Backup» out of the system folders.
    private static readonly Regex SystemFolders = new(
        @"(^|\\)(Program Files( \(x86\))?|ProgramData|Windows)(\\|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal static bool IsSystemFolder(string directory) =>
        SystemFolders.IsMatch(directory.TrimEnd(System.IO.Path.DirectorySeparatorChar));

    // Where to look for the config and where to write it, in order of preference.
    private static string[] Candidates() =>
        PortableAllowed ? new[] { PortablePath, UserPath } : new[] { UserPath };

    public static AppConfig Load()
    {
        foreach (string path in Candidates())
        {
            if (!File.Exists(path)) continue;

            try
            {
                AppConfig cfg = ConfFormat.Read(File.ReadAllText(path));
                cfg.Path = path;
                cfg.LoadedFromFile = true;
                return cfg;
            }
            catch (Exception ex)
            {
                // Staying silent would mean the user edits fan names while the defaults are in use.
                return new AppConfig { Path = path, LoadError = ex.Message };
            }
        }

        return new AppConfig();
    }

    public bool Save()
    {
        try
        {
            string? dir = System.IO.Path.GetDirectoryName(Path);
            if (dir is { Length: > 0 }) Directory.CreateDirectory(dir);
            File.WriteAllText(Path, ConfFormat.Write(this));
            LoadedFromFile = true;
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"{Path} was not written: {ex.Message}");
            return false;
        }
    }

    // Writes the config to the given path and keeps working with it.
    public bool SaveAs(string path)
    {
        Path = path;
        return Save();
    }

    // Writes the config where it was read from, otherwise per Candidates().
    public string? SaveSomewhere()
    {
        IEnumerable<string> candidates = LoadedFromFile
            ? new[] { Path }.Concat(Candidates())
            : Candidates();

        foreach (string path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            if (SaveAs(path)) return path;

        return null;
    }
}

// How the in-game overlay looks; parameter descriptions live in sample.conf.
public sealed class AppearanceConfig
{
    // Several comma-separated names — the first one present in the system wins.
    public string FontFamily { get; set; } = "Impact, Haettenschweiler, Arial Narrow, Arial";

    public string TextColor { get; set; } = "#FFFFFF";

    public double TextOpacity { get; set; } = 0.85;

    // Dark backdrop, off by default: over a game it reads as a rectangle across the picture, and
    // the shadow already protects the text from bright frames.
    public bool ShowPanel { get; set; }

    public string PanelColor { get; set; } = "#0B0D12";

    public double PanelOpacity { get; set; } = 0.45;

    // Shadow blur. Without a backdrop this is the only thing separating the digits from bright
    // frames: small values outline the letters, large ones give a soft blob.
    public double ShadowBlur { get; set; } = 3;

    public double ShadowOpacity { get; set; } = 0.9;

    // Font size as a percentage of the computed one.
    public double FontScalePercent { get; set; } = 100;

    // Unit label size as a percentage of the value size.
    public double UnitSizePercent { get; set; } = 55;

    // Offset from the top-left corner of the work area, in WPF units.
    public double Margin { get; set; } = 10;

    // The rows and the order of the values along them — Row1, Row2, … in the file.
    public List<OverlayRow> Rows { get; set; } = OverlayRow.Default();

    // Full-screen applications the panel is not shown over: exe names with * and ? in them.
    public List<string> BlackListApplications { get; set; } = DefaultBlackList();

    // What goes full screen without being a game: screenshot tools, the lock screen, players,
    // browsers with a video, a slide show, a remote desktop, video calls. Anyone can cross a name off.
    public static List<string> DefaultBlackList() => new()
    {
        "SnippingTool*", "ScreenClippingHost", "ShareX", "Greenshot", "Lightshot",
        "LockApp", "LogonUI",
        "vlc", "mpv", "mpc-hc*", "mpc-be*", "PotPlayer*", "wmplayer", "Video.UI", "Photos", "Microsoft.Photos",
        "chrome", "msedge", "firefox", "brave", "opera", "vivaldi",
        "POWERPNT", "mstsc", "HandBrake",
        "Zoom", "Teams", "ms-teams", "ktalk*", "TrueConf"
    };
}

// The tray icon outside full-screen apps.
public sealed class SpinnerConfig
{
    // Frame set from SpinnerCatalog.
    public string Style { get; set; } = "Loader";

    // Colouring; only applies to sets that support it.
    public SpinnerEffect Effect { get; set; } = SpinnerEffect.Original;

    public bool InvertRotation { get; set; }
}

// The status window, opened by a left click on the tray icon.
public sealed class StatsConfig
{
    // Look up the external address through checkip.dyndns.org.
    public bool ShowExternalAddress { get; set; } = true;

    // Chart history length. 900 points at a one-second poll is a quarter of an hour.
    public int HistoryPoints { get; set; } = 500;

    public int TopProcesses { get; set; } = 12;
}

// The custom on-screen display for volume and brightness, and what drives it.
public sealed class OsdConfig
{
    // Show the custom OSD even when there is nothing to control — a single built-in screen, say.
    public bool AlwaysUseCustomOsd { get; set; }

    // Steps the 0…100 scale is divided into: one key press is one step.
    public int AdjustmentSteps { get; set; } = 16;

    // Drive external monitor brightness over DDC/CI.
    public bool ControlExternalBrightness { get; set; } = true;

    // Move the monitor's own volume over DDC/CI in step with the Windows mixer, so both carry the
    // same number: either one alone leaves a second attenuator nothing on screen shows.
    public bool ControlExternalVolume { get; set; } = true;

    // The pair standing in for brightness keys the keyboard has not got, dimmer first. "native"
    // is written here as soon as the real keys are seen, and then nothing is registered.
    public string BrightnessKeys { get; set; } = "Ctrl+F1/F2";

    // What the value means when the keyboard turns out to have the real keys.
    public const string NativeKeys = "native";
}

// Highlighting for values past a threshold; zero disables one.
public sealed class WarnConfig
{
    public string Color { get; set; } = "#FF6A52";

    public double CpuTemp { get; set; } = 85;
    public double GpuTemp { get; set; } = 83;

    // Used memory, per cent of the installed total.
    public double SysMem { get; set; } = 90;

    // Used video memory, per cent of the whole.
    public double GpuMem { get; set; } = 90;

    // Page file in use, per cent of its size.
    public double SwapMem { get; set; } = 90;

    // Load, per cent. Higher than the memory thresholds: in a game the load sits near the top all
    // the time, and a highlight that is always on says nothing.
    public double CpuUsage { get; set; } = 95;

    public double GpuUsage { get; set; } = 95;
}

// Fan sensor names. Every board names its headers differently, so while the lists are empty the app
// scans the hardware and fills them in itself.
public sealed class FanConfig
{
    public List<string> Cpu { get; set; } = new();

    public List<string> Aio { get; set; } = new();

    public List<string> Gpu { get; set; } = new();

    // Case fans, a cell each. A list written by hand is never touched; an empty one is filled by
    // the scan, or the case fans would have nowhere to show.
    public List<string> Extra { get; set; } = new();

    public bool AverageCpu { get; set; }

    public bool AverageAio { get; set; }

    // On by default: a card has two or three fans, they spin together, and picking one is arbitrary.
    public bool AverageGpu { get; set; } = true;

    // Sorts what was found into the lists: matching roles first, then fallbacks — HardwareMonitor
    // tries the names in order.
    public bool ApplyDetected(IReadOnlyList<FanSensor> fans)
    {
        if (fans.Count == 0) return false;

        List<string> cpu = Pick(fans, FanRole.Cpu);
        List<string> rest = Pick(fans, FanRole.Case);

        Gpu = Pick(fans, FanRole.Gpu);

        // A pump is never invented: a case fan in the AIO slot would look like a working reading.
        // In the CPU slot it beats a dash, and on boards wiring the cooler to SYS_FAN it is right.
        Aio = Pick(fans, FanRole.Aio);
        Cpu = cpu.Concat(rest).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (Extra.Count == 0) Extra = Case(fans, shownAsCpu: Cpu.FirstOrDefault());

        return true;
    }

    // The case fans that deserve a cell. Boards naming every header "Fan #1" give no CPU fan, so
    // all of them land in that slot and only the first is read. Silent ones are left out.
    private static List<string> Case(IReadOnlyList<FanSensor> fans, string? shownAsCpu) =>
        fans.Where(f => f.Role == FanRole.Case && f.Rpm is null or >= 1)
            .OrderByDescending(f => f.Rpm ?? -1)
            .Select(f => f.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => !name.Equals(shownAsCpu, StringComparison.OrdinalIgnoreCase))
            .ToList();

    // Spinning ones first, silent ones after.
    private static List<string> Pick(IReadOnlyList<FanSensor> fans, FanRole role) =>
        fans.Where(f => f.Role == role)
            .OrderByDescending(f => f.Rpm ?? -1)
            .Select(f => f.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    // No names at all — reason to scan the hardware again.
    public bool IsEmpty => Cpu.Count == 0 && Aio.Count == 0 && Gpu.Count == 0;

    // Summary for the log. Never written to the file — it is not a setting.
    public string Summary =>
        $"CPU: {Describe(Cpu)}\nAIO: {Describe(Aio)}\nGPU: {Describe(Gpu)}\nSYS: {Describe(Extra)}";

    private static string Describe(IReadOnlyList<string> names) =>
        names.Count == 0 ? "not found" : string.Join(", ", names);
}

// Sensor names the readings are taken by.
public sealed class SensorNamesConfig
{
    public List<string> CpuLoad { get; set; } = new() { "CPU Total" };

    // Intel names first, then the AMD ones: whichever the machine reports is the one that wins,
    // so one list serves both.
    public List<string> CpuTemp { get; set; } = new()
    {
        "CPU Package", "Core Max", "CPU Cores", "P-Core Max",
        "Core (Tctl/Tdie)", "Core (Tctl)", "Core (Tdie)", "CCDs Max (Tdie)", "CCD1 (Tdie)",
        "Package"
    };

    public List<string> CpuPower { get; set; } = new()
    {
        "CPU Package", "CPU Cores", "Package", "Core (SVI2 TFN)", "CPU SoC"
    };

    // Word that selects the cores the clock is averaged over; hybrid Intel names them "P-Core #1".
    // When nothing matches, every core but the excluded ones is taken, as AMD and plain Intel need.
    public string CpuClockCores { get; set; } = "P-Core";

    public List<string> MemoryUsed { get; set; } = new() { "Memory Used" };

    // Free memory. With the used part it gives the installed total, which LHM does not report.
    public List<string> MemoryAvailable { get; set; } = new() { "Memory Available" };

    public List<string> GpuLoad { get; set; } = new() { "GPU Core", "D3D 3D" };

    public List<string> GpuTemp { get; set; } = new() { "GPU Core", "GPU Hot Spot", "GPU Temperature" };

    public List<string> GpuPower { get; set; } = new() { "GPU Package", "GPU Power", "GPU PPT" };

    public List<string> GpuClock { get; set; } = new() { "GPU Core", "GPU Graphics" };

    // Used video memory, the Direct3D counter first: it is what the Task Manager shows and it
    // follows the memory back down, while the card's own count sticks at the peak on NVIDIA.
    public List<string> GpuMemory { get; set; } = new() { "D3D Dedicated Memory Used", "GPU Memory Used", "GPU Memory Dedicated Used" };

    // What the list above said before, and what a config file written by an older version still
    // carries. Replaced on reading: the value it names goes stale, and nobody chose it on purpose.
    internal static readonly string[] StaleGpuMemory = { "GPU Memory Used", "D3D Dedicated Memory Used", "GPU Memory Dedicated Used" };

    // Total video memory. Only the status window needs it: without a ceiling the megabytes have
    // nothing to be compared against, and a scale without one is meaningless.
    public List<string> GpuMemoryTotal { get; set; } = new() { "GPU Memory Total", "D3D Dedicated Memory Total" };
}

// A value the overlay can show. ExtraFans is not one value but however many names stand in
// ExtraFan under [Hardware].
public enum OverlayMetric
{
    CpuLoad,
    CpuTemp,
    CpuPower,
    CpuClock,
    SysMemory,
    CpuFan,
    AioFan,
    ExtraFans,
    GpuLoad,
    GpuTemp,
    GpuPower,
    GpuClock,
    GpuMemory,
    GpuFan,
    Fps,
    FrameTime
}

// One row of the overlay: its tag and the values along it, in the order they are shown.
public sealed class OverlayRow
{
    public OverlayRow(string title, IEnumerable<OverlayMetric> metrics)
    {
        Title = title;
        Metrics = metrics.ToList();
    }

    public string Title { get; }

    public List<OverlayMetric> Metrics { get; }

    // The rows as they were before any of this could be configured.
    public static List<OverlayRow> Default() => new()
    {
        new OverlayRow("CPU", new[]
        {
            OverlayMetric.CpuLoad, OverlayMetric.CpuTemp, OverlayMetric.CpuPower,
            OverlayMetric.CpuClock, OverlayMetric.SysMemory, OverlayMetric.CpuFan,
            OverlayMetric.AioFan, OverlayMetric.ExtraFans
        }),
        new OverlayRow("GPU", new[]
        {
            OverlayMetric.GpuLoad, OverlayMetric.GpuTemp, OverlayMetric.GpuPower,
            OverlayMetric.GpuClock, OverlayMetric.GpuMemory, OverlayMetric.GpuFan
        }),
        new OverlayRow("FPS", new[] { OverlayMetric.Fps, OverlayMetric.FrameTime })
    };

    // "CPU: CpuLoad, CpuTemp" — the tag before the colon, the values after it. A mistake drops
    // the row rather than stopping the app, and the reason goes into problem for the caller.
    public static OverlayRow? Parse(string text, out string? problem)
    {
        problem = null;

        string title = "";
        string list = text;

        int colon = text.IndexOf(':');
        if (colon >= 0)
        {
            title = text[..colon].Trim();
            list = text[(colon + 1)..];
        }

        var metrics = new List<OverlayMetric>();
        foreach (string name in list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse(name, ignoreCase: true, out OverlayMetric metric))
            {
                problem = $"\"{name}\" is not a value the panel knows. Available: {Names}";
                return null;
            }

            metrics.Add(metric);
        }

        // A tag with nothing after it is a mistake too: the row would be a label and no numbers.
        if (metrics.Count == 0 && title.Length > 0)
        {
            problem = $"\"{title}\" has no values after the colon";
            return null;
        }

        return metrics.Count == 0 ? null : new OverlayRow(title, metrics);
    }

    public override string ToString() =>
        Title.Length == 0
            ? string.Join(", ", Metrics)
            : $"{Title}: {string.Join(", ", Metrics)}";

    public static string Names => string.Join(", ", Enum.GetNames<OverlayMetric>());
}

// One name from BlackListApplications: * stands for any run of characters, ? for one of them.
internal sealed class ProcessPattern
{
    private static readonly char[] Slashes = { '\\', '/' };

    private readonly Regex _regex;

    // With a slash in it the pattern is a path and is held against the whole path of the exe.
    private readonly bool _wholePath;

    public string Text { get; }

    public ProcessPattern(string text)
    {
        Text = text.Trim();
        _wholePath = Text.IndexOfAny(Slashes) >= 0;

        string body = Regex.Escape(Text).Replace(@"\*", ".*").Replace(@"\?", ".");
        _regex = new Regex("^" + body + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }

    // Whether the exe at this path is the one meant. A bare name goes with or without ".exe".
    public bool Matches(string path)
    {
        if (_wholePath) return _regex.IsMatch(path);

        string file = path[(path.LastIndexOfAny(Slashes) + 1)..];
        if (_regex.IsMatch(file)) return true;

        int dot = file.LastIndexOf('.');
        return dot > 0 && _regex.IsMatch(file[..dot]);
    }

    // Blank entries are skipped: a stray comma in the list must not match every application.
    public static List<ProcessPattern> Compile(IEnumerable<string> names) =>
        names.Where(name => name.Trim().Length > 0).Select(name => new ProcessPattern(name)).ToList();

    // The first pattern the exe answers to, or null when none does.
    public static ProcessPattern? Find(IReadOnlyList<ProcessPattern> patterns, string path) =>
        patterns.FirstOrDefault(pattern => pattern.Matches(path));
}
