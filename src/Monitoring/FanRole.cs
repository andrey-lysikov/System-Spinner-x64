namespace SystemSpinnerX64.Monitoring;

// What a fan is for. Detected sensors are sorted into the panel slots by this.
public enum FanRole
{
    // A graphics card fan: the sensor belongs to the card itself.
    Gpu,

    // An AIO pump or its radiator fans: Pump, AIO, Water, Kraken and the like.
    Aio,

    // The CPU cooler: the sensor name contains CPU.
    Cpu,

    // Everything else — case fans, the PSU fan, unidentified headers.
    Case
}
