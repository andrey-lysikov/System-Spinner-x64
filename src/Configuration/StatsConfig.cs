namespace SystemSpinnerX64.Configuration;

/// <summary>The status window, opened by a left click on the tray icon.</summary>
public sealed class StatsConfig
{
    /// <summary>
    /// Look up the external address through checkip.dyndns.org. This is the app's only request
    /// to the network, so it can be turned off; the local address then remains.
    /// </summary>
    public bool ShowExternalAddress { get; set; } = true;

    /// <summary>Chart history length. 900 points at a one-second poll is a quarter of an hour.</summary>
    public int HistoryPoints { get; set; } = 900;

    public int TopProcesses { get; set; } = 12;
}
