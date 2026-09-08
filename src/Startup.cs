//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Principal;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using SystemSpinnerX64.Configuration;
using SystemSpinnerX64.Diagnostics;
using SystemSpinnerX64.Localization;
using SystemSpinnerX64.Monitoring;
using SystemSpinnerX64.Platform;

namespace SystemSpinnerX64.Startup;

// The outcome of the startup checks: either everything is ready, or there is a reason to stop — it
// goes to the log and the app closes.
internal sealed class PreflightResult
{
    private PreflightResult() { }

    public AppConfig? Config { get; private init; }

    // Sensors that are already open — no reason to walk the hardware tree twice.
    public HardwareMonitor? Hardware { get; private init; }

    // Reason to stop. null means it can start.
    public string? Problem { get; private init; }

    public bool CanStart => Problem is null && Config is not null && Hardware is not null;

    public static PreflightResult Start(AppConfig cfg, HardwareMonitor hw) =>
        new() { Config = cfg, Hardware = hw };

    // Stop and say why. Sensors, if they were opened, are closed.
    public static PreflightResult Stop(string problem, HardwareMonitor? hw = null)
    {
        hw?.Dispose();
        return new PreflightResult { Problem = problem };
    }
}

// Everything that has to be checked happens here, before the tray icon appears.
internal static class Preflight
{
    private const string ConfigDoc = "sample.conf in the project root";

    public static PreflightResult Run()
    {
        // 1. The Windows version first: without it there is no point opening the sensors.
        if (PlatformGuard.DescribeOs() is { Length: > 0 } osProblem)
            return PreflightResult.Stop(osProblem);

        Log.Event($"Windows {Environment.OSVersion.Version}");

        // A remote desktop explains half of what the log shows afterwards — one screen, no
        // brightness. Without this line all of it reads as a failure.
        if (Win32.IsRemoteSession)
            Log.Event("remote session: the desktop is on the virtual display of the remote adapter — " +
                     "the monitors on the graphics card are out of reach, and with them DDC/CI");

        // 2. The config.
        var cfg = AppConfig.Load();
        Log.Event(cfg.LoadedFromFile ? $"config loaded: {cfg.Path}" : "no config, using defaults");

        // The interface language right after reading the file: the tray menu is built later.
        Text.Use(cfg.Language);

        // A file left by an older version next to the exe is no longer read — staying silent would
        // mean edits in it simply have no effect.
        if (!AppConfig.PortableAllowed)
        {
            Log.Event($"the exe is in a system folder — config and log go to {AppConfig.FallbackDirectory}");

            if (System.IO.File.Exists(AppConfig.PortablePath))
                Log.Warn($"the file {AppConfig.PortablePath} is no longer used: settings do " +
                         "not belong next to an exe in a system folder. Delete it to avoid " +
                         "confusion — only the one in your profile applies.");
        }

        // No switch in the config — the first run. It is logged in full, and the file that gets
        // written afterwards carries the switch turned off.
        bool switchWasMissing = cfg.Debug is null;
        if (switchWasMissing)
        {
            cfg.Debug = false;
            Log.Info("no Debug switch in the config: this run is logged in full, and the switch " +
                     "is written off into the file");
        }
        else
        {
            Log.SetVerbose(cfg.Debug ?? false);
        }

        // A section the file has not got — a new version brought it — is written in with defaults.
        bool sectionsMissing = cfg.MissingSections.Count > 0;
        if (sectionsMissing)
            Log.Event("no [" + string.Join("], [", cfg.MissingSections) + "] in the config: " +
                     "written in with the standard settings");

        if (cfg.LoadError is { Length: > 0 } configError)
        {
            return PreflightResult.Stop(
                $"Could not read {cfg.Path}:\n{configError}\n\n" +
                $"Fix the file (format — {ConfigDoc}) or delete it, and it will be created anew.");
        }

        // 3. The sensors. Neither half works without them: the speed of the tray animation is
        // the processor load too.
        var hw = new HardwareMonitor(cfg);
        try
        {
            hw.Open();
            Log.Event($"sensors opened: CPU \"{hw.CpuName}\", GPU \"{hw.GpuName}\"");

            // Before the icon appears: the sensor list is needed exactly when something will not start.
            if (Environment.GetCommandLineArgs()
                           .Any(a => a.Equals("--list-sensors", StringComparison.OrdinalIgnoreCase)))
            {
                LogSensors(hw);
            }
        }
        catch (Exception ex)
        {
            hw.Dispose();
            return PreflightResult.Stop(
                $"The sensors could not be opened: {ex.Message}\n" +
                "Administrator rights are present, so the cause is most likely Memory Integrity " +
                "(Windows Security → Device security → Core isolation): it blocks the driver " +
                "used to read temperatures, power and fan speeds.");
        }

        // 4. The driver. Without it there are no temperatures, no power and no fan speeds — half
        // of what both faces show would turn into dashes.
        if (SensorDriver.DescribeIfMissing() is { Length: > 0 } driverProblem)
            return PreflightResult.Stop(driverProblem, hw);

        // 5. The processor: no sensor names are chosen for another vendor.
        if (PlatformGuard.DescribeHardware(hw.CpuName, hw.GpuName) is { Length: > 0 } hardwareProblem)
        {
            hw.Dispose();
            return PreflightResult.Stop(hardwareProblem);
        }

        // The configured names may be left over from a machine of another vendor — the temperature
        // would then silently turn into a dash.
        if (PlatformGuard.DescribeSensorNames(hw.CpuName, cfg.Sensors.CpuTemp) is { Length: > 0 } namesProblem)
            return PreflightResult.Stop(namesProblem, hw);

        // 6. Fans: not found means carrying on without their cells.
        bool fansScanned = cfg.Fans.IsEmpty;
        if (fansScanned) ScanFans(cfg, hw);

        Log.Info($"fans: {cfg.Fans.Summary.Replace("\n", "; ")}");

        // 7. The write is collected here rather than in the branches: there are several reasons to save.
        if (fansScanned || switchWasMissing || sectionsMissing) SaveConfig(cfg);

        return PreflightResult.Start(cfg, hw);
    }

    // Failing here does not stop the work.
    private static void SaveConfig(AppConfig cfg)
    {
        bool existed = cfg.LoadedFromFile;
        string? path = cfg.SaveSomewhere();

        if (path is null)
        {
            Log.Warn("could not write config.conf — the settings apply until restart only. " +
                     "Check write access next to the exe and in " + AppConfig.UserPath);
            return;
        }

        Log.Event($"config file {(existed ? "updated" : "created")}: {path}");
        Log.Info($"if the fan layout is wrong, fix it in the same file; reference: {ConfigDoc}");
    }

    private static void ScanFans(AppConfig cfg, HardwareMonitor hw)
    {
        IReadOnlyList<FanSensor> fans;
        try
        {
            fans = hw.ScanFans();
        }
        catch (Exception ex)
        {
            Log.Warn($"could not read the fan sensors: {ex.Message}");
            return;
        }

        bool found = cfg.Fans.ApplyDetected(fans);
        Log.Info($"fan scan: {fans.Count} found");
        foreach (FanSensor fan in fans) Log.Info($"  {fan.Describe}");

        if (!found)
        {
            Log.Warn("no fan sensors were found at all. The monitoring chip of this motherboard " +
                     "is apparently not supported by the sensor library, or the PawnIO driver is " +
                     "missing. Everything except the fan cells keeps working.");
            LogSensors(hw);
            return;
        }

        // From outside "not found" and "blocked by the driver" look the same — hence the explanation.
        if (cfg.Fans.Cpu.Count == 0 || cfg.Fans.Aio.Count == 0)
        {
            Log.Warn("some fans were not detected: " +
                     $"CPU {(cfg.Fans.Cpu.Count == 0 ? "no" : "yes")}, " +
                     $"AIO {(cfg.Fans.Aio.Count == 0 ? "no" : "yes")}");

            if (SensorDriver.IsPawnIoInstalled() == false)
                Log.Warn("almost certainly PawnIO is missing — see the message above");
            else
                Log.Warn("PawnIO is installed, so it is not the cause: check the full sensor " +
                         "list — the monitoring chip of this board may not be supported.");

            LogSensors(hw);
        }
    }

    // Everything the sensor library sees, into the log. A file of its own would be one more thing
    // to find and send: the fan lines above are already here, and this belongs with them.
    private static void LogSensors(HardwareMonitor hw)
    {
        try
        {
            Log.Info("full sensor list:");

            foreach (string line in hw.DumpSensors()
                                      .Split('\n', StringSplitOptions.RemoveEmptyEntries))
                Log.Info("  " + line.TrimEnd());
        }
        catch (Exception ex)
        {
            Log.Error("the sensor list was not read", ex);
        }
    }
}

// Administrator rights are required: without them the kernel driver will not load and the ETW
// session will not start, so there would be no temperatures, no fan speeds and no FPS.
internal static class Elevation
{
    // Whether the current process has administrator rights.
    public static bool IsElevated { get; } = CheckElevated();

    private static bool CheckElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex)
        {
            // Could not tell — assume no rights: an extra UAC prompt beats a panel of dashes.
            Debug.WriteLine($"the rights check failed: {ex.Message}");
            return false;
        }
    }

    // Restarts the app elevated, passing the same arguments.
    public static bool TryRelaunchElevated(out string? problem)
    {
        problem = null;

        // For single-file this is the path to the exe itself, not to the temporary unpack.
        string? exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            problem = "Could not determine the path to the overlay's own exe for a restart.\n\n" +
                      $"Start {AppParameters.Identity.Name} as administrator manually.";
            return false;
        }

        var start = new ProcessStartInfo(exe)
        {
            // Without ShellExecute the runas verb does nothing: the shell elevates, not the process.
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = AppContext.BaseDirectory
        };

        foreach (string arg in Environment.GetCommandLineArgs().Skip(1))
            start.ArgumentList.Add(arg);

        try
        {
            Process.Start(start);
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            problem = "Without administrator rights the overlay has nothing to show: " +
                      "temperatures, power, fan speeds and the frame counter are only readable " +
                      "with them.\n\nStart it again and confirm the UAC prompt.";
            return false;
        }
        catch (Exception ex)
        {
            problem = $"Could not restart with administrator rights: {ex.Message}\n\n" +
                      "Start " + AppParameters.Identity.Name + " as administrator manually.";
            return false;
        }
    }

    // ERROR_CANCELLED — the user dismissed the UAC prompt.
    private const int ErrorCancelled = 1223;
}

// Autostart through a Task Scheduler task, not a Startup folder shortcut: the app needs
// administrator rights, and a task with highest privileges is asked about once, at creation.
internal static class AutoStart
{
    public static bool IsEnabled() => Run("/Query", "/TN", AppParameters.Identity.TaskName) == 0;

    // Creates or recreates the task.
    public static string? Enable()
    {
        string? exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return Text.NoOwnExePath;

        // /RL HIGHEST — with highest privileges, the whole point of this.
        // /F — replace an existing task silently: simpler than comparing its parameters.
        int code = Run("/Create", "/TN", AppParameters.Identity.TaskName, "/TR", $"\"{exe}\"",
                       "/SC", "ONLOGON", "/RL", "HIGHEST", "/F");

        if (code == 0)
        {
            Log.Event($"autostart enabled: task \"{AppParameters.Identity.TaskName}\" → {exe}");
            return null;
        }

        Log.Warn($"could not create the autostart task, schtasks returned code {code}");
        return Text.SchedulerRefused(code);
    }

    // Removes the task. Returns an error text, or null on success.
    public static string? Disable()
    {
        int code = Run("/Delete", "/TN", AppParameters.Identity.TaskName, "/F");

        if (code == 0)
        {
            Log.Event("autostart disabled: task removed");
            return null;
        }

        Log.Warn($"could not remove the autostart task, schtasks returned code {code}");
        return Text.SchedulerRefused(code);
    }

    // Runs schtasks without a console window and returns its exit code.
    private static int Run(params string[] arguments)
    {
        var start = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (string argument in arguments) start.ArgumentList.Add(argument);

        try
        {
            using Process? process = Process.Start(start);
            if (process is null) return -1;

            // The streams are drained: a process with long output can stall on a full buffer.
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();

            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Log.Error("could not start schtasks", ex);
            return -1;
        }
    }
}

// Says out loud that the app did not start: one that vanishes without a window looks broken
// rather than refused, so the reason goes to the action centre and a click on it opens the log.
internal static class StartupNotice
{
    // How long the icon stays alive for the notification to be raised and clicked.
    private static readonly TimeSpan Linger = TimeSpan.FromSeconds(30);

    // How long the tray icon is left to settle before the balloon is asked for.
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(700);

    public static void Show(string reason)
    {
        try
        {
            // The language of the system: the config may not have been read at all, and this is
            // the one moment the user is being told something without a window.
            Text.Use(Language.Auto);

            // Notifications are off in Windows: that is the user's decision, and a window forced
            // on them instead would be worse than silence. The reason stays in the log.
            if (!Notifications.AreEnabled())
            {
                Log.Warn("notifications are off in Windows — the reason for the refusal is in " +
                         "this log only");
                System.Windows.Application.Current?.Shutdown();
                return;
            }

            Icon picture = LoadIcon();

            var icon = new NotifyIcon
            {
                Icon = picture,
                Visible = true,
                Text = AppParameters.Identity.Name
            };

            string log = Log.Path ?? "";

            icon.BalloonTipTitle = AppParameters.Identity.Name;
            icon.BalloonTipText = Text.StartupFailed(reason);
            icon.BalloonTipIcon = ToolTipIcon.Warning;
            icon.BalloonTipClicked += (_, _) => Open(log);

            // Not straight away: the icon has just been handed to the tray, and a balloon asked
            // for in the same breath is dropped — the shell has not finished adding it yet.
            var delay = new System.Windows.Threading.DispatcherTimer { Interval = Settle };
            delay.Tick += (_, _) =>
            {
                delay.Stop();
                icon.ShowBalloonTip((int)Linger.TotalMilliseconds);
                Log.Warn($"startup notification shown: {reason}");
            };
            delay.Start();

            // The icon must outlive the message: hidden at once, and Windows drops the balloon
            // with it. A timer of its own, because there is no window loop to hang this on.
            var life = new System.Windows.Threading.DispatcherTimer { Interval = Linger };
            life.Tick += (_, _) =>
            {
                life.Stop();
                icon.Visible = false;
                icon.Dispose();

                // NotifyIcon does not free the icon itself, and the shared system one must not
                // be freed at all.
                if (!ReferenceEquals(picture, SystemIcons.Warning)) picture.Dispose();

                System.Windows.Application.Current?.Shutdown();
            };
            life.Start();
        }
        catch (Exception ex)
        {
            // The refusal itself is already in the log; failing to announce it changes nothing.
            Log.Warn($"the startup notification was not shown: {ex.Message}");
            System.Windows.Application.Current?.Shutdown();
        }
    }

    private static void Open(string log)
    {
        if (log.Length == 0 || !File.Exists(log)) return;

        try
        {
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{log}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"{log} did not open in the editor", ex);
        }
    }

    private static Icon LoadIcon()
    {
        try
        {
            using Stream? stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(AppParameters.Identity.IconResource);

            return stream is null ? SystemIcons.Warning : new Icon(stream, SystemInformation.SmallIconSize);
        }
        catch (Exception ex)
        {
            Log.Warn($"the notification icon did not load: {ex.Message}");
            return SystemIcons.Warning;
        }
    }
}

// Asks GitHub whether a newer release exists. The same idea as the macOS version: one quiet check
// a day, plus a manual one from the tray menu.
internal static class UpdateChecker
{
    // What the check came back with. Nothing is downloaded or installed — the answer is a version
    // number and the page to get it from.
    internal sealed record Result(string Current, string Latest, bool IsNewer);

    // GitHub refuses a request without a User-Agent, and the header is also how the traffic is
    // recognised on their side.
    private static readonly HttpClient Http = new() { Timeout = AppParameters.Network.RequestTimeout };

    static UpdateChecker()
    {
        Http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue(AppParameters.Identity.AppFolder, AppParameters.Identity.Version));

        // Without it the API answers with whatever it feels like; this pins the shape of the JSON.
        Http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public static async Task<Result?> Check()
    {
        try
        {
            using HttpResponseMessage answer = await Http.GetAsync(AppParameters.Links.LatestReleaseApi);

            // Nothing published yet: that is not a failure, it is "no updates".
            if (answer.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Log.Info("update check: no releases published yet");
                return new Result(AppParameters.Identity.Version, AppParameters.Identity.Version, IsNewer: false);
            }

            answer.EnsureSuccessStatusCode();
            string json = await answer.Content.ReadAsStringAsync();

            using JsonDocument document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("tag_name", out JsonElement tag)) return null;

            string latest = (tag.GetString() ?? "").TrimStart('v', 'V');
            if (latest.Length == 0) return null;

            string current = AppParameters.Identity.Version;
            bool newer = IsNewer(latest, current);

            // A release worth acting on is kept whatever Debug says — the daily check is the only
            // place it is ever said. "Nothing new" is part of the course of work and goes with it.
            if (newer) Log.Event($"update available: running {current}, latest {latest}");
            else Log.Info($"update check: running {current}, latest {latest}");

            return new Result(current, latest, newer);
        }
        catch (Exception ex)
        {
            // No network, a rate limit, a rewritten API — none of that is worth a crash.
            Log.Warn($"the update check did not go through: {ex.Message}");
            return null;
        }
    }

    // Compared as numbers rather than as text: "0.10.0" is newer than "0.9.0", though it sorts
    // before it. Anything that will not parse is treated as no news.
    internal static bool IsNewer(string latest, string current) =>
        Version.TryParse(latest, out Version? l) &&
        Version.TryParse(current, out Version? c) &&
        Padded(l) > Padded(c);

    // "1.1" and "1.1.0" are one and the same: a number left off counts as zero, not as less.
    private static Version Padded(Version v) =>
        new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
}
