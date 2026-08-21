using System;
using System.Collections.Generic;
using System.Linq;
using SystemSpinnerX64.Monitoring;

namespace SystemSpinnerX64.Configuration;

// Fan sensor names. Every board names its headers differently, so while the lists are empty the app
// scans the hardware and fills them in itself.
public sealed class FanConfig
{
    public List<string> Cpu { get; set; } = new();

    public List<string> Aio { get; set; } = new();

    public List<string> Gpu { get; set; } = new();

    // The user's own list — auto-detection never touches it.
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
        // In the CPU slot it beats a dash — on boards where the cooler hangs off SYS_FAN it is
        // exactly the right one.
        Aio = Pick(fans, FanRole.Aio);
        Cpu = cpu.Concat(rest).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return true;
    }

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
        $"CPU: {Describe(Cpu)}\nAIO: {Describe(Aio)}\nGPU: {Describe(Gpu)}";

    private static string Describe(IReadOnlyList<string> names) =>
        names.Count == 0 ? "not found" : string.Join(", ", names);
}
