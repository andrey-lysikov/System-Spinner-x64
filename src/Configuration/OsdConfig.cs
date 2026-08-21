namespace SystemSpinnerX64.Configuration;

// The custom on-screen display for volume and brightness, and what drives it.
public sealed class OsdConfig
{
    // Show the custom OSD even when there is nothing to control — a single built-in screen, say.
    public bool AlwaysUseCustomOsd { get; set; }

    // Steps the 0…100 scale is divided into: one key press is one step.
    public int AdjustmentSteps { get; set; } = 16;

    // Drive external monitor brightness over DDC/CI.
    public bool ControlExternalBrightness { get; set; } = true;

    // Send volume to the monitor's own speakers over DDC/CI instead of the Windows mixer.
    public bool ControlExternalVolume { get; set; } = true;

    // Key combination for brightness up.
    public string BrightnessUpKey { get; set; } = "Ctrl+Alt+F2";

    public string BrightnessDownKey { get; set; } = "Ctrl+Alt+F1";
}
