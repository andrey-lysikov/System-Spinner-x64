//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using SystemSpinnerX64.Configuration;
using SystemSpinnerX64.Diagnostics;
using SystemSpinnerX64.Platform;

namespace SystemSpinnerX64.Devices;

// What to do with a pressed volume or brightness key: by how much, to what, and whether to take it
// on at all.
public sealed class DisplayManager : IDisposable
{
    private readonly OsdConfig _cfg;
    private readonly List<DisplayDevice> _displays = new();

    public DisplayManager(OsdConfig cfg)
    {
        _cfg = cfg;
    }

    // For the tray menu: the name, and whether the screen answers anything at all. One that
    // answers nothing is greyed out.
    public IReadOnlyList<(string Name, bool Controllable)> DisplayNames =>
        _displays.Select(d => (d.IsInternal ? d.Name + " (built-in)" : d.Name,
                               d.ControlsBrightness || d.ControlsSpeakerVolume)).ToList();

    public bool HasBrightnessControl => _displays.Any(d => d.ControlsBrightness);

    // What the custom OSD is for. Without a monitor driven over DDC, Windows does the job itself
    // and shows its own panel — ours would only repeat it — so the keys are left alone.
    private bool DrivesBrightnessOverDdc =>
        _cfg.ControlExternalBrightness && _displays.Any(d => !d.IsInternal && d.ControlsBrightness);

    private bool DrivesVolumeOverDdc => _cfg.ControlExternalVolume && MonitorSpeakers() is not null;

    // The numbers held here are what the app last set; the buttons on the monitor answer to nobody.
    // The reading goes to the DDC queue and lands when it lands.
    public void RereadValues()
    {
        foreach (DisplayDevice display in _displays) display.Reread();
    }

    // Polls the attached screens again.
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

    private double Step => 100.0 / Math.Clamp(_cfg.AdjustmentSteps,
                                              AppParameters.Limits.MinAdjustmentSteps,
                                              AppParameters.Limits.MaxAdjustmentSteps);

    // Rounding to the step grid is the whole point of the "adjustment steps" setting: without it
    // the first press after someone else moved the value would land on a fractional tick.
    private double Next(double current, bool up)
    {
        double stepped = Math.Round(current / Step) * Step + (up ? Step : -Step);
        return Math.Clamp(stepped, 0, 100);
    }

    // Moves the brightness of one screen: the one the pointer is on.
    public MediaKeyResult AdjustBrightness(bool up, out double shown)
    {
        shown = 0;

        if (!MediaKeyRules.Takes(DrivesBrightnessOverDdc, _cfg.AlwaysUseCustomOsd))
            return MediaKeyResult.PassThrough;

        DisplayDevice? screen = BrightnessTarget();

        // HDR is asked about the screen being looked at, not about the one that can be driven: in
        // HDR a monitor may stop answering the brightness command, and then there is no such screen.
        DisplayDevice? looked = InFront();
        bool hdr = looked is not null && HdrControl.IsOn(looked.GdiName);

        MediaKeyResult result = MediaKeyRules.Brightness(DrivesBrightnessOverDdc, _cfg.AlwaysUseCustomOsd,
                                                         screen is not null, hdr);

        // Only with the full record: a line per press, and it is what says which way the decision
        // went when the keys do something other than what was expected.
        Log.Info($"brightness key: ddc={Yes(DrivesBrightnessOverDdc)} always={Yes(_cfg.AlwaysUseCustomOsd)} " +
                 $"target=\"{screen?.GdiName ?? "none"}\" screen=\"{looked?.GdiName ?? "none"}\" " +
                 $"hdr={Yes(hdr)} -> {result}");

        if (result != MediaKeyResult.Consumed || screen is null) return result;

        shown = Next(screen.Brightness, up);
        screen.SetBrightness(shown);

        return result;
    }

    // The screen being looked at, driveable or not: the one the pointer is on.
    private DisplayDevice? InFront()
    {
        IntPtr active = Win32.MonitorUnderPointer();

        return _displays.FirstOrDefault(d => d.Monitor == active) ?? _displays.FirstOrDefault();
    }

    // The screen under the pointer where it can be driven, otherwise the first that can.
    private DisplayDevice? BrightnessTarget()
    {
        IntPtr active = Win32.MonitorUnderPointer();

        return _displays.FirstOrDefault(d => d.Monitor == active && Drivable(d))
               ?? _displays.FirstOrDefault(Drivable);

        bool Drivable(DisplayDevice display) =>
            display.ControlsBrightness && (display.IsInternal || _cfg.ControlExternalBrightness);
    }

    // The sound passes through two attenuators — the Windows mixer and the monitor's own volume —
    // and they are moved together, to the same number. Driving one behind the other would leave a
    // second attenuator nothing on screen shows.
    public MediaKeyResult AdjustVolume(bool up, out double shown)
    {
        shown = 0;

        DisplayDevice? speakers = _cfg.ControlExternalVolume ? MonitorSpeakers() : null;

        if (!MediaKeyRules.Takes(speakers is not null, _cfg.AlwaysUseCustomOsd))
            return MediaKeyResult.PassThrough;

        double? mixer = AudioEndpoint.Volume();

        // The step is taken from the mixer: the finer scale, and the one everything else agrees
        // with. Found apart, the two are brought together by the first press.
        double current = mixer ?? speakers?.SpeakerVolume ?? 0;

        shown = Next(current, up);

        bool moved = mixer is not null && AudioEndpoint.SetVolume(shown);

        if (speakers is not null)
        {
            speakers.SetSpeakerVolume(shown);
            moved = true;
        }

        return MediaKeyRules.Volume(moved, _cfg.AlwaysUseCustomOsd);
    }

    // Toggles mute. The OSD then shows zero, or the previous volume.
    public MediaKeyResult ToggleMute(out double shown)
    {
        shown = 0;

        if (!MediaKeyRules.Takes(DrivesVolumeOverDdc, _cfg.AlwaysUseCustomOsd)) return MediaKeyResult.PassThrough;

        bool? muted = AudioEndpoint.ToggleMute();
        if (muted is null) return MediaKeyRules.Volume(false, _cfg.AlwaysUseCustomOsd);

        // Muted means zero on the scale, or "unchanged but inaudible" is what it would read as.
        shown = muted.Value ? 0 : AudioEndpoint.Volume() ?? 0;
        return MediaKeyResult.Consumed;
    }

    // The monitor the sound goes to. Windows names a display output after the screen it ends at —
    // "XV320QU LV (NVIDIA High Definition Audio)" — so the name before the brackets is matched
    // whole: as a substring, a screen called "PC" would match half the sound devices on a machine.
    // The connection has to carry sound as well; a monitor on DVI cannot receive any.
    private DisplayDevice? MonitorSpeakers()
    {
        string output = AudioEndpoint.DefaultDeviceName();
        if (output.Length == 0) return null;

        string label = ScreenLabel(output);

        return _displays.FirstOrDefault(d => Speaks(d) && d.Name.Equals(label, StringComparison.OrdinalIgnoreCase))
               // Not every driver names its output that way: one name inside the other is still
               // worth trying, now that a silent connection can no longer be what it lands on.
               ?? _displays.FirstOrDefault(d => Speaks(d) &&
                                                (output.Contains(d.Name, StringComparison.OrdinalIgnoreCase) ||
                                                 d.Name.Contains(output, StringComparison.OrdinalIgnoreCase)));

        static bool Speaks(DisplayDevice display) => display.ControlsSpeakerVolume && display.CarriesAudio;
    }

    private static string Yes(bool value) => value ? "1" : "0";

    private static string ScreenLabel(string output)
    {
        int bracket = output.LastIndexOf('(');
        return bracket > 0 ? output[..bracket].Trim() : output.Trim();
    }

    private void Release()
    {
        foreach (DisplayDevice display in _displays) display.Dispose();
        _displays.Clear();
    }

    public void Dispose() => Release();
}
