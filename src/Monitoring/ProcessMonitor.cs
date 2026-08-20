using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using SystemSpinnerX64.Diagnostics;

namespace SystemSpinnerX64.Monitoring;

/// <summary>One row of the hungriest-processes list.</summary>
/// <param name="Pid">Process id.</param>
/// <param name="Name">Name as Task Manager shows it.</param>
/// <param name="CpuPercent">Share of the processor between two polls, counting the cores.</param>
/// <param name="MemoryMb">Memory in use, megabytes.</param>
/// <param name="Icon">Icon of the executable, or null when it could not be reached.</param>
public sealed record ProcessUsage(int Pid, string Name, double CpuPercent, double MemoryMb, Icon? Icon);

/// <summary>
/// Who is taking the processor and the memory. The load is counted exactly the way Task Manager
/// counts it: the processor time a process gained between two polls, divided by all the time the
/// machine had over the same span — that is, the span multiplied by the number of logical cores.
///
/// Hence the first poll shows nothing. One point is not enough for a difference, and inventing
/// one would mean showing an untruth at the single moment the user looks at the list for the
/// first time.
/// </summary>
public sealed class ProcessMonitor : IDisposable
{
    private readonly Dictionary<int, (TimeSpan Cpu, DateTime At)> _previous = new();
    private readonly Dictionary<int, Icon?> _icons = new();

    private static readonly int Cores = Environment.ProcessorCount;

    /// <summary>The process list, sorted by processor load.</summary>
    public IReadOnlyList<ProcessUsage> Snapshot(int take)
    {
        var result = new List<ProcessUsage>();
        DateTime now = DateTime.UtcNow;
        var alive = new HashSet<int>();

        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                int pid = process.Id;
                alive.Add(pid);

                TimeSpan cpu;
                long memory;
                string name;

                try
                {
                    // System processes do not give up their time even to an administrator — those
                    // are skipped: they would not have made the list anyway.
                    cpu = process.TotalProcessorTime;
                    memory = process.WorkingSet64;
                    name = process.ProcessName;
                }
                catch (Exception)
                {
                    continue;
                }

                double percent = 0;
                if (_previous.TryGetValue(pid, out var before))
                {
                    double seconds = (now - before.At).TotalSeconds;
                    if (seconds > 0)
                        percent = (cpu - before.Cpu).TotalSeconds / (seconds * Cores) * 100.0;
                }

                _previous[pid] = (cpu, now);

                result.Add(new ProcessUsage(
                    pid,
                    name,
                    Math.Clamp(percent, 0, 100),
                    memory / 1024.0 / 1024.0,
                    null));
            }
        }

        // Processes that are gone are dropped from memory: over a day thousands would pile up.
        foreach (int pid in _previous.Keys.Where(pid => !alive.Contains(pid)).ToList())
            _previous.Remove(pid);

        return result
            .OrderByDescending(p => p.CpuPercent)
            .ThenByDescending(p => p.MemoryMb)
            .Take(Math.Clamp(take, 1, 100))
            .Select(p => p with { Icon = IconFor(p.Pid) })
            .ToList();
    }

    // The icon is read once per process: reaching for a file on disk costs more than the rest
    // of the poll put together.
    private Icon? IconFor(int pid)
    {
        if (_icons.TryGetValue(pid, out Icon? cached)) return cached;

        Icon? icon = null;
        try
        {
            using Process process = Process.GetProcessById(pid);
            string? path = process.MainModule?.FileName;
            if (path is { Length: > 0 }) icon = Icon.ExtractAssociatedIcon(path);
        }
        catch (Exception ex)
        {
            // Ordinary case: protected processes hide their module even from an administrator.
            System.Diagnostics.Debug.WriteLine($"no icon for process {pid}: {ex.Message}");
        }

        if (_icons.Count > AppParameters.Polling.ProcessIconCache) ForgetIcons();
        _icons[pid] = icon;
        return icon;
    }

    private void ForgetIcons()
    {
        foreach (Icon? icon in _icons.Values) icon?.Dispose();
        _icons.Clear();
    }

    public void Dispose()
    {
        ForgetIcons();
        _previous.Clear();
        Log.Info("process monitor stopped");
    }
}
