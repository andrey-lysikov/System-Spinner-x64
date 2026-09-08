//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using SystemSpinnerX64.Configuration;
using SystemSpinnerX64.Diagnostics;
using LibreHardwareMonitor.Hardware;

namespace SystemSpinnerX64.Monitoring;

// What a fan is for. Detected sensors are sorted into the panel slots by this.
public enum FanRole
{
    // A graphics card fan: the sensor belongs to the card itself.
    Gpu,

    // An AIO pump or its radiator fans: Pump, AIO, Water, Kraken and the like.
    Aio,

    // The CPU cooler: the sensor name contains CPU.
    Cpu,

    // Everything else — case fans, the PSU fan, unidentified headers.
    Case
}

// One detected fan sensor: what it is, where it sits and what it read while scanning.
public sealed record FanSensor(string Name, string HardwareName, FanRole Role, double? Rpm)
{
    // Line for the scan report in the log.
    public string Describe =>
        $"[{Role}] {HardwareName} / {Name} = {(Rpm is null ? "—" : Rpm.Value.ToString("0"))} rpm";
}

// Works out whose fan this is from the sensor name and the hardware it was found on.
internal static class FanClassifier
{
    // Words that give an AIO away, in the sensor name and in the controller name.
    private static readonly string[] AioMarkers =
    {
        "pump", "aio", "water", "liquid", "насос", "kraken", "capellix", "commander",
        "quadro", "octo", "d5", "ddc", "h100", "h115", "h150", "h170", "galahad"
    };

    public static FanRole Classify(string sensorName, IHardware owner, bool underGpu) =>
        Classify(sensorName, owner.Name, owner.HardwareType, underGpu || IsGpu(owner));

    // The same on plain values instead of a hardware object, so it can be tested: a stub for
    // IHardware would be longer than the logic under test.
    public static FanRole Classify(string sensorName, string hardwareName, HardwareType hardwareType, bool onGpu)
    {
        if (onGpu) return FanRole.Gpu;

        string name = sensorName.ToLowerInvariant();
        string hardware = hardwareName.ToLowerInvariant();

        // A pump is named in many ways but almost always with one of these words; fans hanging off
        // an AIO controller count as AIO too — they sit on its radiator.
        if (AioMarkers.Any(m => name.Contains(m, StringComparison.Ordinal))) return FanRole.Aio;

        if (hardwareType is HardwareType.Cooler &&
            AioMarkers.Any(m => hardware.Contains(m, StringComparison.Ordinal))) return FanRole.Aio;

        if (name.Contains("cpu", StringComparison.Ordinal)) return FanRole.Cpu;

        return FanRole.Case;
    }

    public static bool IsGpu(IHardware hw) =>
        hw.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;
}

// Walks the hardware tree and refreshes the sensor values.
internal sealed class UpdateVisitor : IVisitor
{
    public void VisitComputer(IComputer computer) => computer.Traverse(this);

    public void VisitHardware(IHardware hardware)
    {
        hardware.Update();
        foreach (IHardware sub in hardware.SubHardware) sub.Accept(this);
    }

    public void VisitSensor(ISensor sensor) { }
    public void VisitParameter(IParameter parameter) { }
}

// Polling the hardware through LibreHardwareMonitor: keeps the sensor tree open, refreshes the
// values each cycle and picks out the ones the panel shows.
public sealed class HardwareMonitor : IDisposable
{
    private readonly Computer _computer;
    private readonly UpdateVisitor _visitor = new();
    private readonly AppConfig _cfg;

    private IHardware? _cpu;
    private IHardware? _gpu;
    private IHardware? _memory;
    private readonly List<IHardware> _fanSources = new();

    public HardwareMonitor(AppConfig cfg)
    {
        _cfg = cfg;
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,   // SuperIO sensors: case and CPU fan speeds
            IsControllerEnabled = true,    // Aquacomputer / Corsair / NZXT — the AIO pump speed
            IsStorageEnabled = false,
            IsNetworkEnabled = false
        };
    }

    // For the requirement checks in PlatformGuard.
    public string? CpuName => _cpu?.Name;
    public string? GpuName => _gpu?.Name;

    public void Open()
    {
        _computer.Open();
        _computer.Accept(_visitor);
        Rebind();
    }

    private void Rebind()
    {
        _cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        _memory = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Memory);

        // Discrete cards first, or GpuIndex = 0 would be the integrated one on a Core Ultra or a
        // Ryzen with graphics. Intel goes last; an Arc is picked with GpuIndex.
        var gpus = _computer.Hardware.Where(FanClassifier.IsGpu)
                                     .OrderBy(h => h.HardwareType == HardwareType.GpuIntel)
                                     .ThenByDescending(h => h.HardwareType == HardwareType.GpuNvidia)
                                     .ToList();
        _gpu = gpus.Count == 0 ? null : gpus[Math.Clamp(_cfg.GpuIndex, 0, gpus.Count - 1)];

        _fanSources.Clear();
        foreach (var hw in _computer.Hardware)
        {
            if (hw.HardwareType is HardwareType.Motherboard
                or HardwareType.SuperIO
                or HardwareType.Cooler
                or HardwareType.EmbeddedController
                or HardwareType.Psu)
            {
                _fanSources.Add(hw);
                _fanSources.AddRange(Flatten(hw));
            }
        }
    }

    private static IEnumerable<IHardware> Flatten(IHardware hw)
    {
        foreach (var sub in hw.SubHardware)
        {
            yield return sub;
            foreach (var deeper in Flatten(sub)) yield return deeper;
        }
    }

    public Readings Read()
    {
        _computer.Accept(_visitor);

        SensorNamesConfig names = _cfg.Sensors;

        var r = new Readings
        {
            CpuLoad = Find(_cpu, SensorType.Load, names.CpuLoad),
            CpuTempC = Find(_cpu, SensorType.Temperature, names.CpuTemp),
            CpuPowerW = Find(_cpu, SensorType.Power, names.CpuPower),

            CpuClockMhz = PerformanceCoreClock(),

            SysMemUsedGb = Find(_memory, SensorType.Data, names.MemoryUsed),
            SysMemFreeGb = Find(_memory, SensorType.Data, names.MemoryAvailable),

            GpuLoad = Find(_gpu, SensorType.Load, names.GpuLoad),
            GpuTempC = Find(_gpu, SensorType.Temperature, names.GpuTemp),
            GpuPowerW = Find(_gpu, SensorType.Power, names.GpuPower),
            GpuClockMhz = Find(_gpu, SensorType.Clock, names.GpuClock),
            GpuFanRpm = ReadFan(_gpu is null ? _fanSources : Prepend(_gpu),
                                _cfg.Fans.Gpu, _cfg.Fans.AverageGpu),

            CpuFanRpm = ReadFan(_fanSources, _cfg.Fans.Cpu, _cfg.Fans.AverageCpu),
            AioFanRpm = ReadFan(_fanSources, _cfg.Fans.Aio, _cfg.Fans.AverageAio),

            ExtraFanRpm = _cfg.Fans.Extra
                              .Select(name => FindFan(_fanSources, new[] { name }))
                              .ToList()
        };

        // LHM reports video memory in MB — converted to GB.
        r.GpuMemUsedGb = Find(_gpu, SensorType.SmallData, names.GpuMemory) / 1024.0;
        r.GpuMemTotalGb = Find(_gpu, SensorType.SmallData, names.GpuMemoryTotal) / 1024.0;

        LogSensorChoice(names);
        LogMemoryStall(r);

        return r;
    }

    private bool _memoryStallLogged;

    // Video memory reading full while the card is idle is the shape the fault takes. Only the
    // other sensors, read at that moment, say whether it is the sensor: written down once.
    private void LogMemoryStall(Readings r)
    {
        if (_memoryStallLogged) return;
        if (r.GpuMemTotalGb is not double total || total <= 0) return;
        if (r.GpuMemUsedGb is not double used || used < total) return;

        _memoryStallLogged = true;

        Log.Warn($"video memory reads full: {used:0.#} of {total:0.#} GB with the card at " +
                 $"{r.GpuLoad ?? 0:0} % — every video memory sensor as it stands:");
        LogVideoMemory();
    }

    private bool _sensorChoiceLogged;

    // Once per run: which sensor each configurable reading came from, and what it said. A number
    // that looks wrong is nearly always the wrong sensor, and the percentage never says which.
    private void LogSensorChoice(SensorNamesConfig names)
    {
        if (_sensorChoiceLogged) return;
        _sensorChoiceLogged = true;

        Log.Info("sensors in use:");

        Report("CpuLoad", _cpu, SensorType.Load, names.CpuLoad);
        Report("CpuTemp", _cpu, SensorType.Temperature, names.CpuTemp);
        Report("CpuPower", _cpu, SensorType.Power, names.CpuPower);
        Report("MemoryUsed", _memory, SensorType.Data, names.MemoryUsed);
        Report("MemoryAvailable", _memory, SensorType.Data, names.MemoryAvailable);
        Report("GpuLoad", _gpu, SensorType.Load, names.GpuLoad);
        Report("GpuTemp", _gpu, SensorType.Temperature, names.GpuTemp);
        Report("GpuPower", _gpu, SensorType.Power, names.GpuPower);
        Report("GpuClock", _gpu, SensorType.Clock, names.GpuClock);
        Report("GpuMemory", _gpu, SensorType.SmallData, names.GpuMemory);
        Report("GpuMemoryTotal", _gpu, SensorType.SmallData, names.GpuMemoryTotal);

        LogVideoMemory();

        void Report(string setting, IHardware? hw, SensorType type, IReadOnlyList<string> wanted)
        {
            ISensor? sensor = FindSensor(hw, type, wanted);

            Log.Info(sensor is null
                ? $"  {setting}: nothing matched {string.Join(", ", wanted)}"
                : $"  {setting}: \"{sensor.Name}\" ({sensor.SensorType}) = " +
                  $"{sensor.Value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "—"}");
        }
    }

    // Every video-memory sensor the card offers, whatever it is called. The pair above is chosen
    // from names, and when the choice is wrong this is the list to choose from.
    private void LogVideoMemory()
    {
        if (_gpu is null) return;

        foreach (ISensor sensor in Collect(_gpu, SensorType.SmallData)
                                   .Concat(Collect(_gpu, SensorType.Data))
                                   .Where(s => s.Name.Contains("memory", StringComparison.OrdinalIgnoreCase)))
        {
            Log.Info($"  video memory sensor \"{sensor.Name}\" ({sensor.SensorType}) = " +
                     $"{sensor.Value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "—"}");
        }
    }

    private IEnumerable<IHardware> Prepend(IHardware first)
    {
        yield return first;
        foreach (var hw in Flatten(first)) yield return hw;
        foreach (var hw in _fanSources) yield return hw;
    }

    private static double? Find(IHardware? hw, SensorType type, IReadOnlyList<string> names) =>
        FindSensor(hw, type, names)?.Value;

    // An exact match across the whole list first, then a substring. The sensor is returned rather
    // than its value: only it carries the name a reading came from.
    private static ISensor? FindSensor(IHardware? hw, SensorType type, IReadOnlyList<string> names)
    {
        if (hw is null) return null;
        var sensors = Collect(hw, type).ToList();
        if (sensors.Count == 0) return null;

        foreach (var name in names)
        {
            var exact = sensors.FirstOrDefault(s =>
                s.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && s.Value.HasValue);
            if (exact is not null) return exact;
        }
        foreach (var name in names)
        {
            var partial = sensors.FirstOrDefault(s =>
                s.Name.Contains(name, StringComparison.OrdinalIgnoreCase) && s.Value.HasValue);
            if (partial is not null) return partial;
        }
        return null;
    }

    private static double? ReadFan(IEnumerable<IHardware> sources, IReadOnlyList<string> names, bool average) =>
        average ? AverageFans(sources, names) : FindFan(sources, names);

    private static double? FindFan(IEnumerable<IHardware> sources, IReadOnlyList<string> names)
    {
        var sensors = sources.SelectMany(h => Collect(h, SensorType.Fan))
                             .Where(s => s.Value.HasValue)
                             .ToList();
        if (sensors.Count == 0) return null;

        foreach (var name in names)
        {
            var exact = sensors.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (exact?.Value is float v) return v;
        }
        foreach (var name in names)
        {
            var partial = sensors.FirstOrDefault(s => s.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (partial?.Value is float v) return v;
        }
        return null;
    }

    private bool _clockSourcesLogged;

    // On hybrid Intel the cores are named "P-Core #1"; on plain Intel and on AMD they are
    // "Core #1". So explicit P-cores first, and failing that every core but the efficient ones.
    private double? PerformanceCoreClock()
    {
        if (_cpu is null) return null;

        string wanted = _cfg.Sensors.CpuClockCores;
        string excluded = AppParameters.Sensors.ClockExclude;

        var clocks = Collect(_cpu, SensorType.Clock).Where(s => s.Value.HasValue).ToList();

        var performance = clocks.Where(s => s.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase))
                                .ToList();

        if (performance.Count == 0)
        {
            performance = clocks.Where(s => s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) &&
                                            !s.Name.Contains(excluded, StringComparison.OrdinalIgnoreCase))
                                .ToList();
        }

    // There is no other way to check the right sensors were picked: the names depend on hardware.
        if (!_clockSourcesLogged)
        {
            _clockSourcesLogged = true;
            Log.Info(performance.Count == 0
                ? "CPU clock: no suitable sensors found"
                : $"CPU clock is averaged over: {string.Join(", ", performance.Select(s => s.Name))}");
        }

        return performance.Count == 0 ? null : performance.Average(s => (double)s.Value!.Value);
    }

    // Zeros are not discarded: at idle a card stops its fans on purpose, and "0" is a reading
    // rather than missing data.
    private static double? AverageFans(IEnumerable<IHardware> sources, IReadOnlyList<string> names)
    {
        if (names.Count == 0) return null;

        // The same hardware can arrive twice — the card itself and the card in the source list.
        var sensors = sources.SelectMany(h => Collect(h, SensorType.Fan))
                             .Where(s => s.Value.HasValue)
                             .GroupBy(s => s.Identifier.ToString())
                             .Select(g => g.First())
                             .Where(s => names.Any(n =>
                                 s.Name.Equals(n, StringComparison.OrdinalIgnoreCase) ||
                                 s.Name.Contains(n, StringComparison.OrdinalIgnoreCase)))
                             .ToList();

        return sensors.Count == 0 ? null : sensors.Average(s => (double)s.Value!.Value);
    }

    private static IEnumerable<ISensor> Collect(IHardware hw, SensorType type)
    {
        foreach (var s in hw.Sensors)
            if (s.SensorType == type) yield return s;

        foreach (var sub in hw.SubHardware)
            foreach (var s in Collect(sub, type)) yield return s;
    }

    // --- Fan auto-detection ---

    public IReadOnlyList<FanSensor> ScanFans()
    {
        _computer.Accept(_visitor);

        var found = new List<FanSensor>();
        foreach (var hw in _computer.Hardware) CollectFans(hw, FanClassifier.IsGpu(hw), found);
        return found;
    }

    private static void CollectFans(IHardware hw, bool underGpu, List<FanSensor> into)
    {
        foreach (var s in hw.Sensors)
        {
            if (s.SensorType != SensorType.Fan) continue;
            into.Add(new FanSensor(s.Name, hw.Name, FanClassifier.Classify(s.Name, hw, underGpu), s.Value));
        }

        foreach (var sub in hw.SubHardware)
            CollectFans(sub, underGpu || FanClassifier.IsGpu(sub), into);
    }

    // For when auto-detection missed.
    public string DumpSensors()
    {
        _computer.Accept(_visitor);
        var sb = new StringBuilder();
        foreach (var hw in _computer.Hardware) Dump(hw, sb, 0);
        return sb.ToString();
    }

    private static void Dump(IHardware hw, StringBuilder sb, int depth)
    {
        string pad = new(' ', depth * 2);
        sb.AppendLine($"{pad}[{hw.HardwareType}] {hw.Name}");
        foreach (var s in hw.Sensors.OrderBy(s => s.SensorType).ThenBy(s => s.Name))
            sb.AppendLine($"{pad}  {s.SensorType,-12} \"{s.Name}\" = {s.Value?.ToString("0.##") ?? "—"}");
        foreach (var sub in hw.SubHardware) Dump(sub, sb, depth + 1);
    }

    public void Dispose()
    {
        try { _computer.Close(); } catch { /* the driver may already be unloaded */ }
    }
}
