using SystemSpinnerX64.Spinner;

namespace SystemSpinnerX64.Configuration;

/// <summary>The tray icon outside full-screen apps. Parameter descriptions live in sample.conf.</summary>
public sealed class SpinnerConfig
{
    /// <summary>Frame set from <see cref="SpinnerCatalog"/>. An unknown name falls back to Loader.</summary>
    public string Style { get; set; } = "Loader";

    /// <summary>Colouring; only applies to sets that support it.</summary>
    public SpinnerEffect Effect { get; set; } = SpinnerEffect.Original;

    public bool InvertRotation { get; set; }
}
