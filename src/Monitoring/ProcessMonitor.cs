//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using SystemSpinnerX64.Diagnostics;

namespace SystemSpinnerX64.Monitoring;

// One row of the hungriest-processes list.
public sealed record ProcessUsage(int Pid, string Name, double CpuPercent, double MemoryMb, Icon? Icon);

// Who is taking the processor and the memory.
public sealed class ProcessMonitor : IDisposable
{
    private readonly Dictionary<int, (TimeSpan Cpu, DateTime At)> _previous = new();
    private readonly Dictionary<int, Icon?> _icons = new();

    private static readonly int Cores = Environment.ProcessorCount;

    // The process list, sorted by processor load.
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
            .Take(Math.Clamp(take,
                          AppParameters.Limits.MinTopProcesses,
                          AppParameters.Limits.MaxTopProcesses))
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
