using System;
using System.Collections.Generic;

namespace SystemSpinnerX64.Monitoring;

// Every reading from one poll. null means the sensor was not found.
public sealed class Readings
{
    public double? CpuLoad { get; set; }
    public double? CpuTempC { get; set; }
    public double? CpuPowerW { get; set; }
    public double? CpuClockMhz { get; set; }
    public double? SysMemUsedGb { get; set; }

    // Free memory. The status window needs it; the in-game panel does not show it.
    public double? SysMemFreeGb { get; set; }

    // Page file in use. Only read while the status window is open — see SwapMonitor.
    public double? SwapUsedGb { get; set; }

    public double? SwapTotalGb { get; set; }

    public double? CpuFanRpm { get; set; }
    public double? AioFanRpm { get; set; }

    // Extra fans from the config, in the order they are listed there.
    public IReadOnlyList<double?> ExtraFanRpm { get; set; } = Array.Empty<double?>();

    public double? GpuLoad { get; set; }
    public double? GpuTempC { get; set; }
    public double? GpuPowerW { get; set; }
    public double? GpuClockMhz { get; set; }
    public double? GpuMemUsedGb { get; set; }
    public double? GpuFanRpm { get; set; }

    // Total video memory. Only the status window scale needs it.
    public double? GpuMemTotalGb { get; set; }

    // Whether the card has memory of its own. Integrated graphics take it from the system: there
    // is no separate amount to show, and a scale of it would repeat the memory row.
    public bool GpuHasOwnMemory => GpuMemTotalGb is double total && total > 0;

    // The busier of the two loads. This is what the tray icon spins by: a game leans on the card
    // while the processor idles, and an icon standing still would then say nothing is happening.
    public double BusiestLoad => Math.Max(CpuLoad ?? 0, GpuLoad ?? 0);

    // Used memory as a percentage of installed, or null when the total is unknown.
    public double? MemLoadPercent =>
        SysMemUsedGb is double used && SysMemFreeGb is double free && used + free > 0
            ? used / (used + free) * 100.0
            : null;

    // Page file in use as a percentage of its size.
    public double? SwapLoadPercent =>
        SwapUsedGb is double used && SwapTotalGb is double total && total > 0
            ? used / total * 100.0
            : null;

    // Used video memory as a percentage of the whole.
    public double? GpuMemLoadPercent =>
        GpuMemUsedGb is double used && GpuMemTotalGb is double total && total > 0
            ? used / total * 100.0
            : null;
}
