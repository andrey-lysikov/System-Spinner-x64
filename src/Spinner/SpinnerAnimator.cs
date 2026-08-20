using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Threading;
using SystemSpinnerX64.Configuration;
using SystemSpinnerX64.Diagnostics;
using SystemSpinnerX64.Platform;

namespace SystemSpinnerX64.Spinner;

/// <summary>
/// Spins the tray frames at a speed set by the CPU load: the busier it is, the faster they run.
/// That is the whole way System Spinner shows load — there may be no number anywhere.
///
/// The speed formula is taken from the macOS version unchanged, constants included: the icon has
/// to stay recognisable, and re-deriving it would mean a different-looking program.
/// </summary>
public sealed class SpinnerAnimator : IDisposable
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background);

    private readonly List<Bitmap> _frames = new();
    private readonly List<Icon> _icons = new();
    private readonly List<IntPtr> _handles = new();

    private SpinnerStyle _style = SpinnerCatalog.Fallback;
    private int _current;
    private double _interval = -1;
    private double _lastCpuLoad;

    /// <summary>Where the next frame goes. This call is what changes the tray icon.</summary>
    public event Action<Icon>? FrameReady;

    /// <summary>Spin the frames backwards.</summary>
    public bool Invert { get; set; }

    public SpinnerAnimator()
    {
        _timer.Tick += (_, _) => Advance();
    }

    /// <summary>Whether there is anything to show: an empty set means the resources were missing.</summary>
    public bool HasFrames => _icons.Count > 0;

    /// <summary>
    /// Prepares the frames again. Called when the set, the effect, the theme or the icon size
    /// changes — that is, whenever the old pictures stopped being usable.
    /// </summary>
    public void Load(SpinnerStyle style, SpinnerEffect effect, int iconSize, bool lightTheme)
    {
        _style = style;

        // The new frames are built before the old ones are freed. The tray icon points at one of
        // the old ones until the first new frame is handed over, and freeing that first leaves
        // the tray showing a destroyed handle.
        var frames = new List<Bitmap>();
        var handles = new List<IntPtr>();
        var icons = new List<Icon>();

        foreach (Bitmap frame in SpinnerFrames.Load(style, effect, iconSize, lightTheme))
        {
            frames.Add(frame);

            // Icon.FromHandle does not take ownership of the handle — it is kept here and freed
            // in ReleaseFrames(), or an hour of work would leak thousands.
            IntPtr handle = frame.GetHicon();
            handles.Add(handle);
            icons.Add(Icon.FromHandle(handle));
        }

        List<Icon> previousIcons = new(_icons);
        List<IntPtr> previousHandles = new(_handles);
        List<Bitmap> previousFrames = new(_frames);

        _icons.Clear();
        _handles.Clear();
        _frames.Clear();

        _icons.AddRange(icons);
        _handles.AddRange(handles);
        _frames.AddRange(frames);

        _current = 0;
        _interval = -1;

        Log.Info($"spinner: \"{style.Name}\", {_icons.Count} frames, {iconSize} px, effect {effect}");

        if (_icons.Count > 0) FrameReady?.Invoke(_icons[0]);

        // The tray is showing a new frame now: the old ones can go.
        Release(previousIcons, previousHandles, previousFrames);

        // The speed is known from the last poll — otherwise the icon would stand still after
        // a set change until the sensors are read again.
        UpdateSpeed(_lastCpuLoad);
    }

    /// <summary>Matches the speed to the CPU load in percent.</summary>
    public void UpdateSpeed(double cpuLoadPercent)
    {
        _lastCpuLoad = cpuLoadPercent;
        if (_icons.Count == 0) return;

        // The load is divided by the frame count: in a long set one frame is a smaller share of
        // the cycle, and without this long sets would spin visibly faster at the same load.
        double load = Math.Clamp(cpuLoadPercent / _icons.Count, 1.0, 100.0);
        double interval = Math.Max(AppParameters.Spinning.MinIntervalSeconds,
                                   0.25 / load * _style.SpeedCoefficient);

        if (_interval > 0 &&
            Math.Abs(interval - _interval) <= _interval * AppParameters.Spinning.SpeedTolerance) return;

        _interval = interval;
        _timer.Interval = TimeSpan.FromSeconds(interval);
        if (!_timer.IsEnabled) _timer.Start();
    }

    /// <summary>Stops the spin, leaving the current frame in place.</summary>
    public void Stop()
    {
        _timer.Stop();
        _interval = -1;
    }

    /// <summary>Returns the icon to the first frame — that reads as asleep rather than stuck.</summary>
    public void Rewind()
    {
        if (_icons.Count == 0) return;
        _current = 0;
        FrameReady?.Invoke(_icons[0]);
    }

    private void Advance()
    {
        if (_icons.Count == 0) return;

        _current += Invert ? -1 : 1;
        if (_current >= _icons.Count) _current = 0;
        else if (_current < 0) _current = _icons.Count - 1;

        FrameReady?.Invoke(_icons[_current]);
    }

    private void ReleaseFrames()
    {
        Release(_icons, _handles, _frames);

        _icons.Clear();
        _handles.Clear();
        _frames.Clear();
    }

    private static void Release(List<Icon> icons, List<IntPtr> handles, List<Bitmap> frames)
    {
        foreach (Icon icon in icons) icon.Dispose();
        foreach (IntPtr handle in handles) Win32.DestroyIcon(handle);
        foreach (Bitmap frame in frames) frame.Dispose();
    }

    public void Dispose()
    {
        _timer.Stop();
        ReleaseFrames();
    }
}
