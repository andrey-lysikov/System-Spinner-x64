namespace SystemSpinnerX64.Spinner;

/// <summary>
/// How the animation frames are coloured. Not every set supports it: where the drawing lives by
/// its own colours, the flag is cleared in <see cref="SpinnerCatalog"/>.
/// </summary>
public enum SpinnerEffect
{
    /// <summary>As drawn — the frame is shown unchanged.</summary>
    Original = 1,

    /// <summary>White silhouette: suits a dark taskbar.</summary>
    White = 2,

    /// <summary>Black silhouette: suits a light one.</summary>
    Black = 3,

    /// <summary>
    /// Silhouette matching the taskbar: black on light, white on dark. The theme is read from
    /// the registry whenever the frames are reloaded, and the app watches for it changing.
    /// </summary>
    Auto = 4
}
