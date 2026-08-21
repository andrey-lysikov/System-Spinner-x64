using SystemSpinnerX64.Spinner;

namespace SystemSpinnerX64.Configuration;

// The tray icon outside full-screen apps.
public sealed class SpinnerConfig
{
    // Frame set from SpinnerCatalog.
    public string Style { get; set; } = "Loader";

    // Colouring; only applies to sets that support it.
    public SpinnerEffect Effect { get; set; } = SpinnerEffect.Original;

    public bool InvertRotation { get; set; }
}
