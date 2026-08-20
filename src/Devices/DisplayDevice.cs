using System;
using System.Linq;
using SystemSpinnerX64.Diagnostics;

namespace SystemSpinnerX64.Devices;

/// <summary>
/// One attached screen and what can be driven on it. An external monitor answers over DDC/CI,
/// a built-in laptop panel through WMI; the difference stops here.
///
/// Current values are kept in memory. Reading them from the monitor on every key press is not
/// an option: one DDC exchange takes tens of milliseconds, and the key hook has to return fast
/// or Windows switches it off.
/// </summary>
internal sealed class DisplayDevice : IDisposable
{
    private readonly MonitorControl.PhysicalMonitor[] _physical;

    private DisplayDevice(IntPtr monitor, string name, bool isInternal,
                          MonitorControl.PhysicalMonitor[] physical)
    {
        Monitor = monitor;
        Name = name;
        IsInternal = isInternal;
        _physical = physical;
    }

    public IntPtr Monitor { get; }

    /// <summary>The name as the system gives it: "Dell U2720Q" or "Generic PnP Monitor".</summary>
    public string Name { get; }

    public bool IsInternal { get; }

    /// <summary>Brightness in percent, or null when the screen does not drive it.</summary>
    public double? Brightness { get; private set; }

    /// <summary>Monitor speaker volume over DDC, or null when there are no speakers.</summary>
    public double? SpeakerVolume { get; private set; }

    public bool ControlsBrightness => Brightness is not null;

    public bool ControlsSpeakerVolume => SpeakerVolume is not null;

    /// <summary>
    /// Opens the screen and finds out what it can do. The only place that queries over DDC —
    /// from here on the values come from memory.
    /// </summary>
    public static DisplayDevice Open(IntPtr monitor)
    {
        string name = MonitorControl.DescribeMonitor(monitor);
        bool isInternal = MonitorControl.IsInternal(monitor);

        MonitorControl.PhysicalMonitor[] physical = isInternal
            ? Array.Empty<MonitorControl.PhysicalMonitor>()
            : MonitorControl.Open(monitor);

        var device = new DisplayDevice(monitor, name, isInternal, physical);

        device.Brightness = isInternal
            ? InternalBrightness.Get()
            : physical.Select(p => MonitorControl.Brightness(p.Handle)).FirstOrDefault(v => v is not null);

        device.SpeakerVolume = isInternal
            ? null
            : physical.Select(p => MonitorControl.Feature(p.Handle, MonitorControl.VcpSpeakerVolume))
                      .FirstOrDefault(v => v is not null);

        Log.Info($"display \"{name}\": {(isInternal ? "built-in panel" : "external, DDC/CI")}, " +
                 $"brightness {Show(device.Brightness)}, monitor speakers {Show(device.SpeakerVolume)}");

        return device;

        static string Show(double? value) => value is null ? "no" : $"{value.Value:0} %";
    }

    /// <summary>
    /// Sets the brightness. The value in memory changes at once while the command goes to the
    /// queue: the caller is the key hook and cannot wait for the monitor to answer.
    /// </summary>
    public void SetBrightness(double percent)
    {
        if (!ControlsBrightness) return;

        percent = Math.Clamp(percent, 0, 100);
        Brightness = percent;

        if (IsInternal)
        {
            DdcQueue.Run(() => InternalBrightness.Set(percent));
            return;
        }

        foreach (MonitorControl.PhysicalMonitor physical in _physical)
        {
            IntPtr handle = physical.Handle;
            DdcQueue.Run(() => MonitorControl.SetBrightness(handle, percent));
        }
    }

    /// <summary>Sets the monitor speaker volume over DDC.</summary>
    public void SetSpeakerVolume(double percent)
    {
        if (!ControlsSpeakerVolume) return;

        percent = Math.Clamp(percent, 0, 100);
        SpeakerVolume = percent;

        foreach (MonitorControl.PhysicalMonitor physical in _physical)
        {
            IntPtr handle = physical.Handle;
            DdcQueue.Run(() => MonitorControl.SetFeature(handle, MonitorControl.VcpSpeakerVolume, percent));
        }
    }

    public void Dispose() => MonitorControl.Close(_physical);
}
