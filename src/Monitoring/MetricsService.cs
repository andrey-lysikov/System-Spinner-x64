using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Threading;
using SystemSpinnerX64.Configuration;
using SystemSpinnerX64.Diagnostics;

namespace SystemSpinnerX64.Monitoring;

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

// One poll for the whole app. The in-game panel, the tray icon and the status window all take their
// values from here: the sensors cannot be polled three times over — each walk of the tree wakes the
// driver, and three walks a second are exactly the load the app is measuring.
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

    public bool IsRunning => _timer.IsEnabled;

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
