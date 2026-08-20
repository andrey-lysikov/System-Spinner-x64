namespace SystemSpinnerX64.Localization;

/// <summary>
/// Language of the interface. The log and the config are always English: they are read when
/// something goes wrong, and one language there is safer.
/// </summary>
public enum Language
{
    /// <summary>Follow the system language, falling back to English.</summary>
    Auto,

    En,
    Ru,
    Ar,
    Zh,
    Fr,
    De,
    It,
    Ja
}
