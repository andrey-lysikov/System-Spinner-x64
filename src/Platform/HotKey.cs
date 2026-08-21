using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SystemSpinnerX64.Platform;

// What is held with the key. The values are the ones RegisterHotKey expects.
[Flags]
internal enum HotKeyModifiers
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Win = 0x0008,

    // No auto-repeat — holding the key would otherwise slam brightness to the end.
    NoRepeat = 0x4000
}

// A key combination from the config: "Ctrl+Alt+F2".
internal sealed record HotKey(HotKeyModifiers Modifiers, int VirtualKey)
{
    private static readonly Dictionary<string, HotKeyModifiers> Names =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["ctrl"] = HotKeyModifiers.Control,
            ["control"] = HotKeyModifiers.Control,
            ["alt"] = HotKeyModifiers.Alt,
            ["shift"] = HotKeyModifiers.Shift,
            ["win"] = HotKeyModifiers.Win
        };

    // Parses the config entry. An empty string and the word "off" mean there is no combination — a
    // deliberate refusal to drive brightness from the keyboard, not an error.
    public static HotKey? Parse(string? text, out string? problem)
    {
        problem = null;

        if (string.IsNullOrWhiteSpace(text)) return null;
        if (text.Trim().Equals("off", StringComparison.OrdinalIgnoreCase)) return null;

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            problem = $"\"{text}\" is not a key combination";
            return null;
        }

        HotKeyModifiers modifiers = HotKeyModifiers.None;
        string key = parts[^1];

        foreach (string part in parts[..^1])
        {
            if (!Names.TryGetValue(part, out HotKeyModifiers modifier))
            {
                problem = $"\"{part}\" in \"{text}\" is not a modifier — expected Ctrl, Alt, Shift or Win";
                return null;
            }
            modifiers |= modifier;
        }

        int? virtualKey = VirtualKeyOf(key);
        if (virtualKey is null)
        {
            problem = $"\"{key}\" in \"{text}\" is not a key — expected F1…F24, a letter, a digit " +
                      "or one of Up, Down, Left, Right, PageUp, PageDown, Home, End, Insert, Delete";
            return null;
        }

        // Without a modifier the combination would take the key away from every other program.
        if (modifiers == HotKeyModifiers.None)
        {
            problem = $"\"{text}\" has no modifier: a bare key would be taken away from every " +
                      "other program. Add Ctrl, Alt, Shift or Win.";
            return null;
        }

        return new HotKey(modifiers | HotKeyModifiers.NoRepeat, virtualKey.Value);
    }

    private static readonly Dictionary<string, int> NamedKeys =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["up"] = 0x26,
            ["down"] = 0x28,
            ["left"] = 0x25,
            ["right"] = 0x27,
            ["pageup"] = 0x21,
            ["pagedown"] = 0x22,
            ["home"] = 0x24,
            ["end"] = 0x23,
            ["insert"] = 0x2D,
            ["delete"] = 0x2E,
            ["space"] = 0x20
        };

    internal static int? VirtualKeyOf(string key)
    {
        if (NamedKeys.TryGetValue(key, out int named)) return named;

        if (key.Length > 1 && (key[0] is 'F' or 'f') &&
            int.TryParse(key[1..], NumberStyles.None, CultureInfo.InvariantCulture, out int number) &&
            number is >= 1 and <= 24)
        {
            return 0x70 + number - 1; // VK_F1 = 0x70
        }

        if (key.Length == 1)
        {
            char c = char.ToUpperInvariant(key[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') return c;
        }

        return null;
    }

    // The way back to text — this is what the app writes into config.conf.
    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotKeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotKeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotKeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotKeyModifiers.Win)) parts.Add("Win");

        parts.Add(NameOf(VirtualKey));
        return string.Join("+", parts);
    }

    private static string NameOf(int virtualKey)
    {
        if (virtualKey is >= 0x70 and <= 0x87) return "F" + (virtualKey - 0x70 + 1);

        foreach (var pair in NamedKeys)
            if (pair.Value == virtualKey) return char.ToUpperInvariant(pair.Key[0]) + pair.Key[1..];

        return ((char)virtualKey).ToString();
    }
}
