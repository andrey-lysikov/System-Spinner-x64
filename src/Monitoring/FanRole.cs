namespace SystemSpinnerX64.Monitoring;

/// <summary>What a fan is for. Detected sensors are sorted into the panel slots by this.</summary>
public enum FanRole
{
    /// <summary>A graphics card fan: the sensor belongs to the card itself.</summary>
    Gpu,

    /// <summary>An AIO pump or its radiator fans: Pump, AIO, Water, Kraken and the like.</summary>
    Aio,

    /// <summary>The CPU cooler: the sensor name contains CPU.</summary>
    Cpu,

    /// <summary>Everything else — case fans, the PSU fan, unidentified headers.</summary>
    Case
}
