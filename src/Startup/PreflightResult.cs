using SystemSpinnerX64.Configuration;
using SystemSpinnerX64.Monitoring;

namespace SystemSpinnerX64.Startup;

/// <summary>
/// The outcome of the startup checks: either everything is ready, or there is a reason to stop —
/// it goes to the log and the app closes.
/// </summary>
internal sealed class PreflightResult
{
    private PreflightResult() { }

    public AppConfig? Config { get; private init; }

    /// <summary>Sensors that are already open — no reason to walk the hardware tree twice.</summary>
    public HardwareMonitor? Hardware { get; private init; }

    /// <summary>Reason to stop. null means it can start.</summary>
    public string? Problem { get; private init; }

    public bool CanStart => Problem is null && Config is not null && Hardware is not null;

    public static PreflightResult Start(AppConfig cfg, HardwareMonitor hw) =>
        new() { Config = cfg, Hardware = hw };

    /// <summary>Stop and say why. Sensors, if they were opened, are closed.</summary>
    public static PreflightResult Stop(string problem, HardwareMonitor? hw = null)
    {
        hw?.Dispose();
        return new PreflightResult { Problem = problem };
    }
}
