using System;
using System.Collections.Generic;
using System.Linq;
using SystemSpinnerX64.Configuration;
using SystemSpinnerX64.Diagnostics;
using SystemSpinnerX64.Platform;

namespace SystemSpinnerX64.Devices;

/// <summary>
/// What to do with a pressed volume or brightness key: by how much, to what, and whether to take
/// it on at all. The same order as the macOS version: the step is counted from the current value
/// rounded to the step grid — otherwise, after someone else moved it (a system slider, the
/// buttons on the monitor), the scale would drift off the grid and show fractional ticks.
/// </summary>
public sealed class DisplayManager : IDisposable
{
    private readonly OsdConfig _cfg;
    private readonly List<DisplayDevice> _displays = new();

    public DisplayManager(OsdConfig cfg)
    {
        _cfg = cfg;
    }

    /// <summary>Names of the attached screens — the tray menu shows these.</summary>
    public IReadOnlyList<string> DisplayNames =>
        _displays.Select(d => d.IsInternal ? d.Name + " (built-in)" : d.Name).ToList();

    /// <summary>Whether there is anything to drive by brightness.</summary>
    public bool HasBrightnessControl => _displays.Any(d => d.ControlsBrightness);

    /// <summary>Polls the attached screens again. Called when they change and on demand.</summary>
    public void Refresh()
    {
        Release();

        foreach (IntPtr monitor in MonitorControl.Handles())
        {
            try
            {
                _displays.Add(DisplayDevice.Open(monitor));
            }
            catch (Exception ex)
            {
                Log.Error("a display was not opened", ex);
            }
        }

        Log.Info($"displays: {_displays.Count} found, brightness control " +
                 $"{(HasBrightnessControl ? "available" : "unavailable")}");
    }

    private double Step => 100.0 / Math.Clamp(_cfg.AdjustmentSteps, 2, 100);

    // Rounding to the step grid is the whole point of the "adjustment steps" setting: without it
    // the first press after someone else moved the value would land on a fractional tick.
    private double Next(double current, bool up)
    {
        double stepped = Math.Round(current / Step) * Step + (up ? Step : -Step);
        return Math.Clamp(stepped, 0, 100);
    }

    /// <summary>
    /// Changes the brightness of every screen that drives it.
    /// </summary>
    /// <param name="shown">
    /// What to show in the OSD: the value of the screen the pointer is on, or of the first one
    /// driven when the pointer is on a screen that has no brightness control.
    /// </param>
    public MediaKeyResult AdjustBrightness(bool up, out double shown)
    {
        shown = 0;

        if (!_cfg.ControlExternalBrightness && !InternalBrightness.IsAvailable)
            return _cfg.AlwaysUseCustomOsd ? MediaKeyResult.Consumed : MediaKeyResult.PassThrough;

        bool applied = false;

        // With several screens attached their brightness need not agree, and the OSD can only
        // show one number: it shows the screen being looked at — the one the pointer is on.
        IntPtr active = Win32.MonitorUnderPointer();
        bool shownIsActive = false;

        foreach (DisplayDevice display in _displays)
        {
            if (!display.ControlsBrightness) continue;
            if (!display.IsInternal && !_cfg.ControlExternalBrightness) continue;

            double value = Next(display.Brightness ?? 0, up);
            display.SetBrightness(value);

            if (!applied || (!shownIsActive && display.Monitor == active))
            {
                shown = value;
                shownIsActive = display.Monitor == active;
            }

            applied = true;
        }

        // Nothing to drive: let Windows handle brightness, unless the custom OSD was demanded
        // in every case.
        if (!applied && !_cfg.AlwaysUseCustomOsd) return MediaKeyResult.PassThrough;

        return MediaKeyResult.Consumed;
    }

    /// <summary>
    /// Changes the volume. By default this is the Windows output device volume — it works with
    /// any output, headphones included. The monitor speakers over DDC are a separate setting:
    /// when the sound goes over HDMI those two controls sit one after another, and moving both
    /// at once means turning it down twice.
    /// </summary>
    public MediaKeyResult AdjustVolume(bool up, out double shown)
    {
        shown = 0;

        if (_cfg.ControlExternalVolume && MonitorSpeakers() is DisplayDevice speakers)
        {
            shown = Next(speakers.SpeakerVolume ?? 0, up);
            speakers.SetSpeakerVolume(shown);
            return MediaKeyResult.Consumed;
        }

        double? current = AudioEndpoint.Volume();
        if (current is null) return _cfg.AlwaysUseCustomOsd ? MediaKeyResult.Consumed : MediaKeyResult.PassThrough;

        shown = Next(current.Value, up);
        return AudioEndpoint.SetVolume(shown) ? MediaKeyResult.Consumed : MediaKeyResult.PassThrough;
    }

    /// <summary>Toggles mute. The OSD then shows zero, or the previous volume.</summary>
    public MediaKeyResult ToggleMute(out double shown)
    {
        shown = 0;

        bool? muted = AudioEndpoint.ToggleMute();
        if (muted is null) return _cfg.AlwaysUseCustomOsd ? MediaKeyResult.Consumed : MediaKeyResult.PassThrough;

        // Muted means zero on the scale: the "sound off" icon without a zero scale would read as
        // "the volume is unchanged but inaudible", and that is exactly what has to be shown.
        shown = muted.Value ? 0 : AudioEndpoint.Volume() ?? 0;
        return MediaKeyResult.Consumed;
    }

    // The monitor the sound goes to: the Windows output device name contains the monitor name
    // when the sound is handed over HDMI or DisplayPort.
    private DisplayDevice? MonitorSpeakers()
    {
        string output = AudioEndpoint.DefaultDeviceName();
        if (output.Length == 0) return null;

        return _displays.FirstOrDefault(d =>
                   d.ControlsSpeakerVolume &&
                   (output.Contains(d.Name, StringComparison.OrdinalIgnoreCase) ||
                    d.Name.Contains(output, StringComparison.OrdinalIgnoreCase)))
               ?? _displays.FirstOrDefault(d => d.ControlsSpeakerVolume);
    }

    private void Release()
    {
        foreach (DisplayDevice display in _displays) display.Dispose();
        _displays.Clear();
    }

    public void Dispose() => Release();
}
