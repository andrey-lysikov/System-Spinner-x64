//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

namespace SystemSpinnerX64.Configuration;

// The status window, opened by a left click on the tray icon.
public sealed class StatsConfig
{
    // Look up the external address through checkip.dyndns.org.
    public bool ShowExternalAddress { get; set; } = true;

    // Chart history length. 900 points at a one-second poll is a quarter of an hour.
    public int HistoryPoints { get; set; } = 500;

    public int TopProcesses { get; set; } = 12;
}
