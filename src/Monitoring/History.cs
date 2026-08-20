using System;
using System.Collections.Generic;
using System.Linq;

namespace SystemSpinnerX64.Monitoring;

/// <summary>
/// Tail of recent values for a chart. A list rather than a ring: there are a few hundred points,
/// the chart reads them in order from old to new, and a ring would have to be unrolled on every
/// redraw — more expensive than shifting the array now and then.
/// </summary>
public sealed class History
{
    private readonly List<double> _points = new();
    private readonly int _capacity;

    public History(int capacity)
    {
        _capacity = Math.Clamp(capacity, 10, 10_000);
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

    /// <summary>Snapshot for the chart. A copy: drawing runs on a different thread than polling.</summary>
    public IReadOnlyList<double> Snapshot()
    {
        int extra = Math.Max(0, _points.Count - _capacity);
        return _points.Skip(extra).ToList();
    }

    public void Clear() => _points.Clear();
}
