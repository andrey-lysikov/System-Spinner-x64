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

    // Full-screen applications the panel is not shown over: exe names with * and ? in them.
    public List<string> BlackListApplications { get; set; } = DefaultBlackList();

    // What goes full screen without being a game: screenshot tools, the lock screen, players,
    // browsers with a video, a slide show, a remote desktop, video calls. Anyone can cross a name off.
    public static List<string> DefaultBlackList() => new()
    {
        "SnippingTool*", "ScreenClippingHost", "ShareX", "Greenshot", "Lightshot",
        "LockApp", "LogonUI",
        "vlc", "mpv", "mpc-hc*", "mpc-be*", "PotPlayer*", "wmplayer", "Video.UI", "Photos", "Microsoft.Photos",
        "chrome", "msedge", "firefox", "brave", "opera", "vivaldi",
        "POWERPNT", "mstsc", "HandBrake",
        "Zoom", "Teams", "ms-teams", "ktalk*", "TrueConf"
    };
}
