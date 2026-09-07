//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

namespace SystemSpinnerX64.Diagnostics;

// The kind of a log line. It is the kind, not a threshold: what is written without Debug is
// decided by the level itself rather than by counting up or down from one.
internal enum LogLevel
{
    // The whole course of work. Written only while Debug is on, or before the config has been read.
    Info,

    // The few things that happen to the machine rather than inside this application — the app
    // started or stopped, a screen was found or lost, autostart was changed.
    // Always written, Debug or not: without them a log of a quiet run says nothing at all.
    Event,

    // What is worth noticing: a sensor was not found, the config was not written.
    Warn,

    // What got in the way: sensors did not open, the ETW session did not start.
    Error,

    // What nobody caught. Writes the stack of every inner exception as well.
    Crash,

    // Key presses. They have a tag of their own or a line per press drowns the rest of the file.
    Key
}
