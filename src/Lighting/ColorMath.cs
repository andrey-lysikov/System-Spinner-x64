//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Globalization;
using System.Linq;

namespace SystemSpinnerX64.Lighting;

// One LED colour. Not System.Drawing.Color: that one carries a name and an alpha nobody here
// needs, and a frame is a few hundred of them twenty times a second.
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static readonly Rgb Black = new(0, 0, 0);

    public static Rgb FromHex(uint rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    // "#RRGGBB" or a colour name such as Orange — the same spellings the overlay colours take.
    public static bool TryParse(string text, out Rgb color)
    {
        color = Black;
        string value = text.Trim();

        if (value.StartsWith('#') && value.Length == 7 &&
            uint.TryParse(value[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint hex))
        {
            color = FromHex(hex);
            return true;
        }

        System.Drawing.Color named = System.Drawing.Color.FromName(value);
        if (!named.IsKnownColor) return false;

        color = new Rgb(named.R, named.G, named.B);
        return true;
    }

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}

// Brightness rides on the colour itself: the Aura controller has no dimming of its own in the
// direct mode, so a lamp at half brightness is simply a lamp of half the colour.
internal static class ColorMath
{
    // Fallbacks for an unreadable base or accent colour.
    public static readonly Rgb Default = Rgb.FromHex(0x0078FF);
    public static readonly Rgb DefaultAccent = Rgb.FromHex(0xFF3000);

    // A total lunar eclipse takes the palette over entirely.
    public static readonly Rgb BloodRed = Rgb.FromHex(0xFF0000);

    // Fully saturated, full-value colour of the given hue.
    public static Rgb FromHue(double hue)
    {
        double h = (hue % 360 + 360) % 360 / 60;
        double x = 1 - Math.Abs(h % 2 - 1);

        (double r, double g, double b) = (int)h switch
        {
            0 => (1.0, x, 0.0),
            1 => (x, 1.0, 0.0),
            2 => (0.0, 1.0, x),
            3 => (0.0, x, 1.0),
            4 => (x, 0.0, 1.0),
            _ => (1.0, 0.0, x)
        };

        return new Rgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    // Hue in degrees, saturation and value 0..1.
    public static Rgb FromHsv(double hue, double saturation, double value)
    {
        Rgb pure = FromHue(hue);
        saturation = Math.Clamp(saturation, 0, 1);
        value = Math.Clamp(value, 0, 1);

        static byte Channel(byte full, double s, double v) =>
            (byte)Math.Round((255 - (255 - full) * s) * v);

        return new Rgb(Channel(pure.R, saturation, value),
                       Channel(pure.G, saturation, value),
                       Channel(pure.B, saturation, value));
    }

    public static Rgb Mix(Rgb a, Rgb b, double k)
    {
        k = Math.Clamp(k, 0.0, 1.0);
        return new Rgb(
            (byte)(a.R + (b.R - a.R) * k),
            (byte)(a.G + (b.G - a.G) * k),
            (byte)(a.B + (b.B - a.B) * k));
    }

    public static Rgb Scale(Rgb c, double k)
    {
        k = Math.Clamp(k, 0.0, 1.0);
        return new Rgb((byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k));
    }
}
