namespace SystemSpinnerX64.Configuration;

/// <summary>
/// Highlighting for values past a threshold; zero disables one. One colour for all of them: the
/// point is to catch the eye, and one difference from the rest is enough.
/// </summary>
public sealed class WarnConfig
{
    public string Color { get; set; } = "#FF6A52";

    public double CpuTemp { get; set; } = 85;
    public double GpuTemp { get; set; } = 83;

    /// <summary>Used memory, per cent of the installed total. Colours the bar in the status window.</summary>
    public double SysMem { get; set; } = 90;

    /// <summary>Used video memory, per cent of the whole.</summary>
    public double GpuMem { get; set; } = 90;

    /// <summary>Page file in use, per cent of its size.</summary>
    public double Swap { get; set; } = 90;

    /// <summary>
    /// Load thresholds. Not in the config and left at zero, which means off: 99 % in a game is
    /// normal rather than alarming, and a highlight that is always on says nothing.
    /// </summary>
    public double CpuLoad { get; set; }

    public double GpuLoad { get; set; }
}
