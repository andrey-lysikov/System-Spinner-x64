//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using SystemSpinnerX64.Configuration;
using SystemSpinnerX64.Diagnostics;
using SystemSpinnerX64.Localization;
using SystemSpinnerX64.Monitoring;
using SystemSpinnerX64.Platform;

namespace SystemSpinnerX64.Startup;

// Everything that has to be checked happens here, before the tray icon appears.
internal static class Preflight
{
    private const string ConfigDoc = "sample.conf in the project root";

    public static PreflightResult Run()
    {
        // 1. The Windows version first: without it there is no point opening the sensors.
        if (PlatformGuard.DescribeOs() is { Length: > 0 } osProblem)
            return PreflightResult.Stop(osProblem);

        Log.Info($"Windows {Environment.OSVersion.Version}");

        // A remote desktop explains at a glance half of what the log shows afterwards: one screen
        // instead of the monitors on the card, no brightness, no HDR. Without this line all of it
        // reads as a failure.
        if (Win32.IsRemoteSession)
            Log.Info("remote session: the desktop is on the virtual display of the remote adapter — " +
                     "the monitors on the graphics card are out of reach, and with them DDC/CI and HDR");

        // 2. The config.
        var cfg = AppConfig.Load();
        Log.Info(cfg.LoadedFromFile ? $"config loaded: {cfg.Path}" : "no config, using defaults");

        // The interface language right after reading the file: the tray menu is built later.
        Text.Use(cfg.Language);

        // A file left by an older version next to the exe is no longer read — staying silent would
        // mean edits in it simply have no effect.
        if (!AppConfig.PortableAllowed)
        {
            Log.Info($"the exe is in a system folder — config and log go to {AppConfig.FallbackDirectory}");

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
            Log.Info($"sensors opened: CPU \"{hw.CpuName}\", GPU \"{hw.GpuName}\"");

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
        if (fansScanned || switchWasMissing) SaveConfig(cfg);

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

        Log.Info($"config file {(existed ? "updated" : "created")}: {path}");
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
