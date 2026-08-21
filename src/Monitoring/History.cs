using System;
using System.Collections.Generic;
using System.Linq;

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
