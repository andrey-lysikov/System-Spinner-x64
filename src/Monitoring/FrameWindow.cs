using System.Collections.Generic;

namespace SystemSpinnerX64.Monitoring;

/// <summary>
/// Frame timestamps and the average FPS over them. Split out of <see cref="FpsCounter"/> so it
/// can be tested: everything there comes from ETW, and <c>TraceEvent</c> cannot be built in a test.
///
/// The stamps live on the trace clock — the time of the event, not of its parsing. ETW delivers
/// events in batches, and stamping them at parse time would give a whole batch nearly the same
/// time, sending the FPS into the tens of thousands. The process clock is only used to tell how
/// long ago the last event was: the two scales must not be mixed.
/// </summary>
internal sealed class FrameWindow
{
    private readonly List<double> _frames = new();
    private readonly double _windowSeconds;
    private readonly double _staleSeconds;

    private double _lastEventTime = double.NaN;
    private double _lastEventWall = double.NaN;

    /// <param name="windowSeconds">Averaging window.</param>
    /// <param name="staleSeconds">How much silence to tolerate before admitting there are no frames.</param>
    public FrameWindow(double windowSeconds, double staleSeconds)
    {
        _windowSeconds = windowSeconds;
        _staleSeconds = staleSeconds;
    }

    public int Count => _frames.Count;

    /// <summary>Forget the frames but keep the clock link: what happens on a source change.</summary>
    public void ClearFrames() => _frames.Clear();

    /// <summary>Forget everything: another process means another clock and another way to present.</summary>
    public void Reset()
    {
        _frames.Clear();
        _lastEventTime = double.NaN;
        _lastEventWall = double.NaN;
    }

    /// <param name="stamp">Frame stamp on the trace clock, seconds.</param>
    /// <param name="wallNow">"Now" on the process clock.</param>
    public void Add(double stamp, double wallNow)
    {
        // Events from different providers can arrive out of order, and the list is binary-searched.
        if (_frames.Count > 0 && stamp < _frames[^1]) stamp = _frames[^1];

        _lastEventTime = stamp;
        _lastEventWall = wallNow;

        _frames.Add(stamp);
        Trim(wallNow);
    }

    /// <summary>
    /// The window stretches when no frame fell inside it but the last one arrived recently:
    /// a delayed batch left the strict window empty and made a dash flicker on the panel.
    /// </summary>
    public double? Average(double wallNow)
    {
        if (_frames.Count < 2) return null;

        double now = EventNow(wallNow);
        int i = FirstIndexAtOrAfter(now - _windowSeconds);

        if (_frames.Count - i < 2)
        {
            if (now - _frames[^1] > _staleSeconds) return null;

            i = FirstIndexAtOrAfter(now - _windowSeconds - _staleSeconds);
            if (_frames.Count - i < 2) return null;
        }

        double span = _frames[^1] - _frames[i];
        return span > 0 ? (_frames.Count - i - 1) / span : null;
    }

    public double? FrameTimeMs(double wallNow)
    {
        double? fps = Average(wallNow);
        return fps is > 0 ? 1000.0 / fps.Value : null;
    }

    // "Now" on the trace clock: the averaging window has to live on the same scale as the stamps.
    private double EventNow(double wallNow) =>
        double.IsNaN(_lastEventTime) ? 0 : _lastEventTime + (wallNow - _lastEventWall);

    private void Trim(double wallNow)
    {
        // Room for the stretched window — otherwise the leeway in Average() has nothing to work on.
        double cutoff = EventNow(wallNow) - _windowSeconds - _staleSeconds - 1;
        int drop = 0;
        while (drop < _frames.Count && _frames[drop] < cutoff) drop++;
        if (drop > 0) _frames.RemoveRange(0, drop);
    }

    private int FirstIndexAtOrAfter(double timestamp)
    {
        int i = _frames.BinarySearch(timestamp);
        return i < 0 ? ~i : i;
    }
}
