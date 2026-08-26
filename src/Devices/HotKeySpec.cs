//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SystemSpinnerX64.Devices;

// The pair of keys that stands in for the brightness keys a keyboard has not got, written the way
// a person writes it: "Ctrl+F1/F2" — dimmer first, brighter second.
internal readonly record struct HotKeySpec(uint Modifiers, int DownKey, int UpKey)
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    // Windows repeats a held hotkey by itself; without this one press would arrive many times.
    public const uint ModNoRepeat = 0x4000;

    private static readonly Dictionary<string, uint> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = ModControl, ["control"] = ModControl,
        ["alt"] = ModAlt,
        ["shift"] = ModShift,
        ["win"] = ModWin, ["windows"] = ModWin
    };

    // Never throws: a wrong line in the config leaves the keys unregistered and says why.
    public static HotKeySpec? Parse(string? text, out string? problem)
    {
        problem = null;

        text = text?.Trim() ?? "";
        if (text.Length == 0 || text.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;

        string[] parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            problem = $"\"{text}\" names no keys";
            return null;
        }

        uint modifiers = 0;

        foreach (string part in parts[..^1])
        {
            if (!Names.TryGetValue(part, out uint modifier))
            {
                problem = $"\"{part}\" is not Ctrl, Alt, Shift or Win";
                return null;
            }

            modifiers |= modifier;
        }

        // The keys themselves: "F1/F2", dimmer before brighter.
        string[] keys = parts[^1].Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (keys.Length != 2)
        {
            problem = $"\"{parts[^1]}\" is not a pair of keys like F1/F2";
            return null;
        }

        int? down = Key(keys[0]), up = Key(keys[1]);
        if (down is null || up is null)
        {
            problem = $"\"{parts[^1]}\" names a key that is not F1 to F24";
            return null;
        }

        if (down == up)
        {
            problem = $"\"{parts[^1]}\" is the same key twice";
            return null;
        }

        // A bare function key would be taken from every other application at once — F1 is help
        // and F2 renames things. Demanding a modifier costs nothing and saves the explanation.
        if (modifiers == 0)
        {
            problem = $"\"{text}\" has no Ctrl, Alt, Shift or Win, and a bare key would be taken " +
                      "from every application";
            return null;
        }

        return new HotKeySpec(modifiers, down.Value, up.Value);
    }

    // VK_F1 is 0x70 and the rest follow it in order, up to F24.
    private static int? Key(string name)
    {
        if (name.Length < 2 || (name[0] != 'F' && name[0] != 'f')) return null;

        return int.TryParse(name[1..], NumberStyles.None, CultureInfo.InvariantCulture, out int number)
               && number is >= 1 and <= 24
            ? 0x70 + number - 1
            : null;
    }

    // How it reads in the log: the same shape it has in the config.
    public string Describe =>
        string.Join("+", Named().Append($"F{DownKey - 0x70 + 1}/F{UpKey - 0x70 + 1}"));

    private IEnumerable<string> Named()
    {
        if ((Modifiers & ModControl) != 0) yield return "Ctrl";
        if ((Modifiers & ModAlt) != 0) yield return "Alt";
        if ((Modifiers & ModShift) != 0) yield return "Shift";
        if ((Modifiers & ModWin) != 0) yield return "Win";
    }
}
