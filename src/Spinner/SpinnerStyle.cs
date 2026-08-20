namespace SystemSpinnerX64.Spinner;

/// <summary>One set of tray animation frames.</summary>
/// <param name="Name">Set name; also the start of the resource name — "Loader/0.png".</param>
/// <param name="FrameCount">How many frames were found in the assembly resources.</param>
/// <param name="SupportsEffect">
/// Whether the set survives being repainted as a silhouette. For multicoloured sets — "Rainbow
/// Pie" and the like — a silhouette would turn the drawing into a blob, so only the original stays.
/// </param>
/// <param name="SpeedCoefficient">
/// How much slower than the rest to run this set. Short sets of four or five frames — "Cat",
/// "Pikachu" — complete a cycle too fast, and that reads as flicker rather than animation.
/// </param>
public sealed record SpinnerStyle(string Name, int FrameCount, bool SupportsEffect, int SpeedCoefficient);
