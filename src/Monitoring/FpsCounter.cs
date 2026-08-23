//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SystemSpinnerX64.Configuration;
using SystemSpinnerX64.Diagnostics;
using SystemSpinnerX64.Localization;
using SystemSpinnerX64.Platform;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace SystemSpinnerX64.Monitoring;

// Counts frames from ETW Present events — the same telemetry PresentMon uses, only the session is
// raised inside this process.
public sealed class FpsCounter : IDisposable
{
    private static readonly Guid DxgiProvider = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private static readonly Guid D3d9Provider = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
    private static readonly Guid DxgKrnlProvider = new("802EC45A-1E99-4B83-9920-87C98277BA9D");

    private const int DxgiPresentStart = 42;
    private const int D3d9PresentStart = 1;

    private const ulong AllKeywords = ulong.MaxValue;

    // The Base keyword of DxgKrnl. It must be limited: it produces tens of thousands a second.
    private const ulong DxgKrnlBaseKeyword = 0x1;

    // DXGI and D3D9 give exactly one Present per frame, so they are the accurate ones for DirectX;
    // DxgKrnl is the common denominator — that is, Vulkan and OpenGL.
    private enum Source
    {
        None = 0,
        DxgKrnl = 1,
        D3d9 = 2,
        Dxgi = 3
    }

    private readonly object _lock = new();

    private readonly FrameWindow _frames;

    private readonly double[] _lastSeen = new double[4];

    private Source _source = Source.None;
    private int _trackedPid;

    // --- Picking the DxgKrnl event ---

    private int _dxgkEventId = -1;
    private string? _dxgkTask;
    private double _firstDxgkSeen = double.NaN;

    private bool _noMatchReported;
    private bool _implausibleReported;

    private double _probeStarted = double.NaN;

    private readonly Dictionary<string, (int Count, int Id, int Rank)> _probeCounts = new(StringComparer.Ordinal);

    private readonly Dictionary<string, int> _seenTasks = new(StringComparer.Ordinal);
    private readonly int[] _providerEvents = new int[4];
    private readonly Dictionary<string, int> _seenDxgi = new(StringComparer.Ordinal);

    private bool _providersReported;

    private TraceEventSession? _session;

    private Timer? _providerTimer;
    private bool _dxgKrnlEnabled;

    public string? Status { get; private set; }

    public string Api => _source switch
    {
        Source.Dxgi => "DXGI",
        Source.D3d9 => "D3D9",
        Source.DxgKrnl => $"Vulkan/OpenGL ({_dxgkTask})",
        _ => "—"
    };

    public FpsCounter()
    {
        _frames = new FrameWindow(AppParameters.Fps.AverageWindowSeconds,
                                  AppParameters.Fps.StaleFramesSeconds);
    }

    public void Start()
    {
        try
        {
            StopStaleSession();

            // The delivery rate cannot be set — delayed batches are handled by Average().
            _session = new TraceEventSession(AppParameters.Identity.EtwSession) { StopOnDispose = true };

            // Every keyword rather than 0x1: with 0x1 DXGI sent only object lifetime events and no
            // Present at all. The provider is quiet, so there is nothing to filter.
            _session.EnableProvider(DxgiProvider, TraceEventLevel.Verbose, AllKeywords);
            _session.EnableProvider(D3d9Provider, TraceEventLevel.Verbose, AllKeywords);

            Log.Info("ETW: DXGI and D3D9; DxgKrnl turns on only if they stay silent");

            _providerTimer = new Timer(_ => AdjustProviders(), null,
                                       AppParameters.Fps.FallbackCheckDelay, AppParameters.Fps.FallbackCheckPeriod);

            // Dynamic specifically: it loads the provider manifest and gives events with task names.
            // AllEvents does not parse the schema and returns "EventID(322)", which names nothing.
            _session.Source.Dynamic.All += OnEvent;

            Task.Run(() =>
            {
                try { _session.Source.Process(); }
                catch (Exception ex)
                {
                    Status = Text.FpsSessionBroken(ex.Message);
                    Log.Error("the ETW session ended — frames are no longer counted", ex);
                }
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            Status = Text.FpsNeedsAdmin;
            Log.Error("the ETW session did not start — no rights", ex);
            _session = null;
        }
        catch (Exception ex)
        {
            Status = Text.FpsNotStarted(ex.Message);
            Log.Error("the ETW session did not start", ex);
            _session = null;
        }
    }

    // Tears the session down: parsing the frame events is the main CPU cost.
    public void Stop()
    {
        Dispose();

        lock (_lock)
        {
            _frames.Reset();
            _source = Source.None;
            _trackedPid = 0;
            Array.Clear(_lastSeen);
            ResetProbe();
        }

        Status = null;
    }

    // The graphics kernel is switched on when needed: 66 000 events in eight seconds against 2 268.
    private void AdjustProviders()
    {
        TraceEventSession? session = _session;
        if (session is null) return;

        bool directXWorks;
        lock (_lock) directXWorks = _source is Source.Dxgi or Source.D3d9;

        try
        {
            if (!directXWorks && !_dxgKrnlEnabled)
            {
                session.EnableProvider(DxgKrnlProvider, TraceEventLevel.Informational, DxgKrnlBaseKeyword);
                _dxgKrnlEnabled = true;
                Log.Info("FPS: no DirectX events — enabling DxgKrnl " +
                         $"(looking for {string.Join(", ", AppParameters.Fps.DxgKrnlTasks)})");
            }
            else if (directXWorks && _dxgKrnlEnabled)
            {
                session.DisableProvider(DxgKrnlProvider);
                _dxgKrnlEnabled = false;
                lock (_lock) ResetProbe();
                Log.Info("FPS: frames come from DXGI — disabling DxgKrnl, it produces tens of " +
                         "thousands of extra events per second");
            }
        }
        catch (Exception ex)
        {
            Log.Error("could not reconfigure the ETW providers", ex);
        }
    }

    // After a crash the session stays in the system — it is torn down here.
    private static void StopStaleSession()
    {
        try
        {
            if (!TraceEventSession.GetActiveSessionNames().Contains(AppParameters.Identity.EtwSession))
                return;

            // A session left by a copy that crashed: it belongs to the system rather than to this
            // process, and the object that speaks to it has to be let go of as well.
            using TraceEventSession? stale =
                TraceEventSession.GetActiveSession(AppParameters.Identity.EtwSession);

            stale?.Stop();
        }
        catch { /* nothing to stop */ }
    }

    private void OnEvent(TraceEvent data)
    {
        Guid provider = data.ProviderGuid;

        Source source;
        if (provider == DxgiProvider) source = Source.Dxgi;
        else if (provider == D3d9Provider) source = Source.D3d9;
        else if (provider == DxgKrnlProvider) source = Source.DxgKrnl;
        else return;

        int pid = data.ProcessID;
        if (pid != ForegroundPid()) return;

        NoteProvider(pid, source, data);

        if (source != Source.DxgKrnl && !IsPresentStart(data, source)) return;

        Count(pid, source, data);
    }

    // The event numbers were never confirmed on any run, so the task name is what counts.
    private static bool IsPresentStart(TraceEvent data, Source source)
    {
        int id = (int)data.ID;
        if (id == (source == Source.Dxgi ? DxgiPresentStart : D3d9PresentStart)) return true;

        return data.Opcode == TraceEventOpcode.Start &&
               string.Equals(data.TaskName, "Present", StringComparison.OrdinalIgnoreCase);
    }

    private void NoteProvider(int pid, Source source, TraceEvent data)
    {
        lock (_lock)
        {
            if (pid != _trackedPid) return; // the counters follow the process; Count resets them

            _providerEvents[(int)source]++;
            if (double.IsNaN(_firstDxgkSeen)) _firstDxgkSeen = Now;

            if (source == Source.Dxgi)
            {
                string name = $"{data.TaskName}/{data.OpcodeName} (id {(int)data.ID})";
                _seenDxgi[name] = _seenDxgi.TryGetValue(name, out int seen) ? seen + 1 : 1;
            }

            if (_providersReported || double.IsNaN(_firstDxgkSeen)) return;
            if (Now - _firstDxgkSeen < AppParameters.Fps.ProviderReportSeconds) return;

            _providersReported = true;
            Log.Info($"events over {AppParameters.Fps.ProviderReportSeconds:0} s from process {pid}: " +
                     $"DXGI={_providerEvents[(int)Source.Dxgi]}, " +
                     $"D3D9={_providerEvents[(int)Source.D3d9]}, " +
                     $"DxgKrnl={_providerEvents[(int)Source.DxgKrnl]}" +
                     (_seenDxgi.Count == 0
                         ? ". DXGI sent nothing."
                         : $". DXGI sent: {string.Join(", ", _seenDxgi.OrderByDescending(p => p.Value).Take(8).Select(p => $"{p.Key}={p.Value}"))}"));
        }
    }

    private void Count(int pid, Source source, TraceEvent data)
    {
        lock (_lock)
        {
            // The game changed: another one may present its frames another way.
            if (pid != _trackedPid)
            {
                _trackedPid = pid;
                _source = Source.None;
                Array.Clear(_lastSeen);
                _frames.Reset();
                ResetProbe();
            }

            if (source == Source.DxgKrnl && !IsCountableDxgkEvent(data)) return;

            _lastSeen[(int)source] = Now;
            if (!Prefer(source)) return;

            _frames.Add(data.TimeStampRelativeMSec / 1000.0, Now);
        }
    }

    // The accurate source displaces the loose one at once, the other way only after silence. Under _lock.
    private bool Prefer(Source source)
    {
        if (source == _source) return true;

        if (source > _source)
        {
            _source = source;
            _frames.ClearFrames();
            return true;
        }

        if (Now - _lastSeen[(int)_source] < AppParameters.Fps.SourceStaleSeconds) return false;

        _source = source;
        _frames.ClearFrames();
        return true;
    }

    // The graphics kernel event numbers are undocumented and differ between Windows builds, so the
    // first second is spent listening. A preference order is not enough — an event can arrive many
    // times per frame (as PresentHistoryDetailed did), so the rate matters too.
    private bool IsCountableDxgkEvent(TraceEvent data)
    {
        int id = (int)data.ID;
        if (_dxgkEventId >= 0) return id == _dxgkEventId;

        // Present is logged as a Start/Stop pair — only the start counts, or there are twice the frames.
        if (data.Opcode != TraceEventOpcode.Start) return false;

        // If the manifest did not load this reads "EventID(322)" — then the search by number works.
        string task = data.TaskName;
        if (string.IsNullOrEmpty(task)) task = $"EventID({id})";

        _seenTasks[task] = _seenTasks.TryGetValue(task, out int seen) ? seen + 1 : 1;
        if (double.IsNaN(_firstDxgkSeen)) _firstDxgkSeen = Now;

        int rank = IndexOfTask(task, id);
        if (rank < 0)
        {
            ReportIfNothingMatches();
            return false;
        }

        if (double.IsNaN(_probeStarted)) _probeStarted = Now;

        _probeCounts[task] = _probeCounts.TryGetValue(task, out var probe)
            ? (probe.Count + 1, id, rank)
            : (1, id, rank);

        double elapsed = Now - _probeStarted;
        if (elapsed < AppParameters.Fps.TaskProbeSeconds) return false;

        ChooseDxgkEvent(elapsed);
        return _dxgkEventId >= 0 && id == _dxgkEventId;
    }

    private void ChooseDxgkEvent(double elapsed)
    {
        var measured = _probeCounts
            .Select(p => (Task: p.Key, p.Value.Id, p.Value.Rank, Rate: p.Value.Count / elapsed))
            .OrderBy(m => m.Rank)
            .ToList();

        string rates = string.Join(", ", measured.Select(m => $"{m.Task}≈{m.Rate:0}/s"));

        var chosen = measured.FirstOrDefault(m => m.Rate <= AppParameters.Fps.MaxPlausibleFps);
        if (chosen.Task is null)
        {
            _probeCounts.Clear();
            _probeStarted = Now;

            if (!_implausibleReported)
            {
                _implausibleReported = true;
                Log.Warn($"FPS: every candidate event arrives too often ({rates}) — no game " +
                         "runs that fast, so the event fires several times per frame. Still " +
                         "looking. Please report this along with these names.");
            }
            return;
        }

        _dxgkEventId = chosen.Id;
        _dxgkTask = chosen.Task;

        Log.Info($"DxgKrnl: counting \"{_dxgkTask}\" (event {_dxgkEventId}), " +
                 $"measured rates: {rates}");
    }

    // The only case where the FPS stays empty silently. What did arrive is written down: a suitable
    // name is moved from there into Fps.DxgKrnlTasks.
    private void ReportIfNothingMatches()
    {
        if (_noMatchReported || _dxgkEventId >= 0) return;
        if (Now - _firstDxgkSeen < AppParameters.Fps.NoMatchReportSeconds) return;

        if (_seenTasks.Values.Sum() < AppParameters.Fps.MinEventsToComplain) return;

        _noMatchReported = true;
        Log.Warn($"FPS: none of the events {string.Join(", ", AppParameters.Fps.DxgKrnlTasks)} arrived from " +
                 $"the game. What did arrive: {string.Join(", ", _seenTasks.OrderByDescending(p => p.Value).Select(p => $"{p.Key}={p.Value}"))}. " +
                 "Please report this: the list of events the app looks for is built in, and this " +
                 "game sends something else.");
    }

    private int IndexOfTask(string task, int id) => IndexOfTask(AppParameters.Fps.DxgKrnlTasks, task, id);

    // The place of an event in the preference list, or -1. Both task names and bare numbers are
    // allowed: when the provider manifest does not load, the event has no name at all.
    internal static int IndexOfTask(IReadOnlyList<string> tasks, string task, int id)
    {
        string number = id.ToString(CultureInfo.InvariantCulture);

        for (int i = 0; i < tasks.Count; i++)
        {
            string wanted = tasks[i].Trim();
            if (string.Equals(wanted, task, StringComparison.OrdinalIgnoreCase)) return i;
            if (string.Equals(wanted, number, StringComparison.Ordinal)) return i;
        }
        return -1;
    }

    private void ResetProbe()
    {
        _dxgkEventId = -1;
        _dxgkTask = null;
        _firstDxgkSeen = double.NaN;
        _noMatchReported = false;
        _implausibleReported = false;
        _providersReported = false;
        _probeStarted = double.NaN;
        _probeCounts.Clear();
        _seenTasks.Clear();
        _seenDxgi.Clear();
        Array.Clear(_providerEvents);
    }

    private static int _pidCache;
    private static double _pidCachedAt = double.NegativeInfinity;

    // Cached for 250 ms — this is called for every frame.
    private static int ForegroundPid()
    {
        if (Now - _pidCachedAt < 0.25) return _pidCache;
        _pidCachedAt = Now;

        IntPtr hwnd = Win32.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return _pidCache = 0;

        Win32.GetWindowThreadProcessId(hwnd, out uint pid);
        return _pidCache = (int)pid;
    }

    private static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    public double? Average()
    {
        lock (_lock) return _frames.Average(Now);
    }

    public double? FrameTimeMs()
    {
        lock (_lock) return _frames.FrameTimeMs(Now);
    }

    public void Dispose()
    {
        try
        {
            _providerTimer?.Dispose();

            if (_session is not null)
            {
                _session.Source.Dynamic.All -= OnEvent;
                _session.Dispose(); // StopOnDispose removes the session from the system
            }
        }
        catch { /* the session is already closed */ }

        _providerTimer = null;
        _dxgKrnlEnabled = false;
        _session = null;
    }
}
