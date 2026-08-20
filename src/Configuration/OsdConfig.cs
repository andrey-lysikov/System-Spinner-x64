namespace SystemSpinnerX64.Configuration;

/// <summary>
/// The custom on-screen display for volume and brightness, and what drives it. Only the two
/// settings the config still carries are read from it — the rest are fixed here: they were tuned
/// once and never needed changing.
/// </summary>
public sealed class OsdConfig
{
    /// <summary>Take over the media keys at all. false switches this whole half off.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Show the custom OSD even when there is nothing to control — a single built-in screen,
    /// say. Otherwise the key is handed back and Windows draws its own panel.
    /// </summary>
    public bool AlwaysUseCustomOsd { get; set; }

    /// <summary>Steps the 0…100 scale is divided into: one key press is one step.</summary>
    public int AdjustmentSteps { get; set; } = 16;

    /// <summary>How long the OSD stays up after the last press, seconds.</summary>
    public double VisibleSeconds { get; set; } = 2.5;

    /// <summary>Distance from the bottom edge of the screen, in WPF units.</summary>
    public double BottomInset { get; set; } = 140;


    /// <summary>
    /// Drive external monitor brightness over DDC/CI. Turn it off when a monitor answers with
    /// a second of delay — that happens behind DisplayPort to HDMI adapters.
    /// </summary>
    public bool ControlExternalBrightness { get; set; } = true;

    /// <summary>
    /// Send volume to the monitor's own speakers over DDC/CI instead of the Windows mixer.
    /// Only makes sense when the sound goes into the monitor over HDMI or DisplayPort.
    /// </summary>
    public bool ControlExternalVolume { get; set; }

    /// <summary>
    /// Key combination for brightness up. Windows has no brightness keys of its own — on laptops
    /// the firmware handles them and they never reach an application — so an ordinary
    /// combination is used. The defaults mirror the Mac layout: F1 down, F2 up.
    /// </summary>
    public string BrightnessUpKey { get; set; } = "Ctrl+Alt+F2";

    public string BrightnessDownKey { get; set; } = "Ctrl+Alt+F1";
}
