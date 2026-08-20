using System.Collections.Generic;

namespace SystemSpinnerX64.Configuration;

/// <summary>Frame counter settings. Parameter descriptions live in sample.conf.</summary>
public sealed class FpsConfig
{
    public bool Enabled { get; set; } = true;

    public double AvgWindowSeconds { get; set; } = 1.0;

    public bool IncludeVulkanOpenGl { get; set; } = true;

    /// <summary>
    /// Plausibility ceiling: an event arriving more often fires several times per frame and
    /// cannot be a frame counter. Set well above what any game produces.
    /// </summary>
    public double MaxPlausibleFps { get; set; } = 1500;

    /// <summary>
    /// DxgKrnl events in order of preference; the first one the game actually sends wins.
    /// Measured on Windows 11 24H2: in a borderless window frames are marked by
    /// PresentHistoryDetailed and Present never arrives, but it is kept for full-screen cases.
    /// QueuePacket is left out on purpose — 95 000 events against 14 000, several per frame.
    /// </summary>
    public List<string> DxgKrnlTasks { get; set; } = new()
    {
        "PresentHistoryDetailed", "PresentHistory", "Present", "Flip"
    };
}
