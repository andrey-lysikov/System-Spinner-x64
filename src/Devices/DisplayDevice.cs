using System;
using System.Linq;
using SystemSpinnerX64.Diagnostics;

namespace SystemSpinnerX64.Devices;

// One attached screen and what can be driven on it.
internal sealed class DisplayDevice : IDisposable
{
    private readonly MonitorControl.PhysicalMonitor[] _physical;

    private DisplayDevice(IntPtr monitor, string name, string gdiName, bool isInternal,
                          bool carriesAudio, MonitorControl.PhysicalMonitor[] physical)
    {
        Monitor = monitor;
        Name = name;
        GdiName = gdiName;
        IsInternal = isInternal;
        CarriesAudio = carriesAudio;
        _physical = physical;
    }

    public IntPtr Monitor { get; }

    // The name as the system gives it: "Dell U2720Q" or "Generic PnP Monitor".
    public string Name { get; }

    // The name the rest of Windows knows the screen by: "\\.\DISPLAY1".
    public string GdiName { get; }

    public bool IsInternal { get; }

    // HDMI and DisplayPort carry sound; DVI and VGA do not, whatever the screen is called.
    public bool CarriesAudio { get; }

    // Decided when the screen is opened: a monitor does not grow a control halfway through the day.
    public bool ControlsBrightness { get; private set; }

    public bool ControlsSpeakerVolume { get; private set; }

    // Written from the DDC thread, read from the key handler: a double lands in one piece where a
    // nullable one would be two writes with a gap in the middle.
    public double Brightness { get; private set; }

    public double SpeakerVolume { get; private set; }

    // Opens the screen and finds out what it can do.
    public static DisplayDevice Open(IntPtr monitor)
    {
        (string name, string gdiName, bool carriesAudio) = MonitorControl.Describe(monitor);
        bool isInternal = MonitorControl.IsInternal(monitor);

        MonitorControl.PhysicalMonitor[] physical = isInternal
            ? Array.Empty<MonitorControl.PhysicalMonitor>()
            : MonitorControl.Open(monitor);

        var device = new DisplayDevice(monitor, name, gdiName, isInternal, carriesAudio, physical);

        double? brightness = isInternal
            ? InternalBrightness.Get()
            : physical.Select(p => MonitorControl.Brightness(p.Handle)).FirstOrDefault(v => v is not null);

        double? volume = isInternal
            ? null
            : physical.Select(p => MonitorControl.Feature(p.Handle, MonitorControl.VcpSpeakerVolume))
                      .FirstOrDefault(v => v is not null);

        device.ControlsBrightness = brightness is not null;
        device.Brightness = brightness ?? 0;

        device.ControlsSpeakerVolume = volume is not null;
        device.SpeakerVolume = volume ?? 0;

        Log.Info($"display \"{name}\": {(isInternal ? "built-in panel" : "external, DDC/CI")}, " +
                 $"brightness {Show(brightness)}, monitor speakers {Show(volume)}" +
                 $"{(carriesAudio ? ", carries audio" : "")}");

        return device;

        static string Show(double? value) => value is null ? "no" : $"{value.Value:0} %";
    }

    // Asks the monitor what it stands at: the numbers held here are only what the app last set,
    // and the buttons on the monitor answer to nobody.
    public void Reread()
    {
        if (IsInternal)
        {
            if (ControlsBrightness)
                DdcQueue.Run(Job("read-brightness"), () => Brightness = InternalBrightness.Get() ?? Brightness);

            return;
        }

        foreach (MonitorControl.PhysicalMonitor screen in _physical)
        {
            IntPtr handle = screen.Handle;

            if (ControlsBrightness)
            {
                DdcQueue.Run(Job("read-brightness", handle),
                             () => Brightness = MonitorControl.Brightness(handle) ?? Brightness);
            }

            if (ControlsSpeakerVolume)
            {
                DdcQueue.Run(Job("read-volume", handle),
                             () => SpeakerVolume = MonitorControl.Feature(handle, MonitorControl.VcpSpeakerVolume)
                                                   ?? SpeakerVolume);
            }
        }
    }

    // What makes two commands the same thing in the queue: this screen, that value.
    private string Job(string what, IntPtr handle = default) => $"{Monitor}:{handle}:{what}";

    // Sets the brightness. The value in memory changes at once while the command goes to the queue:
    // the caller is the key hook and cannot wait for the monitor to answer.
    public void SetBrightness(double percent)
    {
        if (!ControlsBrightness) return;

        percent = Math.Clamp(percent, 0, 100);
        Brightness = percent;

        if (IsInternal)
        {
            DdcQueue.Run(Job("brightness"), () => InternalBrightness.Set(percent));
            return;
        }

        foreach (MonitorControl.PhysicalMonitor physical in _physical)
        {
            IntPtr handle = physical.Handle;
            DdcQueue.Run(Job("brightness", handle), () => MonitorControl.SetBrightness(handle, percent));
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
            DdcQueue.Run(Job("volume", handle),
                         () => MonitorControl.SetFeature(handle, MonitorControl.VcpSpeakerVolume, percent));
        }
    }

    public void Dispose() => MonitorControl.Close(_physical);
}
