namespace SystemSpinnerX64.Configuration;

/// <summary>
/// How the in-game overlay looks; parameter descriptions live in sample.conf. The position is
/// not configurable: the panel is always in the top-left corner, and its size is in WPF units,
/// so it does not depend on the resolution.
/// </summary>
public sealed class AppearanceConfig
{
    /// <summary>Several comma-separated names — the first one present in the system wins.</summary>
    public string FontFamily { get; set; } = "Impact, Haettenschweiler, Arial Narrow, Arial";

    public string TextColor { get; set; } = "#FFFFFF";

    public double TextOpacity { get; set; } = 0.85;

    /// <summary>
    /// Dark backdrop, off by default: over a game it reads as a rectangle across the picture,
    /// and the shadow already protects the text from bright frames.
    /// </summary>
    public bool ShowPanel { get; set; }

    public string PanelColor { get; set; } = "#0B0D12";

    public double PanelOpacity { get; set; } = 0.45;

    /// <summary>
    /// Shadow blur. Without a backdrop this is the only thing separating the digits from bright
    /// frames: small values outline the letters, large ones give a soft blob. 0 means no shadow.
    /// </summary>
    public double ShadowBlur { get; set; } = 3;

    public double ShadowOpacity { get; set; } = 0.9;

    /// <summary>Font size as a percentage of the computed one. Accepted from 50 to 300.</summary>
    public double FontScalePercent { get; set; } = 100;

    /// <summary>Unit label size as a percentage of the value size.</summary>
    public double UnitSizePercent { get; set; } = 55;

    /// <summary>Offset from the top-left corner of the work area, in WPF units.</summary>
    public double Margin { get; set; } = 10;
}
