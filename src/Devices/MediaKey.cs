namespace SystemSpinnerX64.Devices;

/// <summary>Which key was pressed. The macOS set minus the keyboard backlight.</summary>
public enum MediaKey
{
    VolumeUp,
    VolumeDown,
    Mute,
    BrightnessUp,
    BrightnessDown
}

/// <summary>
/// What to do with the press. <see cref="PassThrough"/> means there was nothing to control and
/// the key goes back to Windows, which then shows its own panel.
/// </summary>
public enum MediaKeyResult
{
    PassThrough,
    Consumed
}
