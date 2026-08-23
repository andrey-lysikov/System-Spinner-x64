//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SystemSpinnerX64.Platform;

// The app targets Windows 11 x64 and an Intel or AMD processor.
internal static class PlatformGuard
{
    // Describes the Windows version mismatch, or null when it is fine.
    public static string? DescribeOs()
    {
        Version v = Environment.OSVersion.Version;
        if (v.Major > 10 || (v.Major == 10 && v.Build >= AppParameters.Requirements.Windows11Build)) return null;

        return $"Windows 11 or newer is required, but this is Windows {v.Major}, build {v.Build}.\n\n" +
               "On Windows 10 some sensors are named differently, the Present events used to count " +
               "frames work another way, and the tray has no per-monitor scaling for its icons.";
    }

    // Names come from LibreHardwareMonitor: "Intel Core Ultra 7 265K", "AMD Ryzen 9 7950X".
    public static string? DescribeHardware(string? cpuName, string? gpuName)
    {
        // Empty means the sensors did not open; a separate message already says so.
        if (string.IsNullOrWhiteSpace(cpuName)) return null;

        if (!IsIntel(cpuName) && !IsAmd(cpuName))
        {
            return $"The CPU was detected as \"{cpuName}\" — this build covers Intel and AMD.\n\n" +
                   "Temperature, power and the per-core clock are read by sensor name, and the " +
                   "defaults here are the ones those two report, so the values would stay empty.";
        }

        // The card is only reported: it can be any, and that is no reason to refuse.
        _ = gpuName;
        return null;
    }

    public static bool IsIntel(string cpuName) =>
        cpuName.IndexOf("intel", StringComparison.OrdinalIgnoreCase) >= 0;

    public static bool IsAmd(string cpuName) =>
        cpuName.IndexOf("amd", StringComparison.OrdinalIgnoreCase) >= 0 ||
        cpuName.IndexOf("ryzen", StringComparison.OrdinalIgnoreCase) >= 0 ||
        cpuName.IndexOf("threadripper", StringComparison.OrdinalIgnoreCase) >= 0;

    // How AMD sensor names are recognised: that processor reports its temperature only as Tctl,
    // Tdie or CCD, and Intel never uses those names.
    private static readonly Regex AmdTempName = new(@"Tctl|Tdie|CCD",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Whether the configured names suit the processor that was found. A config carried over from
    // another machine is the usual way to end up with a dash where the temperature should be.
    public static string? DescribeSensorNames(string? cpuName, IReadOnlyList<string> cpuTemp)
    {
        if (cpuName is not { Length: > 0 }) return null;

        // An empty list is a deliberate "do not show the temperature", like "Aio =" for the pump.
        // The check catches foreign names, not their absence.
        if (cpuTemp.Count == 0) return null;

        bool intel = IsIntel(cpuName);
        bool amd = IsAmd(cpuName);

        // Anything else was already refused, and a name we do not know is no reason to complain.
        if (!intel && !amd) return null;

        // Intel needs at least one name without Tctl/Tdie/CCD; AMD needs at least one with them.
        bool suits = intel
            ? cpuTemp.Any(name => !AmdTempName.IsMatch(name))
            : cpuTemp.Any(name => AmdTempName.IsMatch(name));

        if (suits) return null;

        string wanted = intel
            ? "    CpuTemp = CPU Package, Core Max, CPU Cores"
            : "    CpuTemp = Core (Tctl/Tdie), CPU Package, Core (Tctl)";

        return $"The CPU was detected as \"{cpuName}\", but CpuTemp in the config holds only " +
               $"{(intel ? "AMD" : "Intel")} sensor names — the temperature would show a dash.\n" +
               $"Configured names: {string.Join(", ", cpuTemp)}\n\n" +
               "Add a name that fits, for example:\n" +
               wanted + "\n" +
               "or delete the CpuTemp line — the defaults cover both. Deleting config.conf works " +
               "too: it will be created anew.";
    }
}
