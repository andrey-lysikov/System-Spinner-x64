//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;

namespace SystemSpinnerX64.Configuration;

// How the in-game overlay looks; parameter descriptions live in sample.conf.
public sealed class AppearanceConfig
{
    // Several comma-separated names — the first one present in the system wins.
    public string FontFamily { get; set; } = "Impact, Haettenschweiler, Arial Narrow, Arial";

    public string TextColor { get; set; } = "#FFFFFF";

    public double TextOpacity { get; set; } = 0.85;

    // Dark backdrop, off by default: over a game it reads as a rectangle across the picture, and
    // the shadow already protects the text from bright frames.
    public bool ShowPanel { get; set; }

    public string PanelColor { get; set; } = "#0B0D12";

    public double PanelOpacity { get; set; } = 0.45;

    // Shadow blur. Without a backdrop this is the only thing separating the digits from bright
    // frames: small values outline the letters, large ones give a soft blob.
    public double ShadowBlur { get; set; } = 3;

    public double ShadowOpacity { get; set; } = 0.9;

    // Font size as a percentage of the computed one.
    public double FontScalePercent { get; set; } = 100;

    // Unit label size as a percentage of the value size.
    public double UnitSizePercent { get; set; } = 55;

    // Offset from the top-left corner of the work area, in WPF units.
    public double Margin { get; set; } = 10;

    // The rows and the order of the values along them — Row1, Row2, … in the file.
    public List<OverlayRow> Rows { get; set; } = OverlayRow.Default();
}
