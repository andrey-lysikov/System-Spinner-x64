//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace SystemSpinnerX64.Spinner;

// The frame sets baked into the assembly.
public static class SpinnerCatalog
{
    public const string ResourcePrefix = "Spinners/";

    // Set and frame number: "Blue Ball/12.png" gives "Blue Ball" and 12.
    private static readonly Regex FrameName = new(
        @"^(?<style>[^/]+)/(?<index>\d+)\.png$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // Sets a silhouette does not suit: the drawing lives by its own colours.
    private static readonly HashSet<string> NoEffect = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cirrcles", "Color Well", "Dots", "Grey Loader", "Loader", "Pie",
        "Rainbow Pie", "Rotation Color Well"
    };

    // Sets to run at half speed: too few frames, so the cycle is otherwise too short.
    private static readonly HashSet<string> HalfSpeed = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cat", "Pikachu", "Rotation Color Well"
    };

    public static IReadOnlyList<SpinnerStyle> All { get; } = Discover();

    public static SpinnerStyle Fallback { get; } =
        All.FirstOrDefault(s => s.Name.Equals(AppParameters.Spinning.FallbackName, StringComparison.OrdinalIgnoreCase))
        ?? All.FirstOrDefault()
        ?? new SpinnerStyle(AppParameters.Spinning.FallbackName, 0, false, 1);

    public static SpinnerStyle? Find(string name) =>
        All.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    // The set by name; an unknown name is no reason to end up with no icon.
    public static SpinnerStyle Validate(string name) => Find(name) ?? Fallback;

    // Resource name of one frame.
    public static string ResourceName(SpinnerStyle style, int index) =>
        $"{ResourcePrefix}{style.Name}/{index.ToString(CultureInfo.InvariantCulture)}.png";

    private static IReadOnlyList<SpinnerStyle> Discover() =>
        Group(typeof(SpinnerCatalog).Assembly.GetManifestResourceNames());

    // Parses resource names into the list of sets.
    internal static IReadOnlyList<SpinnerStyle> Group(IEnumerable<string> resourceNames)
    {
        var frames = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (string resource in resourceNames)
        {
            if (!resource.StartsWith(ResourcePrefix, StringComparison.Ordinal)) continue;

            Match m = FrameName.Match(resource[ResourcePrefix.Length..]);
            if (!m.Success) continue;

            string style = m.Groups["style"].Value;

            // Not the frame count but the highest number plus one: a set runs from zero upwards,
            // and a gap in the middle must cut the animation short rather than shift it.
            int index = int.Parse(m.Groups["index"].Value, CultureInfo.InvariantCulture);
            frames[style] = Math.Max(frames.TryGetValue(style, out int seen) ? seen : 0, index + 1);
        }

        return frames.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                     .Select(p => new SpinnerStyle(
                         p.Key,
                         p.Value,
                         SupportsEffect: !NoEffect.Contains(p.Key),
                         SpeedCoefficient: HalfSpeed.Contains(p.Key) ? 2 : 1))
                     .ToList();
    }
}
