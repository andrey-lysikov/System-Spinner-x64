using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SystemSpinnerX64.Configuration;
using SystemSpinnerX64.Monitoring;

namespace SystemSpinnerX64.ViewModels;

/// <summary>
/// The three panel rows. To add a metric: create a <see cref="Metric"/>, put it in a group and
/// fill it in <see cref="Apply"/>.
/// </summary>
public sealed class OverlayViewModel : Observable
{
    private string? _notice;

    // Order in a row: load, temperature, power, clock, memory, fan speed. The second number is
    // the digits in the longest expected value. Four for GB means "99.9": on a 128 GB machine the
    // row shifts, but a fifth digit would mean a gap in front of every normal value.
    private readonly Metric _cpuLoad = new("%", 3);
    private readonly Metric _cpuTemp = new("°C", 3);
    private readonly Metric _cpuPower = new("W", 3);
    private readonly Metric _cpuClock = new("MHz", 4);
    private readonly Metric _sysMem = new("GB", 4);
    private readonly Metric _cpuFan = new("RPM", 4);

    // The CPU row has two fan speeds in a row, and without a tag it is unclear which is the pump.
    private readonly Metric _aioFan = new("RPM/AIO", 4);

    private readonly List<Metric> _extraFans = new();

    private readonly Metric _gpuLoad = new("%", 3);
    private readonly Metric _gpuTemp = new("°C", 3);
    private readonly Metric _gpuPower = new("W", 3);
    private readonly Metric _gpuClock = new("MHz", 4);
    private readonly Metric _gpuMem = new("GB", 4);
    private readonly Metric _gpuFan = new("RPM", 4);

    private readonly Metric _fpsAvg = new("avg", 3);
    private readonly Metric _frameTime = new("ms", 3);

    private readonly WarnConfig _warn;

    /// <param name="extraFans">
    /// The cells are created up front rather than as work goes on: the column widths depend on
    /// what the panel holds, and they are computed once.
    /// </param>
    public OverlayViewModel(WarnConfig warn, int extraFans = 0)
    {
        _warn = warn;

        for (int i = 0; i < extraFans; i++) _extraFans.Add(new Metric("RPM", 4));

        var cpu = new List<Metric> { _cpuLoad, _cpuTemp, _cpuPower, _cpuClock, _sysMem, _cpuFan, _aioFan };
        cpu.AddRange(_extraFans);

        Groups = new ObservableCollection<MetricGroup>
        {
            new("CPU", cpu.ToArray()),
            new("GPU", _gpuLoad, _gpuTemp, _gpuPower, _gpuClock, _gpuMem, _gpuFan),
            new("FPS", _fpsAvg, _frameTime)
        };
    }

    public ObservableCollection<MetricGroup> Groups { get; }

    /// <summary>
    /// Cell widths by column: three digits for a percentage, four for a clock, and one column
    /// spans every row — which is why the CPU, GPU and FPS numbers line up.
    /// </summary>
    /// <param name="columnGap">
    /// A guaranteed minimum rather than an exact distance: where the FPS row has a wider label,
    /// CPU and GPU keep a tail. The skew cannot be removed without breaking the alignment, and
    /// without the margin columns with equal labels run together.
    /// </param>
    public void Layout(Func<int, double> measureDigits, Func<string, double> measureUnit,
                       double unitGap, double columnGap)
    {
        int columns = Groups.Max(g => g.Metrics.Count);
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

    /// <summary>The notice line at the bottom: sensor errors, frame counter state.</summary>
    public string? Notice
    {
        get => _notice;
        set => Set(ref _notice, value);
    }

    public void Apply(Readings r)
    {
        _cpuLoad.Update(r.CpuLoad, _warn.CpuLoad);
        _cpuTemp.Update(r.CpuTempC, _warn.CpuTemp);
        _cpuPower.Update(r.CpuPowerW);
        _cpuClock.Update(r.CpuClockMhz);
        _sysMem.Update(r.SysMemUsedGb, 1);
        _cpuFan.Update(r.CpuFanRpm);

        // There may be no pump at all; the same goes for Extra names that match nothing.
        _aioFan.UpdateOrHide(r.AioFanRpm);

        for (int i = 0; i < _extraFans.Count; i++)
            _extraFans[i].UpdateOrHide(i < r.ExtraFanRpm.Count ? r.ExtraFanRpm[i] : null);

        _gpuLoad.Update(r.GpuLoad, _warn.GpuLoad);
        _gpuTemp.Update(r.GpuTempC, _warn.GpuTemp);
        _gpuPower.Update(r.GpuPowerW);
        _gpuClock.Update(r.GpuClockMhz);
        _gpuMem.Update(r.GpuMemUsedGb, 1);
        _gpuFan.Update(r.GpuFanRpm);
    }

    /// <summary>FPS is not shown above this: a fourth digit would cost the whole column its width.</summary>

    public void ApplyFps(double? avgFps, double? frameTimeMs)
    {
        _fpsAvg.Update(avgFps is null ? null : Math.Min(avgFps.Value, AppParameters.Overlay.MaxShownFps));
        _frameTime.Update(frameTimeMs);
    }
}
