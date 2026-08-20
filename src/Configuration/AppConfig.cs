using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SystemSpinnerX64.Diagnostics;
using SystemSpinnerX64.Localization;

namespace SystemSpinnerX64.Configuration;

/// <summary>
/// Every setting the app has; parameter descriptions live in sample.conf. Everything except the
/// fan names has a default: fan headers are only known on the spot, so the first run fills them
/// in by scanning (see Startup/Preflight.cs).
///
/// The file is looked for next to the exe (portable, wins) and otherwise
/// in %LOCALAPPDATA%\SystemSpinnerX64.
/// </summary>
public sealed class AppConfig
{
    public Language Language { get; set; } = Language.Auto;

    /// <summary>How often the sensors are polled and the panel redrawn, milliseconds.</summary>
    public int UpdateIntervalMs { get; set; } = 1000;

    /// <summary>Which GPU to show when there are several. The discrete one comes first.</summary>
    public int GpuIndex { get; set; }

    /// <summary>
    /// Show the overlay over full-screen apps. Off leaves only the tray icon — that is, exactly
    /// System Spinner without the overlay.
    /// </summary>
    public bool ShowOverlayInGames { get; set; } = true;

    /// <summary>
    /// Keep the tray icon spinning outside full-screen apps too. Off leaves the first frame
    /// standing still until a game starts.
    /// </summary>
    public bool SpinOnDesktop { get; set; } = true;

    public SensorNamesConfig Sensors { get; set; } = new();

    // null means the parameter was absent: such a run is logged entirely at Info, and Warn is
    // written to the file, so later runs are quiet.
    public LogLevel? LogLevel { get; set; }

    public FanConfig Fans { get; set; } = new();
    public FpsConfig Fps { get; set; } = new();
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

    // The file exists but could not be read — that has to be said, not silently ignored.
    public string? LoadError { get; private set; }

    public const string FileName = "config.conf";

    public const string AppFolder = "SystemSpinnerX64";

    // The config next to the exe wins, so the folder can be carried around.
    public static string PortablePath =>
        System.IO.Path.Combine(AppContext.BaseDirectory, FileName);

    public static string UserPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppFolder, FileName);

    // The test is not «can it be written» but «does it belong here»: the app runs as administrator
    // and writing to Program Files succeeds, so checking for failure never triggered.
    public static bool PortableAllowed => !IsSystemFolder(AppContext.BaseDirectory);

    // By path segment: Windows and Program Files are not only on drive C, and the install may sit
    // any number of folders deep. Whole segments are compared, so «Program Files Backup» is not
    // a system folder.
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

    /// <summary>Writes the config to the given path and keeps working with it.</summary>
    public bool SaveAs(string path)
    {
        Path = path;
        return Save();
    }

    /// <summary>Writes the config where it was read from, otherwise per Candidates(). Path or null.</summary>
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
