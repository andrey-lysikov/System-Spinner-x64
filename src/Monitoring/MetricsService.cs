//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Threading;
using SystemSpinnerX64.Configuration;
using SystemSpinnerX64.Diagnostics;

namespace SystemSpinnerX64.Monitoring;

// Tail of recent values for a chart.
public sealed class History
{
    private readonly List<double> _points = new();
    private readonly int _capacity;

    public History(int capacity)
    {
        _capacity = Math.Clamp(capacity,
                              AppParameters.Limits.MinHistoryPoints,
                              AppParameters.Limits.MaxHistoryPoints);
    }

    public int Count => _points.Count;

    public void Add(double value)
    {
        _points.Add(value);

        // Shifted in one go rather than point by point: RemoveAt(0) on every add would rewrite
        // the whole list once a second.
        if (_points.Count > _capacity + _capacity / 10)
            _points.RemoveRange(0, _points.Count - _capacity);
    }

    // Snapshot for the chart. A copy: drawing runs on a different thread than polling.
    public IReadOnlyList<double> Snapshot()
    {
        int extra = Math.Max(0, _points.Count - _capacity);
        return _points.Skip(extra).ToList();
    }

    public void Clear() => _points.Clear();
}

// Every reading from one poll. null means the sensor was not found.
public sealed class Readings
{
    public double? CpuLoad { get; set; }
    public double? CpuTempC { get; set; }
    public double? CpuPowerW { get; set; }
    public double? CpuClockMhz { get; set; }
    public double? SysMemUsedGb { get; set; }

    // Free memory. The status window needs it; the in-game panel does not show it.
    public double? SysMemFreeGb { get; set; }

    // Page file in use. Only read while the status window is open — see SwapMonitor.
    public double? SwapUsedGb { get; set; }

    public double? SwapTotalGb { get; set; }

    public double? CpuFanRpm { get; set; }
    public double? AioFanRpm { get; set; }

    // Extra fans from the config, in the order they are listed there.
    public IReadOnlyList<double?> ExtraFanRpm { get; set; } = Array.Empty<double?>();

    public double? GpuLoad { get; set; }
    public double? GpuTempC { get; set; }
    public double? GpuPowerW { get; set; }
    public double? GpuClockMhz { get; set; }
    public double? GpuMemUsedGb { get; set; }
    public double? GpuFanRpm { get; set; }

    // Total video memory. Only the status window scale needs it.
    public double? GpuMemTotalGb { get; set; }

    // Whether the card has memory of its own. Integrated graphics take it from the system: there
    // is no separate amount to show, and a scale of it would repeat the memory row.
    public bool GpuHasOwnMemory => GpuMemTotalGb is double total && total > 0;

    // The busier of the two loads. This is what the tray icon spins by: a game leans on the card
    // while the processor idles, and an icon standing still would then say nothing is happening.
    public double BusiestLoad => Math.Max(CpuLoad ?? 0, GpuLoad ?? 0);

    // Used memory as a percentage of installed, or null when the total is unknown.
    public double? MemLoadPercent =>
        SysMemUsedGb is double used && SysMemFreeGb is double free && used + free > 0
            ? used / (used + free) * 100.0
            : null;

    // Page file in use as a percentage of its size.
    public double? SwapLoadPercent =>
        SwapUsedGb is double used && SwapTotalGb is double total && total > 0
            ? used / total * 100.0
            : null;

    // Used video memory as a percentage of the whole.
    public double? GpuMemLoadPercent =>
        GpuMemUsedGb is double used && GpuMemTotalGb is double total && total > 0
            ? used / total * 100.0
            : null;
}

// The page file — what the macOS version calls swap.
internal static class SwapMonitor
{
    private const double Megabyte = 1024.0;

    // Used and total page file in gigabytes, or null when there is no page file.
    public static (double UsedGb, double TotalGb)? Read()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT AllocatedBaseSize, CurrentUsage FROM Win32_PageFileUsage");
            using ManagementObjectCollection results = searcher.Get();

            double used = 0;
            double total = 0;

            // A machine can have several page files, one per drive: they add up into one number,
            // exactly as Task Manager shows them.
            foreach (ManagementBaseObject item in results)
            {
                using (item)
                {
                    if (item["AllocatedBaseSize"] is uint allocated) total += allocated / Megabyte;
                    if (item["CurrentUsage"] is uint current) used += current / Megabyte;
                }
            }

            return total > 0 ? (used, total) : null;
        }
        catch (ManagementException ex)
        {
            // The page file can be switched off entirely — an ordinary case, not a failure.
            System.Diagnostics.Debug.WriteLine($"Win32_PageFileUsage is unavailable: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            Log.Error("the page file usage was not read", ex);
            return null;
        }
    }
}

// Rate on a network interface. The unit is picked to fit, as in the macOS version.
public readonly record struct Throughput(double BytesPerSecond)
{
    public static readonly Throughput Zero = new(0);

    private const double Kilobyte = 1024.0;

    public double Value => Scaled().Value;

    public string Unit => Scaled().Unit;

    private (double Value, string Unit) Scaled()
    {
        double megabyte = Kilobyte * Kilobyte;
        double gigabyte = megabyte * Kilobyte;

        return BytesPerSecond switch
        {
            >= 1024 * 1024 * 1024 * 1024.0 => (BytesPerSecond / (gigabyte * Kilobyte), "TB/s"),
            >= 1024 * 1024 * 1024.0 => (BytesPerSecond / gigabyte, "GB/s"),
            >= 1024 * 1024.0 => (BytesPerSecond / megabyte, "MB/s"),
            _ => (BytesPerSecond / Kilobyte, "KB/s")
        };
    }

    public string Describe() =>
        Value.ToString(Value >= 100 ? "0" : "0.0", CultureInfo.InvariantCulture) + " " + Unit;
}

// What the network line of the status window shows.
public sealed class NetworkUsage
{
    public string Address { get; init; } = "";
    public Throughput Inbound { get; init; } = Throughput.Zero;
    public Throughput Outbound { get; init; } = Throughput.Zero;

    public static readonly NetworkUsage Empty = new();
}

// Receive and send rates and the address of the machine.
public sealed class NetworkMonitor
{
    // One client for the whole app: each new one takes its own socket, and that lingers for two
    // more minutes after closing — over a day that would add up.
    private static readonly HttpClient Http = new() { Timeout = AppParameters.Network.RequestTimeout };

    // IPv4 first, then IPv6: the service answers over whichever family the request went out on,
    // and a machine with IPv6 alone would otherwise show nothing.
    private static readonly Regex AddressInPage = new(
        @"\b(?<ip>(?:\d{1,3}\.){3}\d{1,3})\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex AddressV6InPage = new(
        @"(?<ip>[0-9A-Fa-f]{0,4}(?::[0-9A-Fa-f]{0,4}){2,7})",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private long _previousIn;
    private long _previousOut;
    private bool _hasBaseline;

    private string _localAddress = "";
    private string _externalAddress = "";
    private bool _lookupRunning;
    private DateTime _lookupAllowedAt = DateTime.MinValue;
    private DateTime _retryAt = DateTime.MinValue;
    private int _retriesLeft = AppParameters.Network.MaxRetries;
    private bool _wasResolving = true;

    public NetworkUsage Usage { get; private set; } = NetworkUsage.Empty;

    // A poll. seconds is the time since the last one.
    public void Update(double seconds, bool resolveExternalAddress)
    {
        (long inBytes, long outBytes, string address) = ReadCounters();

        if (!resolveExternalAddress) _externalAddress = "";

        // The network changed — the old external address is no longer ours.
        if (address != _localAddress)
        {
            _localAddress = address;
            _externalAddress = "";

            // A different network is a different answer — the tries start over.
            _retriesLeft = AppParameters.Network.MaxRetries;
            RequestLookup(resolveExternalAddress);
        }
        else if (resolveExternalAddress && !_wasResolving)
        {
            RequestLookup(true);
        }
        else if (resolveExternalAddress && _externalAddress.Length == 0 &&
                 _retriesLeft > 0 && DateTime.UtcNow >= _retryAt)
        {
            // The last attempt came to nothing. A couple of tries later is the only way the
            // address appears without a restart; past that the local one stands.
            _retriesLeft--;
            RequestLookup(true);
        }

        _wasResolving = resolveExternalAddress;

        double elapsed = Math.Max(seconds, 0.001);

        // Counters can go backwards: the interface was brought up again and they restarted at zero.
        double inbound = _hasBaseline && inBytes >= _previousIn ? (inBytes - _previousIn) / elapsed : 0;
        double outbound = _hasBaseline && outBytes >= _previousOut ? (outBytes - _previousOut) / elapsed : 0;

        _previousIn = inBytes;
        _previousOut = outBytes;
        _hasBaseline = true;

        Usage = new NetworkUsage
        {
            Address = _externalAddress.Length > 0 ? _externalAddress : _localAddress,
            Inbound = new Throughput(inbound),
            Outbound = new Throughput(outbound)
        };
    }

    private static (long In, long Out, string Address) ReadCounters()
    {
        long inBytes = 0;
        long outBytes = 0;
        string address = "";

        try
        {
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                if (adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;

                IPInterfaceStatistics statistics = adapter.GetIPStatistics();
                inBytes += statistics.BytesReceived;
                outBytes += statistics.BytesSent;

                if (address.Length > 0) continue;

                // The address is taken from the interface with a gateway: virtual bridges and
                // Hyper-V have none, and without this the address of a VM would be reported as ours.
                IPInterfaceProperties properties = adapter.GetIPProperties();
                if (properties.GatewayAddresses.Count == 0) continue;

                address = PickAddress(properties.UnicastAddresses);
            }
        }
        catch (NetworkInformationException ex)
        {
            Log.Error("the network counters were not read", ex);
        }

        return (inBytes, outBytes, address);
    }

    // IPv4 if there is one, otherwise a routable IPv6. Link-local (fe80::) is skipped: it says
    // nothing about where the machine is on the network.
    private static string PickAddress(UnicastIPAddressInformationCollection addresses)
    {
        IPAddress? v4 = addresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address;
        if (v4 is not null) return v4.ToString();

        IPAddress? v6 = addresses
            .Select(a => a.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetworkV6 &&
                                 !a.IsIPv6LinkLocal && !a.IsIPv6SiteLocal && !a.IsIPv6Teredo);

        return v6?.ToString() ?? "";
    }

    private void RequestLookup(bool enabled)
    {
        if (!enabled || _lookupRunning) return;

        _lookupRunning = true;

        // The delay is not politeness towards someone else's service but towards ours: right after
        // a network change the route may not be up yet and the request would be wasted.
        _lookupAllowedAt = DateTime.UtcNow.AddSeconds(AppParameters.Network.LookupDelaySeconds);

        Task.Run(async () =>
        {
            try
            {
                TimeSpan wait = _lookupAllowedAt - DateTime.UtcNow;
                if (wait > TimeSpan.Zero) await Task.Delay(wait);

                string? found = await FetchExternalAddress();
                if (found is { Length: > 0 })
                {
                    // The assignment is atomic and only the poll reads the string — no lock needed.
                    _externalAddress = found;
                    Log.Info($"external address: {found}");
                }
                else
                {
                    _retryAt = DateTime.UtcNow + AppParameters.Network.RetryDelay;
                }
            }
            finally
            {
                _lookupRunning = false;
            }
        });
    }

    private static async Task<string?> FetchExternalAddress()
    {
        try
        {
            string page = await Http.GetStringAsync(AppParameters.Network.ExternalAddressUrl);
            Match match = AddressInPage.Match(page);
            return match.Success ? match.Groups["ip"].Value : null;
        }
        catch (Exception ex)
        {
            // No network or the service is down — the local address remains. Not a failure.
            Log.Warn($"the external address was not looked up: {ex.Message}");
            return null;
        }
    }

    // Extracting the address from the service answer.
    internal static string? ParseAddress(string page)
    {
        Match match = AddressInPage.Match(page);
        if (match.Success) return match.Groups["ip"].Value;

        // IPv6 is only accepted when the framework agrees it is an address: the pattern alone
        // would also match a fragment of markup or a timestamp.
        foreach (Match candidate in AddressV6InPage.Matches(page))
        {
            string text = candidate.Groups["ip"].Value;
            if (IPAddress.TryParse(text, out IPAddress? parsed) &&
                parsed.AddressFamily == AddressFamily.InterNetworkV6)
                return parsed.ToString();
        }

        return null;
    }
}

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

// Everything known about the machine at one moment.
public sealed class MetricsSnapshot
{
    public Readings Readings { get; init; } = new();
    public NetworkUsage Network { get; init; } = NetworkUsage.Empty;
    public IReadOnlyList<double> CpuHistory { get; init; } = Array.Empty<double>();
    public IReadOnlyList<double> MemoryHistory { get; init; } = Array.Empty<double>();
    public IReadOnlyList<ProcessUsage> Processes { get; init; } = Array.Empty<ProcessUsage>();

    public static readonly MetricsSnapshot Empty = new();
}

// One poll for the whole app: the panel, the tray icon and the status window all read from here.
// Each walk of the tree wakes the driver, and three a second are the load the app is measuring.
public sealed class MetricsService : IDisposable
{
    private readonly AppConfig _cfg;
    private readonly HardwareMonitor _hardware;
    private readonly NetworkMonitor _network = new();
    private readonly ProcessMonitor _processes = new();

    private readonly History _cpuHistory;
    private readonly History _memoryHistory;

    private readonly DispatcherTimer _timer = new();

    private bool _polling;
    private bool _detailed;
    private DateTime _lastPoll = DateTime.MinValue;
    private string? _lastError;

    // A fresh snapshot. Raised on the UI thread.
    public event Action<MetricsSnapshot>? Updated;

    // The poll failed. Raised once per new cause.
    public event Action<string>? Failed;

    public MetricsSnapshot Latest { get; private set; } = MetricsSnapshot.Empty;

    public MetricsService(AppConfig cfg, HardwareMonitor hardware)
    {
        _cfg = cfg;
        _hardware = hardware;

        _cpuHistory = new History(cfg.Stats.HistoryPoints);
        _memoryHistory = new History(cfg.Stats.HistoryPoints);

        _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(AppParameters.Polling.MinIntervalMs, cfg.UpdateIntervalMs));
        _timer.Tick += (_, _) => Poll();
    }

    // Whether to collect what only the status window needs.
    public bool Detailed
    {
        get => _detailed;
        set
        {
            if (_detailed == value) return;
            _detailed = value;
            Log.Info(value ? "detailed metrics on" : "detailed metrics off");

            // The first process load is a difference against a previous poll that does not exist
            // yet. It is taken right away, so the list is not empty the moment the window opens.
            if (value) Poll();
        }
    }

    public void Start()
    {
        if (_timer.IsEnabled) return;
        _timer.Start();

        // Load is measured between two reads — the first one only sets the reference point.
        Poll();
    }

    public void Stop() => _timer.Stop();

    // Changes the poll period on the fly — from the tray menu.
    public void SetInterval(int milliseconds)
    {
        _cfg.UpdateIntervalMs = Math.Max(AppParameters.Polling.MinIntervalMs, milliseconds);
        _timer.Interval = TimeSpan.FromMilliseconds(_cfg.UpdateIntervalMs);
    }

    private void Poll()
    {
        // The poll did not finish within the period — the tick is skipped rather than queued:
        // the library will not survive two walks of the sensor tree at once.
        if (_polling) return;
        _polling = true;

        DateTime now = DateTime.UtcNow;
        double seconds = _lastPoll == DateTime.MinValue
            ? _timer.Interval.TotalSeconds
            : (now - _lastPoll).TotalSeconds;
        _lastPoll = now;

        bool detailed = _detailed;
        bool externalAddress = _cfg.Stats.ShowExternalAddress;
        int topProcesses = _cfg.Stats.TopProcesses;

        // Walking the sensors takes tens of milliseconds — moved off the UI thread, or the tray
        // animation would stutter once a second.
        Task.Run(() =>
        {
            Readings? readings = null;
            string? error = null;
            IReadOnlyList<ProcessUsage> processes = Array.Empty<ProcessUsage>();

            try
            {
                readings = _hardware.Read();
                _network.Update(seconds, externalAddress);

                if (detailed)
                {
                    processes = _processes.Snapshot(topProcesses);

                    // The page file is only shown in the status window, and the WMI query behind
                    // it costs tens of milliseconds — no reason to pay for it the rest of the time.
                    if (SwapMonitor.Read() is (double used, double total))
                    {
                        readings.SwapUsedGb = used;
                        readings.SwapTotalGb = total;
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            // Back to the UI thread, unless the app is already closing: a dispatcher on its way
            // out would throw, and the log would show a crash at every exit.
            if (_timer.Dispatcher.HasShutdownStarted) return;

            try
            {
                _timer.Dispatcher.BeginInvoke(() => Publish(readings, error, processes, detailed));
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // The dispatcher shut down between the check and the call.
            }
            catch (InvalidOperationException)
            {
            }
        });
    }

    private void Publish(Readings? readings, string? error,
                         IReadOnlyList<ProcessUsage> processes, bool detailed)
    {
        _polling = false;

        if (readings is null)
        {
            if (error is not null && error != _lastError)
            {
                Log.Error($"sensor poll: {_lastError = error}");
                Failed?.Invoke(error);
            }
            return;
        }

        _lastError = null;

        if (readings.CpuLoad is double cpu) _cpuHistory.Add(cpu);
        if (readings.MemLoadPercent is double memory) _memoryHistory.Add(memory);

        // The history copy is made only for the status window: nine hundred numbers a second for
        // nothing is exactly the kind of spending the app avoids.
        Latest = new MetricsSnapshot
        {
            Readings = readings,
            Network = _network.Usage,
            CpuHistory = detailed ? _cpuHistory.Snapshot() : Array.Empty<double>(),
            MemoryHistory = detailed ? _memoryHistory.Snapshot() : Array.Empty<double>(),
            Processes = processes
        };

        Updated?.Invoke(Latest);
    }

    public void Dispose()
    {
        _timer.Stop();
        _processes.Dispose();
    }
}
