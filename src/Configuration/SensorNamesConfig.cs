using System.Collections.Generic;

namespace SystemSpinnerX64.Configuration;

/// <summary>
/// Sensor names the readings are taken by. They are settings rather than constants because the
/// naming depends on the hardware generation and the library version, and a wrong name shows up
/// as a silent dash.
///
/// Each list is tried for an exact match first, then for a substring; the first hit wins. What
/// the library sees on this machine goes to the log at Info.
///
/// The CPU names are the Intel ones. The GPU names are shared: Intel, NVIDIA and AMD all report
/// load, temperature and clock the same way.
/// </summary>
public sealed class SensorNamesConfig
{
    public List<string> CpuLoad { get; set; } = new() { "CPU Total" };

    public List<string> CpuTemp { get; set; } = new()
    {
        "CPU Package", "Core Max", "CPU Cores", "P-Core Max", "Package"
    };

    public List<string> CpuPower { get; set; } = new()
    {
        "CPU Package", "CPU Cores", "Package"
    };

    /// <summary>
    /// Word that selects the cores the clock is averaged over. Efficient cores are excluded:
    /// they run noticeably slower and would drag the average down.
    /// </summary>
    public string CpuClockCores { get; set; } = "P-Core";

    /// <summary>What excludes the efficient cores when there are no explicit P-cores.</summary>
    public string CpuClockExclude { get; set; } = "E-Core";

    public List<string> MemoryUsed { get; set; } = new() { "Memory Used" };

    /// <summary>Free memory. With the used part it gives the installed total, which LHM does not report.</summary>
    public List<string> MemoryAvailable { get; set; } = new() { "Memory Available" };

    public List<string> GpuLoad { get; set; } = new() { "GPU Core", "D3D 3D" };

    public List<string> GpuTemp { get; set; } = new() { "GPU Core", "GPU Hot Spot", "GPU Temperature" };

    public List<string> GpuPower { get; set; } = new() { "GPU Package", "GPU Power", "GPU PPT" };

    public List<string> GpuClock { get; set; } = new() { "GPU Core", "GPU Graphics" };

    public List<string> GpuMemory { get; set; } = new() { "GPU Memory Used", "D3D Dedicated Memory Used", "GPU Memory Dedicated Used" };

    /// <summary>
    /// Total video memory. Only the status window needs it: without a ceiling the megabytes have
    /// nothing to be compared against, and a scale without one is meaningless.
    /// </summary>
    public List<string> GpuMemoryTotal { get; set; } = new() { "GPU Memory Total", "D3D Dedicated Memory Total" };
}
