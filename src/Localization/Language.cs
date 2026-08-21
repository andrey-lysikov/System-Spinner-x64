namespace SystemSpinnerX64.Localization;

// Language of the interface. The log and the config are always English: they are read when
// something goes wrong, and one language there is safer.
public enum Language
{
    // Follow the system language, falling back to English.
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
