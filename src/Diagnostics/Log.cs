using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace SystemSpinnerX64.Diagnostics;

/// <summary>
/// A log file next to config.conf. The app shows no windows at all, so the log is the only way
/// to find out what happened: why the FPS is empty, where the dash instead of fan speed came
/// from, why the app closed right after starting.
///
/// Opened on the first line of <c>App.OnStartup</c>, before every check, at Info — otherwise the
/// reason for the earliest refusal would have nowhere to go.
/// </summary>
public static class Log
{
    public const string FileName = "SystemSpinnerX64.log";

    // Past this the file moves to SystemSpinnerX64.log.old and a new one starts.

    // Checking the size on every write would mean extra trips to the disk.

    private static readonly object Gate = new();

    private static string? _path;
    private static LogLevel _level = LogLevel.Info;
    private static int _writes;

    public static string? Path
    {
        get { lock (Gate) return _path; }
    }

    /// <summary>
    /// If the folder cannot be written to, tries the fallback; if that fails too, work goes on
    /// without a log: being unable to keep records is no reason not to start.
    /// </summary>
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
                    string path = System.IO.Path.Combine(dir, FileName);

                    RotateIfBig(path);

                    // The first line is written here rather than through Write(): that one swallows
                    // errors, and a folder without write access would look like a log that opened
                    // fine but receives nothing — the fallback folder would never kick in.
                    using (var probe = new StreamWriter(path, append: true, new UTF8Encoding(true)))
                        probe.WriteLine($"--- System Spinner x64 {Version()} started ---");

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

    /// <summary>Everything is written until this is called.</summary>
    public static void SetLevel(LogLevel level)
    {
        lock (Gate)
        {
            if (_level == level) return;
            _level = level;
            if (_path is not null) Write($"log level: {level}");
        }
    }

    public static void Info(string message) => Add(LogLevel.Info, message);

    public static void Warn(string message) => Add(LogLevel.Warn, message);

    public static void Error(string message, Exception? ex = null) =>
        Add(LogLevel.Error, ex is null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}");

    /// <summary>
    /// An exception nobody caught. Unlike <see cref="Error"/> this writes the stack as well:
    /// a crash leaves no other trace, and the line that threw is the only thing worth having.
    /// Inner exceptions are unwrapped — the outer one is usually just a wrapper.
    /// </summary>
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

    private static void Add(LogLevel level, string message)
    {
        lock (Gate)
        {
            if (_path is null || level > _level) return;

            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

            // Multi-line explanations are indented to the margin, or the log is unreadable by eye.
            string[] lines = message.Replace("\r\n", "\n").Split('\n');
            Write($"{stamp} {Tag(level)} {lines[0]}");
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

    /// <summary>Closing line — it shows the exit was orderly rather than a crash.</summary>
    public static void Finish(string reason) => Add(LogLevel.Info, $"--- shutdown: {reason} ---");

    private static string Version() =>
        typeof(Log).Assembly.GetName().Version?.ToString() ?? "";
}
