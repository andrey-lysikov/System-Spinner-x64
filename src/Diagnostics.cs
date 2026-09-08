//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SystemSpinnerX64.Diagnostics;

// The kind of a log line. It is the kind, not a threshold: what is written without Debug is
// decided by the level itself rather than by counting up or down from one.
internal enum LogLevel
{
    // The whole course of work. Written only while Debug is on, or before the config has been read.
    Info,

    // The few things that happen to the machine rather than inside this application. Always
    // written, Debug or not: without them a log of a quiet run says nothing at all.
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

// A log file next to config.conf, and the only diagnostic channel this application has: it has
// to answer on its own, including from a run where Debug was never switched on.
public static class Log
{
    private static readonly object Gate = new();

    private static string? _path;

    // Until the config says otherwise everything is written: the config is read after the log is
    // opened, and the lines from before it would be the ones missing.
    private static bool _verbose = true;

    private static int _linesSinceSizeCheck;

    public static string? Path
    {
        get { lock (Gate) return _path; }
    }

    // If the folder cannot be written to, tries the fallback; if that fails too, work goes on
    // without a log: being unable to keep records is no reason not to start.
    public static void Start(string directory, string? fallbackDirectory = null)
    {
        lock (Gate)
        {
            foreach (string? dir in new[] { directory, fallbackDirectory })
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;

                try
                {
                    System.IO.Directory.CreateDirectory(dir);
                    string path = System.IO.Path.Combine(dir, AppParameters.Identity.LogFile);

                    Rotate(path);

                    // Written here rather than through Write(), which swallows errors: a folder
                    // without write access would look fine and the fallback would never kick in.
                    using (var probe = new StreamWriter(path, append: true, new UTF8Encoding(true)))
                        probe.WriteLine($"--- {AppParameters.Identity.Name} {AppParameters.Identity.Version} started ---");

                    _path = path;
                    return;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"the log in {dir} did not open: {ex.Message}");
                }
            }
        }
    }

    // Everything is written until this is called. Debug keeps it that way; without it the course
    // of work and the key presses drop out, and what happened to the machine stays.
    public static void SetVerbose(bool verbose)
    {
        lock (Gate)
        {
            if (_verbose == verbose) return;
            _verbose = verbose;
        }

        Event(verbose
            ? "debug logging on: the whole course of work goes into this file"
            : "debug logging off: events, warnings and errors are still kept — " +
              "set Debug = true in [General] for the rest");
    }

    // The course of work. Kept only while Debug is on.
    public static void Info(string message) => Add(LogLevel.Info, message);

    // Something that happened to the machine rather than inside this application: a screen found
    // or lost, autostart changed, the app started or stopped. Kept whatever Debug says.
    public static void Event(string message) => Add(LogLevel.Event, message);

    public static void Warn(string message) => Add(LogLevel.Warn, message);

    public static void Error(string message, Exception? ex = null) =>
        Add(LogLevel.Error, ex is null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}");

    // An exception nobody caught. Unlike Error this writes the stack as well: a crash leaves no
    // other trace, and the line that threw is the only thing worth having.
    public static void Crash(string where, Exception ex)
    {
        var text = new StringBuilder();
        text.Append("UNHANDLED in ").Append(where).Append(':');

        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            text.AppendLine();
            text.Append(current.GetType().FullName).Append(": ").Append(current.Message);

            if (current.StackTrace is { Length: > 0 } stack)
            {
                text.AppendLine();
                text.Append(stack);
            }
        }

        Add(LogLevel.Crash, text.ToString());
    }

    // A key seen by the hook or read from the raw input. Part of the full record, and only written
    // with it — but under a tag of its own: a line per press would drown the rest otherwise.
    public static void Key(string message) => Add(LogLevel.Key, message);

    private static readonly Dictionary<string, DateTime> LastSaid = new();

    // A warning from somewhere that runs many times a second. Said once, then held for a while
    // however often it recurs; subject is what counts as "the same complaint".
    public static void WarnOccasionally(string subject, string message)
    {
        lock (Gate)
        {
            DateTime now = DateTime.UtcNow;
            if (LastSaid.TryGetValue(subject, out DateTime last) &&
                now - last < AppParameters.Logging.RepeatAfter) return;

            LastSaid[subject] = now;
        }

        Add(LogLevel.Warn, message);
    }

    private static void Add(LogLevel level, string message)
    {
        lock (Gate)
        {
            if (_path is null) return;
            if (!_verbose && level is LogLevel.Info or LogLevel.Key) return;

            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

            // Multi-line explanations are indented to the margin, or the log is unreadable by eye.
            string indent = new(' ', AppParameters.Logging.ContinuationIndent);
            string[] lines = message.Replace("\r\n", "\n").Split('\n');

            Write($"{stamp} {Tag(level)} {lines[0]}");
            for (int i = 1; i < lines.Length; i++) Write($"{indent}{lines[i]}");
        }
    }

    private static string Tag(LogLevel level) => level switch
    {
        LogLevel.Event => "EVENT",
        LogLevel.Warn => "WARN ",
        LogLevel.Error => "ERROR",
        LogLevel.Crash => "CRASH",
        LogLevel.Key => "KEY  ",
        _ => "INFO "
    };

    // Only ever called under Gate.
    private static void Write(string line)
    {
        try
        {
            if (++_linesSinceSizeCheck >= AppParameters.Logging.CheckEveryLines)
            {
                _linesSinceSizeCheck = 0;
                Rotate(_path!);
            }

            // Opened and closed per line: the process can be killed at any moment, and a buffered
            // log is an empty log. The BOM appears only for a new file, at position zero.
            using var writer = new StreamWriter(_path!, append: true, new UTF8Encoding(true));
            writer.WriteLine(line);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"a log line was lost: {ex.Message}");
        }
    }

    // Numbered generations, .log.1 the newest: each is pushed up one number and whatever falls
    // past MaxRotations is gone. Failures are swallowed: an unrotated log is still worth writing.
    private static void Rotate(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length < AppParameters.Logging.MaxBytes) return;

            File.Delete($"{path}.{AppParameters.Logging.MaxRotations}");

            for (int number = AppParameters.Logging.MaxRotations - 1; number >= 1; number--)
            {
                string from = $"{path}.{number}";
                if (File.Exists(from)) File.Move(from, $"{path}.{number + 1}", overwrite: true);
            }

            File.Move(path, $"{path}.1", overwrite: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"could not rotate the log: {ex.Message}");
        }
    }

    // Closing line — it shows the exit was orderly rather than a crash.
    public static void Finish(string reason) => Add(LogLevel.Event, $"--- shutdown: {reason} ---");
}
