//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace SystemSpinnerX64.Diagnostics;

// A log file next to config.conf.
public static class Log
{
    private static readonly object Gate = new();

    private static string? _path;
    private static LogLevel _level = LogLevel.Info;
    private static int _writes;

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

                    RotateIfBig(path);

                    // The first line is written here rather than through Write(): that one swallows
                    // errors, and a folder without write access would look like a log that opened
                    // fine but receives nothing — the fallback folder would never kick in.
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

    // Everything is written until this is called. Debug keeps it that way; without it only what
    // got in the way is written — warnings and errors.
    public static void SetVerbose(bool verbose)
    {
        LogLevel level = verbose ? LogLevel.Info : LogLevel.Warn;

        lock (Gate)
        {
            if (_level == level) return;
            _level = level;
            if (_path is not null) Write(verbose ? "debug logging on" : "debug logging off");
        }
    }

    public static void Info(string message) => Add(LogLevel.Info, message);

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

        Add(LogLevel.Error, text.ToString());
    }

    // A key seen by the hook or read from the raw input. Part of the full record, and only written
    // with it — but under a tag of its own: a line per press would drown the rest otherwise.
    public static void Key(string message) => Add(LogLevel.Info, message, "KEY  ");

    private static void Add(LogLevel level, string message, string? tag = null)
    {
        lock (Gate)
        {
            if (_path is null || level > _level) return;

            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

            // Multi-line explanations are indented to the margin, or the log is unreadable by eye.
            string[] lines = message.Replace("\r\n", "\n").Split('\n');
            Write($"{stamp} {tag ?? Tag(level)} {lines[0]}");
            for (int i = 1; i < lines.Length; i++) Write($"{new string(' ', 30)}{lines[i]}");
        }
    }

    private static string Tag(LogLevel level) => level switch
    {
        LogLevel.Error => "ERROR",
        LogLevel.Warn => "WARN ",
        _ => "INFO "
    };

    // Only ever called under Gate.
    private static void Write(string line)
    {
        try
        {
            if (++_writes % AppParameters.Logging.SizeCheckEvery == 0) RotateIfBig(_path!);

            // The BOM appears only for a new file — StreamWriter writes the preamble at position zero.
            using var writer = new StreamWriter(_path!, append: true, new UTF8Encoding(true));
            writer.WriteLine(line);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"a log line was lost: {ex.Message}");
        }
    }

    private static void RotateIfBig(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length < AppParameters.Logging.MaxBytes) return;

            string old = path + ".old";
            File.Delete(old);
            File.Move(path, old);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"could not rotate the log: {ex.Message}");
        }
    }

    // Closing line — it shows the exit was orderly rather than a crash.
    public static void Finish(string reason) => Add(LogLevel.Info, $"--- shutdown: {reason} ---");

}
