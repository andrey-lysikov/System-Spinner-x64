namespace SystemSpinnerX64.Monitoring;

/// <summary>One detected fan sensor: what it is, where it sits and what it read while scanning.</summary>
/// <param name="Name">Sensor name — this is what goes into config.conf.</param>
/// <param name="HardwareName">Where it was found: a SuperIO chip, an AIO controller, the card.</param>
/// <param name="Role">Where the heuristic put it.</param>
/// <param name="Rpm">Speed at scan time; null means the sensor exists but has no value.</param>
public sealed record FanSensor(string Name, string HardwareName, FanRole Role, double? Rpm)
{
    /// <summary>Line for the scan report in the log.</summary>
    public string Describe =>
        $"[{Role}] {HardwareName} / {Name} = {(Rpm is null ? "—" : Rpm.Value.ToString("0"))} rpm";
}
