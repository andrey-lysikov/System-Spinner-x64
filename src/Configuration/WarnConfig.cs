namespace SystemSpinnerX64.Configuration;

// Highlighting for values past a threshold; zero disables one.
public sealed class WarnConfig
{
    public string Color { get; set; } = "#FF6A52";

    public double CpuTemp { get; set; } = 85;
    public double GpuTemp { get; set; } = 83;

    // Used memory, per cent of the installed total.
    public double SysMem { get; set; } = 90;

    // Used video memory, per cent of the whole.
    public double GpuMem { get; set; } = 90;

    // Page file in use, per cent of its size.
    public double SwapMem { get; set; } = 90;

    // Load, per cent. Higher than the memory thresholds: in a game the load sits near the top all
    // the time, and a highlight that is always on says nothing.
    public double CpuUsage { get; set; } = 95;

    public double GpuUsage { get; set; } = 95;
}
