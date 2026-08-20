using System;
using System.Diagnostics;
using SystemSpinnerX64.Diagnostics;
using SystemSpinnerX64.Localization;

namespace SystemSpinnerX64.Startup;

/// <summary>
/// Autostart through a Task Scheduler task rather than a Startup folder shortcut: the app needs
/// administrator rights, and a shortcut would raise a UAC prompt at every sign-in, while a task
/// with highest privileges starts without questions — decided once, when it is created.
///
/// Driven by <c>schtasks.exe</c>: part of Windows, with no reference to the scheduler COM libraries.
/// </summary>
internal static class AutoStart
{
    private const string TaskName = "SystemSpinnerX64";

    public static bool IsEnabled() => Run("/Query", "/TN", TaskName) == 0;

    /// <summary>Creates or recreates the task. Returns an error text, or null on success.</summary>
    public static string? Enable()
    {
        string? exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return Text.NoOwnExePath;

        // /RL HIGHEST — with highest privileges, the whole point of this.
        // /F — replace an existing task silently: simpler than comparing its parameters.
        int code = Run("/Create", "/TN", TaskName, "/TR", $"\"{exe}\"",
                       "/SC", "ONLOGON", "/RL", "HIGHEST", "/F");

        if (code == 0)
        {
            Log.Info($"autostart enabled: task \"{TaskName}\" → {exe}");
            return null;
        }

        Log.Warn($"could not create the autostart task, schtasks returned code {code}");
        return Text.SchedulerRefused(code);
    }

    /// <summary>Removes the task. Returns an error text, or null on success.</summary>
    public static string? Disable()
    {
        int code = Run("/Delete", "/TN", TaskName, "/F");

        if (code == 0)
        {
            Log.Info("autostart disabled: task removed");
            return null;
        }

        Log.Warn($"could not remove the autostart task, schtasks returned code {code}");
        return Text.SchedulerRefused(code);
    }

    /// <summary>Runs schtasks without a console window and returns its exit code.</summary>
    private static int Run(params string[] arguments)
    {
        var start = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (string argument in arguments) start.ArgumentList.Add(argument);

        try
        {
            using Process? process = Process.Start(start);
            if (process is null) return -1;

            // The streams are drained: a process with long output can stall on a full buffer.
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();

            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Log.Error("could not start schtasks", ex);
            return -1;
        }
    }
}
