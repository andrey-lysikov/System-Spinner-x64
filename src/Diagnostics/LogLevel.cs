namespace SystemSpinnerX64.Diagnostics;

/// <summary>
/// What goes into the log. The order matters: a message is written when its level is not above
/// the configured one, so the values run from terse to verbose.
/// </summary>
public enum LogLevel
{
    /// <summary>Only what got in the way: sensors did not open, the ETW session did not start.</summary>
    Error = 0,

    /// <summary>Plus what is worth noticing: a sensor was not found, the config was not written.</summary>
    Warn = 1,

    /// <summary>Plus the course of work: startup checks, the chosen frame source. For debugging.</summary>
    Info = 2
}
