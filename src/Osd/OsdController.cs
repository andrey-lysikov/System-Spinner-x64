using System;
using System.Windows.Threading;
using SystemSpinnerX64.Configuration;

namespace SystemSpinnerX64.Osd;

/// <summary>
/// Shows the OSD and takes it away again. The countdown restarts on every press, so holding
/// a key keeps the panel up instead of making it blink.
/// </summary>
public sealed class OsdController : IDisposable
{
    private readonly AppConfig _cfg;
    private readonly DispatcherTimer _hide = new();

    private OsdWindow? _window;

    public OsdController(AppConfig cfg)
    {
        _cfg = cfg;
        _hide.Tick += (_, _) =>
        {
            _hide.Stop();
            _window?.HideOsd();
        };
    }

    /// <summary>Shows a value in percent and starts the countdown to hiding.</summary>
    public void Show(double percent, OsdKind kind)
    {
        // The window is built on first use: the app may run all day without anyone touching the
        // volume, and parsing the markup up front would be paid for nothing.
        _window ??= new OsdWindow(_cfg);

        _window.Show(percent, kind, _cfg.Osd.AdjustmentSteps);

        _hide.Stop();
        _hide.Interval = TimeSpan.FromSeconds(Math.Clamp(_cfg.Osd.VisibleSeconds, 0.5, 30));
        _hide.Start();
    }

    /// <summary>Re-reads the theme — called when the system has switched it.</summary>
    public void ApplyTheme() => _window?.ApplyTheme();

    public void Dispose()
    {
        _hide.Stop();
        _window?.Close();
        _window = null;
    }
}
