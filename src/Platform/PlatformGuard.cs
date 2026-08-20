using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SystemSpinnerX64.Platform;

/// <summary>
/// The app targets Windows 11 x64 and an Intel processor. There is no way around it: no match
/// means a line in the log and an exit.
///
/// The graphics card is deliberately not checked, unlike in GameOverlay: LibreHardwareMonitor
/// names load, temperature, clock and memory the same way for Intel, NVIDIA and AMD — they come
/// from the vendor driver rather than being guessed per architecture. Refusing to start over the
/// card would be refusing for no reason; which card was found is in the log.
/// </summary>
internal static class PlatformGuard
{
    /// <summary>Describes the Windows version mismatch, or null when it is fine.</summary>
    public static string? DescribeOs()
    {
        Version v = Environment.OSVersion.Version;
        if (v.Major > 10 || (v.Major == 10 && v.Build >= AppParameters.Requirements.Windows11Build)) return null;

        return $"Windows 11 or newer is required, but this is Windows {v.Major}, build {v.Build}.\n\n" +
               "On Windows 10 some sensors are named differently, the Present events used to count " +
               "frames work another way, and the tray has no per-monitor scaling for its icons.";
    }

    /// <summary>Names come from LibreHardwareMonitor: "Intel Core Ultra 7 265K".</summary>
    public static string? DescribeHardware(string? cpuName, string? gpuName)
    {
        // Empty means the sensors did not open; a separate message already says so.
        if (string.IsNullOrWhiteSpace(cpuName)) return null;

        if (!IsIntel(cpuName))
        {
            return $"The CPU was detected as \"{cpuName}\" — this build targets Intel.\n\n" +
                   "Temperature, power and the per-core clock are read by sensor name, and the " +
                   "defaults here are the Intel ones (CPU Package, P-Core, E-Core), so those " +
                   "values would stay empty. GameOverlay covers AMD as well.";
        }

        // The card is only reported: it can be any, and that is no reason to refuse.
        _ = gpuName;
        return null;
    }

    public static bool IsIntel(string cpuName) =>
        cpuName.IndexOf("intel", StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>
    /// How AMD sensor names are recognised: that processor reports its temperature only as Tctl,
    /// Tdie or CCD, and Intel never uses those names.
    /// </summary>
    private static readonly Regex AmdTempName = new(@"Tctl|Tdie|CCD",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Whether the configured names suit the processor that was found. Needed because the file
    /// travels between machines: the temperature would silently become a dash, and a dash reads as
    /// "the sensor is silent" rather than "it is looked up under the wrong name". Hence a refusal.
    /// </summary>
    public static string? DescribeSensorNames(string? cpuName, IReadOnlyList<string> cpuTemp)
    {
        if (cpuName is not { Length: > 0 }) return null;

        // An empty list is a deliberate "do not show the temperature", like "Aio =" for the pump.
        // The check catches foreign names, not their absence.
        if (cpuTemp.Count == 0) return null;
        if (!IsIntel(cpuName)) return null;

        // At least one name without Tctl/Tdie/CCD is needed — one that Intel can actually report.
        if (cpuTemp.Any(name => !AmdTempName.IsMatch(name))) return null;

        return $"The CPU was detected as \"{cpuName}\", but Sensors.CpuTemp in the config holds " +
               "only AMD sensor names — the temperature would show a dash.\n" +
               $"Configured names: {string.Join(", ", cpuTemp)}\n\n" +
               "This happens with a config written on an AMD machine. Add an Intel name at the " +
               "front, for example:\n" +
               "    CpuTemp = CPU Package, Core Max, CPU Cores\n" +
               "or delete the CpuTemp line entirely — the defaults are the Intel ones. Deleting " +
               "config.conf works too: it will be created anew.";
    }
}
