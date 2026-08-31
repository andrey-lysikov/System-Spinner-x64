//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;

namespace SystemSpinnerX64.Configuration;

// Sensor names the readings are taken by.
public sealed class SensorNamesConfig
{
    public List<string> CpuLoad { get; set; } = new() { "CPU Total" };

    // Intel names first, then the AMD ones: whichever the machine reports is the one that wins,
    // so one list serves both.
    public List<string> CpuTemp { get; set; } = new()
    {
        "CPU Package", "Core Max", "CPU Cores", "P-Core Max",
        "Core (Tctl/Tdie)", "Core (Tctl)", "Core (Tdie)", "CCDs Max (Tdie)", "CCD1 (Tdie)",
        "Package"
    };

    public List<string> CpuPower { get; set; } = new()
    {
        "CPU Package", "CPU Cores", "Package", "Core (SVI2 TFN)", "CPU SoC"
    };

    // Word that selects the cores the clock is averaged over. Hybrid Intel names them "P-Core #1";
    // when nothing matches, every core except the excluded ones is taken — which is what AMD and
    // plain Intel need, where they are just "Core #1".
    public string CpuClockCores { get; set; } = "P-Core";

    public List<string> MemoryUsed { get; set; } = new() { "Memory Used" };

    // Free memory. With the used part it gives the installed total, which LHM does not report.
    public List<string> MemoryAvailable { get; set; } = new() { "Memory Available" };

    public List<string> GpuLoad { get; set; } = new() { "GPU Core", "D3D 3D" };

    public List<string> GpuTemp { get; set; } = new() { "GPU Core", "GPU Hot Spot", "GPU Temperature" };

    public List<string> GpuPower { get; set; } = new() { "GPU Package", "GPU Power", "GPU PPT" };

    public List<string> GpuClock { get; set; } = new() { "GPU Core", "GPU Graphics" };

    // Used video memory. The Direct3D counter comes first on purpose: it is the one Windows shows
    // in the Task Manager, and it follows the memory back down when an application lets it go.
    // "GPU Memory Used" is the card's own count of what is not free, and on NVIDIA that figure has
    // been seen to stick at the peak — a card left reading full while nothing is running.
    public List<string> GpuMemory { get; set; } = new() { "D3D Dedicated Memory Used", "GPU Memory Used", "GPU Memory Dedicated Used" };

    // What the list above said before, and what a config file written by an older version still
    // carries. Replaced on reading: the value it names goes stale, and nobody chose it on purpose.
    internal static readonly string[] StaleGpuMemory = { "GPU Memory Used", "D3D Dedicated Memory Used", "GPU Memory Dedicated Used" };

    // Total video memory. Only the status window needs it: without a ceiling the megabytes have
    // nothing to be compared against, and a scale without one is meaningless.
    public List<string> GpuMemoryTotal { get; set; } = new() { "GPU Memory Total", "D3D Dedicated Memory Total" };
}
