using System;
using System.Collections.Generic;

namespace SystemSpinnerX64.Monitoring;

/// <summary>Every reading from one poll. null means the sensor was not found.</summary>
public sealed class Readings
{
    public double? CpuLoad { get; set; }
    public double? CpuTempC { get; set; }
    public double? CpuPowerW { get; set; }
    public double? CpuClockMhz { get; set; }
    public double? SysMemUsedGb { get; set; }

    /// <summary>Free memory. The status window needs it; the in-game panel does not show it.</summary>
    public double? SysMemFreeGb { get; set; }

    /// <summary>Page file in use. Only read while the status window is open — see SwapMonitor.</summary>
    public double? SwapUsedGb { get; set; }

    public double? SwapTotalGb { get; set; }

    public double? CpuFanRpm { get; set; }
    public double? AioFanRpm { get; set; }

    /// <summary>Extra fans from the config, in the order they are listed there.</summary>
    public IReadOnlyList<double?> ExtraFanRpm { get; set; } = Array.Empty<double?>();

    public double? GpuLoad { get; set; }
    public double? GpuTempC { get; set; }
    public double? GpuPowerW { get; set; }
    public double? GpuClockMhz { get; set; }
    public double? GpuMemUsedGb { get; set; }
    public double? GpuFanRpm { get; set; }

    /// <summary>Total video memory. Only the status window scale needs it.</summary>
    public double? GpuMemTotalGb { get; set; }

    /// <summary>Used memory as a percentage of installed, or null when the total is unknown.</summary>
    public double? MemLoadPercent =>
        SysMemUsedGb is double used && SysMemFreeGb is double free && used + free > 0
            ? used / (used + free) * 100.0
            : null;

    /// <summary>Page file in use as a percentage of its size.</summary>
    public double? SwapLoadPercent =>
        SwapUsedGb is double used && SwapTotalGb is double total && total > 0
            ? used / total * 100.0
            : null;

    /// <summary>Used video memory as a percentage of the whole.</summary>
    public double? GpuMemLoadPercent =>
        GpuMemUsedGb is double used && GpuMemTotalGb is double total && total > 0
            ? used / total * 100.0
            : null;
}
