using System;
using System.Linq;
using SystemSpinnerX64.Diagnostics;

namespace SystemSpinnerX64.Devices;

// One attached screen and what can be driven on it.
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

    // The name as the system gives it: "Dell U2720Q" or "Generic PnP Monitor".
    public string Name { get; }

    public bool IsInternal { get; }

    // Brightness in percent, or null when the screen does not drive it.
    public double? Brightness { get; private set; }

    // Monitor speaker volume over DDC, or null when there are no speakers.
    public double? SpeakerVolume { get; private set; }

    public bool ControlsBrightness => Brightness is not null;

    public bool ControlsSpeakerVolume => SpeakerVolume is not null;

    // Opens the screen and finds out what it can do.
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

    // Sets the brightness. The value in memory changes at once while the command goes to the queue:
    // the caller is the key hook and cannot wait for the monitor to answer.
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

    // Sets the monitor speaker volume over DDC.
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
