//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

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
        // In the CPU slot it beats a dash — on boards where the cooler hangs off SYS_FAN it is
        // exactly the right one.
        Aio = Pick(fans, FanRole.Aio);
        Cpu = cpu.Concat(rest).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (Extra.Count == 0) Extra = Case(fans, shownAsCpu: Cpu.FirstOrDefault());

        return true;
    }

    // The case fans that deserve a cell. Boards that call every header "Fan #1" give no CPU fan
    // at all, so all seven land in the processor slot and only the first of them is ever read —
    // the rest would be lost without this. Silent ones are left out: an empty header reads as
    // a steady zero, and five such cells would say nothing.
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
