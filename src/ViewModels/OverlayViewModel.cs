using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SystemSpinnerX64.Configuration;
using SystemSpinnerX64.Monitoring;

namespace SystemSpinnerX64.ViewModels;

// The panel rows. Which values stand where comes from the config: the rows and their order are
// AppearanceFullScreen/Row1, Row2, …
public sealed class OverlayViewModel : Observable
{
    private string? _notice;

    private readonly WarnConfig _warn;
    private readonly int _extraFanCount;

    // Every cell built for a value. A value may stand in more than one row, and each of those
    // cells is its own object: the column widths are set per cell, and one object shared between
    // two columns would end up with the width of whichever was measured last.
    private readonly Dictionary<OverlayMetric, List<Metric>> _cells = new();

    // Cells for ExtraFan names, one list per place ExtraFans stands in the rows.
    private readonly List<List<Metric>> _extraFans = new();

    public OverlayViewModel(WarnConfig warn, IReadOnlyList<OverlayRow> rows, int extraFans = 0)
    {
        _warn = warn;
        _extraFanCount = extraFans;

        Groups = new ObservableCollection<MetricGroup>(
            rows.Select(row => new MetricGroup(row.Title, Cells(row).ToArray()))
                .Where(group => group.Metrics.Count > 0));
    }

    // One config name to one cell, except ExtraFans: that is as many cells as there are names in
    // ExtraFan under [Hardware].
    private IEnumerable<Metric> Cells(OverlayRow row)
    {
        foreach (OverlayMetric name in row.Metrics)
        {
            if (name == OverlayMetric.ExtraFans)
            {
                var fans = new List<Metric>();
                for (int i = 0; i < _extraFanCount; i++) fans.Add(new Metric("RPM", 4));

                _extraFans.Add(fans);
                foreach (Metric fan in fans) yield return fan;
                continue;
            }

            yield return Cell(name);
        }
    }

    private Metric Cell(OverlayMetric name)
    {
        (string unit, int slots) = Shape(name);
        var metric = new Metric(unit, slots);

        if (!_cells.TryGetValue(name, out List<Metric>? cells)) _cells[name] = cells = new List<Metric>();
        cells.Add(metric);

        return metric;
    }

    // The unit and the digits in the longest expected value. Four for GB means "99.9": on a 128 GB
    // machine the row shifts, but a fifth digit would mean a gap in front of every normal value.
    private static (string Unit, int Slots) Shape(OverlayMetric name) => name switch
    {
        OverlayMetric.CpuLoad or OverlayMetric.GpuLoad => ("%", 3),
        OverlayMetric.CpuTemp or OverlayMetric.GpuTemp => ("°C", 3),
        OverlayMetric.CpuPower or OverlayMetric.GpuPower => ("W", 3),
        OverlayMetric.CpuClock or OverlayMetric.GpuClock => ("MHz", 4),
        OverlayMetric.SysMemory or OverlayMetric.GpuMemory => ("GB", 4),
        OverlayMetric.CpuFan or OverlayMetric.GpuFan => ("RPM", 4),

        // Two fan speeds can stand side by side, and without a tag it is unclear which is the pump.
        OverlayMetric.AioFan => ("RPM/AIO", 4),

        OverlayMetric.Fps => ("avg", 3),
        OverlayMetric.FrameTime => ("ms", 3),

        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "no cell for this value")
    };

    public ObservableCollection<MetricGroup> Groups { get; }

    // Cell widths by column: three digits for a percentage, four for a clock, and one column spans
    // every row — which is why the numbers of the rows line up.
    public void Layout(Func<int, double> measureDigits, Func<string, double> measureUnit,
                       double unitGap, double columnGap)
    {
        int columns = Groups.Count == 0 ? 0 : Groups.Max(g => g.Metrics.Count);
        for (int column = 0; column < columns; column++)
        {
            var cells = Groups.Where(g => g.Metrics.Count > column)
                              .Select(g => g.Metrics[column])
                              .ToList();

            double valueWidth = measureDigits(cells.Max(m => m.ValueSlots));
            double unitWidth = cells.Max(m => measureUnit(m.Unit));

            foreach (Metric cell in cells)
            {
                cell.ValueWidth = valueWidth;
                cell.CellWidth = valueWidth + unitGap + unitWidth + columnGap;
            }
        }
    }

    // The notice line at the bottom: sensor errors, frame counter state.
    public string? Notice
    {
        get => _notice;
        set => Set(ref _notice, value);
    }

    public void Apply(Readings r)
    {
        Show(OverlayMetric.CpuLoad, r.CpuLoad, threshold: _warn.CpuUsage);
        Show(OverlayMetric.CpuTemp, r.CpuTempC, threshold: _warn.CpuTemp);
        Show(OverlayMetric.CpuPower, r.CpuPowerW);
        Show(OverlayMetric.CpuClock, r.CpuClockMhz);
        Show(OverlayMetric.SysMemory, r.SysMemUsedGb, decimals: 1);
        Show(OverlayMetric.CpuFan, r.CpuFanRpm);

        // There may be no pump at all; the same goes for Extra names that match nothing.
        ShowOrHide(OverlayMetric.AioFan, r.AioFanRpm);

        foreach (List<Metric> fans in _extraFans)
            for (int i = 0; i < fans.Count; i++)
                fans[i].UpdateOrHide(i < r.ExtraFanRpm.Count ? r.ExtraFanRpm[i] : null);

        Show(OverlayMetric.GpuLoad, r.GpuLoad, threshold: _warn.GpuUsage);
        Show(OverlayMetric.GpuTemp, r.GpuTempC, threshold: _warn.GpuTemp);
        Show(OverlayMetric.GpuPower, r.GpuPowerW);
        Show(OverlayMetric.GpuClock, r.GpuClockMhz);
        Show(OverlayMetric.GpuMemory, r.GpuMemUsedGb, decimals: 1);
        Show(OverlayMetric.GpuFan, r.GpuFanRpm);
    }

    // FPS is not shown above MaxShownFps: a fourth digit would cost the whole column its width.
    public void ApplyFps(double? avgFps, double? frameTimeMs)
    {
        Show(OverlayMetric.Fps,
             avgFps is null ? null : Math.Min(avgFps.Value, AppParameters.Overlay.MaxShownFps));

        Show(OverlayMetric.FrameTime, frameTimeMs);
    }

    private void Show(OverlayMetric name, double? value, int decimals = 0, double threshold = 0)
    {
        if (!_cells.TryGetValue(name, out List<Metric>? cells)) return;

        foreach (Metric cell in cells) cell.Update(value, threshold, decimals);
    }

    private void ShowOrHide(OverlayMetric name, double? value)
    {
        if (!_cells.TryGetValue(name, out List<Metric>? cells)) return;

        foreach (Metric cell in cells) cell.UpdateOrHide(value);
    }
}
