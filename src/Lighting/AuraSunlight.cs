//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Threading;
using System.Threading.Tasks;
using SystemSpinnerX64.Configuration;
using SystemSpinnerX64.Diagnostics;
using SystemSpinnerX64.Spinner;

namespace SystemSpinnerX64.Lighting;

// The lighting driven by the sun, as sunlight-flow does it: dark by day, coming up as the sun
// sets, red and breathing through a total lunar eclipse. On top of that the colour — never the
// brightness — drifts towards a warning as the CPU or the GPU nears its threshold. Which
// controller carries it out is the driver's business: this only works out one colour a frame.
internal sealed class AuraSunlight : IDisposable
{
    private readonly AuraConfig _cfg;
    private readonly SunLevel _level = new();

    // Lets a toggle cut the idle wait short instead of losing up to a second.
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    // Only the loop touches the device, apart from Dispose once the loop has stopped.
    private ILightDevice? _device;
    private DateTime _reopenAfter = DateTime.MinValue;
    private bool _takenOver;

    // Set when the controller may have forgotten us — a wake from sleep resets it to its own effect.
    private volatile bool _devicesChanged;

    // Set when the colour or the effect changed, so a light standing still is redrawn at once.
    private volatile bool _restyled;

    private AuraLook _look;

    private volatile bool _enabled;
    private volatile bool _toggling;
    private volatile bool _levelKnown;

    // The first look after a switch-on answers at once rather than waiting for the weather.
    private volatile bool _quickLevel;

    // Until then a new goal is reached in the short fade of a slider being dragged. A moment rather
    // than a flag: a drag that changes nothing — by day the goal stays at zero — must not leave the
    // next daily drift snapping over in a fraction of a second.
    private DateTime _adjustUntil = DateTime.MinValue;
    private double _targetLevel;
    private double _displayLevel;

    private double _fadeFrom;
    private double _fadeTo;
    private DateTime _fadeStart;
    private TimeSpan _fadeDuration = AppParameters.Aura.ToggleFade;
    private bool _fadeReported = true;

    private Task? _levelRefresh;
    private DateTime _levelStamp = DateTime.MinValue;

    // Fed from the sensor poll on the UI thread.
    private double _heatTarget;
    private double _load;
    private double _heat;

    private double _effectPhase;

    private bool _blood;
    private double _pulsePhase;
    private DateTime _bloodChecked = DateTime.MinValue;

    public AuraSunlight(AuraConfig cfg, ILightDevice device)
    {
        _cfg = cfg;
        _device = device;
        _look = LookOf(cfg);

        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    public bool IsEnabled => _enabled;

    public void Enable()
    {
        if (_enabled) return;

        // Read again on every switch-on, so an edit to the file needs no restart.
        _look = LookOf(_cfg);
        Sky.EnsureResolved();

        _levelKnown = false;
        _quickLevel = true;
        _levelStamp = DateTime.MinValue;
        _toggling = true;
        _enabled = true;

        Log.Event($"aura: on — {_look}, ceiling {_cfg.Brightness:0} %");
        Wake();
    }

    // Fades down to dark. The controller keeps the black it was last sent until it is taken over
    // again or the machine is switched off, when it shows its own saved effect.
    public void Disable()
    {
        if (!_enabled) return;

        _toggling = true;
        _enabled = false;

        Log.Event("aura: off, fading out");
        Wake();
    }

    // The ceiling moved in the menu. The level is worked out again at once, without waiting for the
    // weather, and reached quickly: the light has to follow the thumb as it is dragged.
    public void Rescale()
    {
        if (!_enabled) return;

        _adjustUntil = DateTime.UtcNow + AppParameters.Aura.AdjustWindow;
        _quickLevel = true;
        _levelStamp = DateTime.MinValue;
        Wake();
    }

    // A colour or an effect picked in the menu: shown with the next frame, while the brightness
    // carries on where it is.
    public void Restyle()
    {
        _look = LookOf(_cfg);

        // Switched off, the controller is left alone: the look is read again at the switch-on.
        if (!_enabled) return;

        _restyled = true;
        Log.Info($"aura: now {_look}");
        Wake();
    }

    // The latest readings: how hot the machine is, 0..1 of the way to a threshold, and how busy.
    public void Feed(double heat, double load)
    {
        Volatile.Write(ref _heatTarget, Math.Clamp(heat, 0, 1));
        Volatile.Write(ref _load, Math.Clamp(load, 0, 1));
    }

    // After sleep the controller is back on its own effect and has to be taken over again.
    public void Recover(string reason)
    {
        if (!_enabled) return;

        Log.Info($"aura: taking the controller over again — {reason}");
        _devicesChanged = true;
        Wake();
    }

    private static AuraLook LookOf(AuraConfig cfg)
    {
        if (!Rgb.TryParse(cfg.Color, out Rgb color))
        {
            color = ColorMath.Default;
            Log.Warn($"[Aura] Color: \"{cfg.Color}\" is not a colour — {color} is used");
        }

        return new AuraLook(cfg.Effect, color, Math.Clamp(cfg.Speed, 1, 10));
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        DateTime lastFrame = DateTime.UtcNow;
        DateTime lastWrite = DateTime.MinValue;
        DateTime logStamp = DateTime.MinValue;
        int written = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                DateTime now = DateTime.UtcNow;
                TimeSpan elapsed = now - lastFrame;
                lastFrame = now;
                double dt = Math.Clamp(elapsed.TotalSeconds, 0, 1.0);

                if (_enabled && now - _levelStamp > AppParameters.Aura.LevelRefresh &&
                    (_levelRefresh?.IsCompleted ?? true))
                {
                    _levelStamp = now;
                    _levelRefresh = Task.Run(() => RefreshLevelAsync(ct), ct);
                }

                bool devicesChanged = _devicesChanged;
                _devicesChanged = false;

                if (devicesChanged) _takenOver = false;
                if (_device is null && (_enabled || _displayLevel > 0)) devicesChanged |= Reopen(now);

                // Until the first level is known the lamps hold where they are, so the ramp that
                // follows is the quick one of a switch-on rather than the slow daily drift.
                double goal = _enabled ? (_levelKnown ? _targetLevel : _displayLevel) : 0.0;
                bool moving = Advance(goal);

                if (now - _bloodChecked > AppParameters.Aura.BloodCheck)
                {
                    _bloodChecked = now;
                    bool blood = _enabled && _level.LastElevation < 0 && Moon.IsBlood(now);
                    if (blood != _blood)
                    {
                        _blood = blood;
                        Log.Event(blood ? "aura: blood moon tonight" : "aura: blood moon over");
                    }
                }

                bool lit = _displayLevel > 0.0005;

                // Dark lamps have nothing to tint, so the heat is let go of with them.
                moving |= AdvanceHeat(lit ? Volatile.Read(ref _heatTarget) : 0.0, dt);

                AuraLook look = _look;
                if (look.IsAnimated && lit)
                {
                    _effectPhase = (_effectPhase + Effects.Rate(look) * dt) % 1.0;
                    moving = true;
                }

                bool pulsing = lit && _blood;
                if (pulsing)
                {
                    // Faster the busier the machine, never above three beats a second.
                    double rate = Math.Clamp(0.5 + 2.5 * Volatile.Read(ref _load), 0.5, 3.0);
                    _pulsePhase = (_pulsePhase + rate * dt) % 1.0;
                    moving = true;
                }

                // Standing at the goal there is nothing to send, so the controller is left alone
                // apart from an occasional restatement, in case something else touched it.
                bool restyled = _restyled;
                _restyled = false;

                bool due = moving || devicesChanged || restyled ||
                           (_enabled && now - lastWrite > AppParameters.Aura.KeepAlive);

                if (due && _device is not null && Render(look, pulsing))
                {
                    lastWrite = now;
                    written++;
                }

                if (now - logStamp > AppParameters.Aura.LogPeriod && (_enabled || moving))
                {
                    logStamp = now;
                    Log.Info($"aura: brightness {_displayLevel:F3} -> {goal:F3}, sun {_level.LastElevation:F1}°, " +
                             $"heat {_heat:F2}, frames {written}");
                    written = 0;
                }

                await _wake.WaitAsync(moving ? AppParameters.Aura.Frame : AppParameters.Aura.IdleTick, ct)
                           .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // One bad frame must not end the lighting for the rest of the session.
                Log.Error("aura: the lighting loop stumbled", ex);
                await Task.Delay(AppParameters.Aura.IdleTick, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    // Draws the frame and sends it. False when the controller went away mid-write.
    private bool Render(AuraLook look, bool pulsing)
    {
        ILightDevice device = _device!;

        double output = _displayLevel;
        if (pulsing)
        {
            // Breathing, not blinking: a third off at most, so the light never drops out.
            double wave = 0.5 - 0.5 * Math.Cos(2 * Math.PI * _pulsePhase);
            output *= 1.0 - AppParameters.Aura.PulseDepth * (1.0 - wave);
        }

        // One colour for the whole light: the effect, then the heat, then the eclipse over both.
        Rgb color = Effects.Render(look, _effectPhase);
        if (_heat > 0) color = ColorMath.Mix(color, look.WarnColor, _heat);
        if (_blood) color = ColorMath.BloodRed;
        color = ColorMath.Scale(color, output);

        try
        {
            if (!_takenOver)
            {
                device.TakeOver();
                _takenOver = true;
            }

            device.Show(color);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"aura: the controller stopped answering — {ex.Message}");
            device.Dispose();
            _device = null;
            _reopenAfter = DateTime.UtcNow + AppParameters.Aura.ReopenDelay;
            return false;
        }
    }

    // Looks for the controller again after it went away: an unplugged cable, a USB reset on wake.
    private bool Reopen(DateTime now)
    {
        if (now < _reopenAfter) return false;
        _reopenAfter = now + AppParameters.Aura.ReopenDelay;

        ILightDevice? device = LightDevices.Open(_cfg.LedsPerChannel);
        if (device is null) return false;

        _device = device;
        _takenOver = false;

        Log.Event($"lighting: controller back — {device.Describe()}");
        return true;
    }

    // Off the loop thread: the IP lookup and the weather can take seconds, and the lamps must not
    // freeze mid-ramp meanwhile.
    private async Task RefreshLevelAsync(CancellationToken ct)
    {
        try
        {
            Sky.EnsureResolved();

            bool quick = _quickLevel;
            _quickLevel = false;
            _targetLevel = await _level.TargetAsync(_cfg, waitForWeather: !quick, ct).ConfigureAwait(false);

            // While the latitude is still a guess, or the cloud cover is still on its way, the next
            // look comes sooner: the answer may well be in by then.
            if (!Sky.Location.ByIp || _level.CloudStale)
                _levelStamp = DateTime.UtcNow - AppParameters.Aura.LevelRefresh + AppParameters.Aura.LocationRetry;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn($"aura: the level was not worked out — {ex.Message}");
        }
        finally
        {
            _levelKnown = true;
            Wake();
        }
    }

    // Exponential drift towards the latest reading: the tint flows between polls instead of
    // stepping once a second.
    private bool AdvanceHeat(double target, double dt)
    {
        double delta = target - _heat;
        if (Math.Abs(delta) < 0.002)
        {
            _heat = target;
            return false;
        }

        _heat += delta * (1.0 - Math.Exp(-dt / AppParameters.Aura.HeatSmoothing.TotalSeconds));
        return true;
    }

    // Fixed-span ramp to the goal, so a toggle takes the same time whatever the level. Returns true
    // while still moving.
    private bool Advance(double goal)
    {
        if (Math.Abs(goal - _fadeTo) > 1e-9)
        {
            _fadeFrom = _displayLevel;
            _fadeTo = goal;
            _fadeStart = DateTime.UtcNow;
            _fadeDuration = _toggling ? AppParameters.Aura.ToggleFade
                          : DateTime.UtcNow < _adjustUntil ? AppParameters.Aura.AdjustFade
                          : AppParameters.Aura.DriftFade;
            _fadeReported = Math.Abs(_fadeTo - _displayLevel) < 0.0005;
            _toggling = false;
        }

        if (_fadeReported && Math.Abs(_displayLevel - _fadeTo) < 0.0005)
        {
            _displayLevel = _fadeTo;
            return false;
        }

        double span = _fadeDuration.TotalSeconds;
        double t = span <= 0 ? 1.0 : (DateTime.UtcNow - _fadeStart).TotalSeconds / span;
        t = Math.Clamp(t, 0.0, 1.0);

        if (t >= 1.0 && !_fadeReported)
        {
            _fadeReported = true;
            Log.Info($"aura: reached {_fadeTo:F3} in {(DateTime.UtcNow - _fadeStart).TotalSeconds:F1} s");
        }

        // Eased in: the change starts barely noticeable and gathers pace towards the end, both
        // when the light comes up and when it goes down.
        double eased = t * t * t;

        _displayLevel = Math.Clamp(_fadeFrom + (_fadeTo - _fadeFrom) * eased, 0.0, 1.0);
        return t < 1.0;
    }

    private void Wake()
    {
        try { if (_wake.CurrentCount == 0) _wake.Release(); }
        catch (SemaphoreFullException) { /* already pending */ }
    }

    // Leaves the lamps dark rather than frozen on whatever the last frame was: a light that stops
    // following the sun without saying so is worse than one that is plainly off.
    public void Dispose()
    {
        _cts.Cancel();
        try { _loop.Wait(TimeSpan.FromSeconds(2)); } catch { /* did not finish in time */ }

        if (_device is { } device)
        {
            try
            {
                if (_takenOver) device.Show(Rgb.Black);
            }
            catch (Exception ex)
            {
                Log.Info($"aura: the lamps were not darkened on exit — {ex.Message}");
            }

            device.Dispose();
        }

        _cts.Dispose();
    }
}
