namespace SystemSpinnerX64.Diagnostics;

// The kind of a log line. The order matters: a line is written when its level is not above the
// one in force, so the values run from terse to verbose. The config has no level of its own —
// only the Debug switch, which chooses between Info and Warn.
internal enum LogLevel
{
    // Only what got in the way: sensors did not open, the ETW session did not start.
    Error = 0,

    // Plus what is worth noticing: a sensor was not found, the config was not written.
    Warn = 1,

    // Plus the course of work: startup checks, the chosen frame source.
    Info = 2
}
