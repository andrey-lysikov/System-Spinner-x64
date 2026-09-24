//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using SystemSpinnerX64.Diagnostics;

namespace SystemSpinnerX64.Spinner;

// The Sun & Moon spinner: the turning Sun frames by day, reddening toward the horizon, the moon in
// its current phase by night, and a cloud in front of either when the sky is overcast. Everything
// moves over the same frames: the moon's face turns, the cloud sways and the rain falls. It reads
// the same sky as the Aura lighting: the haze starts at DimAbove, where the lamps start to come
// up, and the moon goes red on the nights they do.
internal static class SkyIcon
{
    // What the picture shows, coarse enough that a slow drift does not redraw it every minute.
    // Dusk is how deep the sunrise or sunset haze is, from 0 in full day to DuskSteps on the horizon.
    internal readonly record struct State(bool SunUp, int Dusk, int Phase, bool Blood, SkyWeather Weather);

    // The frame set the sun is taken from. It is not a spinner of its own any more.
    public const string SunFrames = "Sun";
    public const int FrameCount = 24;

    internal const int DuskSteps = 8;
    internal const int PhaseSteps = 16;

    // Drawn four times larger and scaled down: at sixteen pixels the cloud's outline and a thin
    // crescent come out ragged otherwise.
    private const int Supersample = 4;

    public static State Now()
    {
        DateTime utc = DateTime.UtcNow;
        return At(utc, Sky.Elevation(utc), Sky.Config.DimAbove, Sky.Weather);
    }

    internal static State At(DateTime utc, double elevation, double dimAbove, SkyWeather weather)
    {
        // A threshold at or below the horizon would leave no span for the haze to fade over.
        double span = dimAbove > 0 ? dimAbove : 10;
        double dusk = elevation > 0 ? 1.0 - Sun.SmoothStep(elevation / span) : 0.0;

        return new State(
            SunUp: elevation > 0,
            Dusk: (int)Math.Round(dusk * DuskSteps),
            Phase: (int)Math.Round(Moon.Phase(utc) * PhaseSteps) % PhaseSteps,
            Blood: elevation <= 0 && Moon.IsBlood(utc),
            Weather: weather);
    }

    // In colour when tint is null; otherwise one colour, for a silhouette effect.
    public static List<Bitmap> Frames(State state, int size, Color? tint)
    {
        // A plain glyph of the moon on a clear night has nothing that moves.
        bool moves = state.SunUp || state.Weather != SkyWeather.Clear || tint is null;
        int count = moves ? FrameCount : 1;

        var frames = new List<Bitmap>(count);
        for (int index = 0; index < count; index++)
            frames.Add(Render(state, index, size, tint));

        return frames;
    }

    public static Bitmap Render(State state, int frame, int size, Color? tint)
    {
        int large = size * Supersample;

        using Bitmap? sun = state.SunUp ? LoadSun(frame) : null;
        using var canvas = new Bitmap(large, large, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(canvas))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            Draw(g, state, sun, frame / (double)FrameCount, large, tint);
        }

        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            g.DrawImage(canvas, new Rectangle(0, 0, size, size));
        }

        return bitmap;
    }

    private static Bitmap? LoadSun(int frame)
    {
        string resource = SpinnerCatalog.ResourceName(SunFrames, frame % FrameCount);

        try
        {
            using Stream? stream = typeof(SkyIcon).Assembly.GetManifestResourceStream(resource);
            if (stream is not null) return new Bitmap(stream);

            Log.Warn($"Sun & Moon: {resource} is missing from the assembly");
        }
        catch (Exception ex)
        {
            Log.Error($"Sun & Moon: {resource} did not load", ex);
        }

        return null;
    }

    // Under a cloud the sun or the moon moves up and left and shrinks a little; the cloud sits in
    // front, low and right, and higher when it rains to leave room for the drops. Progress runs
    // from 0 to 1 over one loop of frames.
    private static void Draw(Graphics g, State state, Bitmap? sun, double progress, float size, Color? tint)
    {
        bool rainy = state.Weather == SkyWeather.Rainy;
        bool clouded = state.Weather != SkyWeather.Clear;

        RectangleF body = clouded
            ? new RectangleF(0, size * (1 - (rainy ? 0.28f : 0.22f) - 0.78f), size * 0.78f, size * 0.78f)
            : new RectangleF(0, 0, size, size);

        double phase = state.Phase / (double)PhaseSteps;

        if (state.SunUp)
        {
            if (sun is not null) DrawSun(g, sun, body, tint, state.Dusk / (float)DuskSteps);
        }
        else if (tint is { } glyph)
        {
            DrawMoonGlyph(g, body, phase, glyph);
        }
        else
        {
            DrawMoon(g, body, phase, state.Blood, progress);
        }

        if (!clouded) return;

        float width = size * 0.78f;
        float height = width * 0.52f;
        float sway = (float)Math.Sin(2 * Math.PI * progress) * size * 0.02f;
        var cloud = new RectangleF(size - width - size * 0.02f + sway,
                                   size - size * (rainy ? 0.24f : 0.08f) - height, width, height);

        // In colour the drops go first, to start behind the cloud and fall out from under it. In one
        // colour they go last: the cloud clears a margin around itself, and inside it, filled the
        // same colour, they do not show anyway.
        if (tint is { } color)
        {
            DrawCloudGlyph(g, cloud, rainy, color);
            if (rainy) FillDrops(g, cloud, progress, color);
        }
        else
        {
            if (rainy) FillDrops(g, cloud, progress, Rgb(0.45, 0.70, 0.95));
            DrawCloud(g, cloud, rainy);
        }
    }

    private static Color Rgb(double red, double green, double blue, double alpha = 1) =>
        Color.FromArgb((int)Math.Round(alpha * 255), (int)Math.Round(red * 255),
                       (int)Math.Round(green * 255), (int)Math.Round(blue * 255));

    // --- The sun ---

    // A silhouette replaces the colour and keeps the alpha. Low in the sky the sun reddens instead:
    // a red-orange haze mixed into it, up to about half.
    private static void DrawSun(Graphics g, Bitmap sun, RectangleF body, Color? tint, float dusk)
    {
        Rectangle box = Rectangle.Round(body);

        ColorMatrix? matrix = null;
        if (tint is { } c)
        {
            matrix = new ColorMatrix(new[]
            {
                new[] { 0f, 0f, 0f, 0f, 0f },
                new[] { 0f, 0f, 0f, 0f, 0f },
                new[] { 0f, 0f, 0f, 0f, 0f },
                new[] { 0f, 0f, 0f, c.A / 255f, 0f },
                new[] { c.R / 255f, c.G / 255f, c.B / 255f, 0f, 1f }
            });
        }
        else if (dusk > 0)
        {
            float haze = 0.55f * dusk, keep = 1 - haze;
            matrix = new ColorMatrix(new[]
            {
                new[] { keep, 0f, 0f, 0f, 0f },
                new[] { 0f, keep, 0f, 0f, 0f },
                new[] { 0f, 0f, keep, 0f, 0f },
                new[] { 0f, 0f, 0f, 1f, 0f },
                new[] { 0.95f * haze, 0.30f * haze, 0.15f * haze, 0f, 1f }
            });
        }

        if (matrix is null)
        {
            g.DrawImage(sun, box);
            return;
        }

        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(matrix);
        g.DrawImage(sun, box, 0, 0, sun.Width, sun.Height, GraphicsUnit.Pixel, attributes);
    }

    // --- The moon ---

    // The lit part: half the limb on the lit side, closed by a terminator half-ellipse whose width
    // follows the phase. It bulges toward the lit side past the quarter. Waxing is lit on the right.
    private static GraphicsPath? LitPath(RectangleF disc, double phase)
    {
        double illumination = Moon.Illumination(phase);
        if (illumination < 0.005) return null;

        float radius = disc.Width / 2;
        float cx = disc.X + radius, cy = disc.Y + radius;
        float terminator = (float)Math.Abs(Math.Cos(2 * Math.PI * phase)) * radius;
        bool waxing = phase < 0.5;
        float direction = (waxing ? 1 : -1) * (illumination > 0.5 ? -1 : 1);
        float middle = cx + direction * terminator;
        const float kappa = 0.5523f; // Bézier handle length for a quarter ellipse

        var path = new GraphicsPath();
        // From the top round the lit side to the bottom: clockwise on screen when waxing.
        path.AddArc(disc, -90, waxing ? 180 : -180);
        path.AddBezier(cx, disc.Bottom, cx + direction * terminator * kappa, disc.Bottom,
                       middle, cy + radius * kappa, middle, cy);
        path.AddBezier(middle, cy, middle, cy - radius * kappa,
                       cx + direction * terminator * kappa, disc.Top, cx, disc.Top);
        path.CloseFigure();
        return path;
    }

    // The maria, roughly where they sit on the near side: overlapping soft blobs, so the edges come
    // out uneven. Centre and radius in disc radii, up is up.
    private static readonly (float X, float Y, float R)[] Maria =
    {
        (-0.28f, 0.32f, 0.26f), (-0.12f, 0.40f, 0.14f), (0.10f, 0.34f, 0.16f), (0.24f, 0.10f, 0.20f),
        (0.12f, 0.02f, 0.12f), (0.58f, 0.28f, 0.13f), (-0.52f, 0.05f, 0.22f), (-0.45f, -0.22f, 0.18f),
        (-0.30f, 0.00f, 0.14f), (-0.12f, -0.32f, 0.13f), (0.40f, -0.18f, 0.13f), (0.05f, 0.18f, 0.10f),
    };

    // The face turns with the frames while the light stays where it is. In a total lunar eclipse
    // it goes copper red, as the lighting does.
    private static void DrawMoon(Graphics g, RectangleF rect, double phase, bool blood, double turn)
    {
        float radius = rect.Width * 0.46f;
        float cx = rect.X + rect.Width / 2, cy = rect.Y + rect.Height / 2;
        var disc = new RectangleF(cx - radius, cy - radius, radius * 2, radius * 2);

        // Earthshine: the unlit disc stays faintly there, so a thin crescent still has a shape.
        using (var earthshine = new SolidBrush(Rgb(0.30, 0.33, 0.40, 0.55))) g.FillEllipse(earthshine, disc);

        using GraphicsPath? lit = LitPath(disc, phase);
        if (lit is null) return;

        using var face = new GraphicsPath();
        face.AddEllipse(disc);

        GraphicsState outside = g.Save();
        g.SetClip(lit, CombineMode.Intersect);

        // Lit from the upper left.
        using (var light = new PathGradientBrush(face)
               {
                   CenterPoint = new PointF(cx - 0.25f * radius, cy - 0.3f * radius),
                   CenterColor = blood ? Rgb(0.93, 0.55, 0.38) : Rgb(0.97, 0.97, 0.95),
                   SurroundColors = new[] { blood ? Rgb(0.60, 0.22, 0.14) : Rgb(0.74, 0.75, 0.77) }
               })
            g.FillEllipse(light, disc);

        GraphicsState level = g.Save();
        g.TranslateTransform(cx, cy);
        g.RotateTransform((float)(360 * turn));
        g.TranslateTransform(-cx, -cy);

        Color mare = blood ? Rgb(0.42, 0.16, 0.10) : Rgb(0.50, 0.52, 0.56);
        foreach ((float x, float y, float r) in Maria)
        {
            float w = 2 * r * radius, h = 1.7f * r * radius;
            using var blob = new GraphicsPath();
            blob.AddEllipse(cx + x * radius - w / 2, cy - y * radius - h / 2, w, h);

            // Soft at the edge and even inside, as the blur gives them in the macOS version.
            using var brush = new PathGradientBrush(blob)
            {
                CenterColor = Color.FromArgb(140, mare),
                SurroundColors = new[] { Color.FromArgb(0, mare) },
                Blend = new Blend { Positions = new[] { 0f, 0.35f, 1f }, Factors = new[] { 0f, 0.85f, 1f } }
            };
            g.FillPath(brush, blob);
        }

        // Tycho, bright and low in the south.
        float tycho = radius * 0.06f;
        using (var bright = new SolidBrush(Rgb(1, 1, 1, blood ? 0.3 : 0.55)))
            g.FillEllipse(bright, cx - radius * 0.15f - tycho, cy + radius * 0.62f - tycho, tycho * 2, tycho * 2);

        g.Restore(level);

        // Limb darkening: a soft rim.
        Color rim = Rgb(0.20, 0.22, 0.28);
        using (var limb = new PathGradientBrush(face)
               {
                   CenterColor = Color.FromArgb(0, rim),
                   SurroundColors = new[] { Color.FromArgb(90, rim) },
                   Blend = new Blend { Positions = new[] { 0f, 0.25f, 1f }, Factors = new[] { 0f, 1f, 1f } }
               })
            g.FillEllipse(limb, disc);

        g.Restore(outside);
    }

    // In one colour: the outline of the disc and its lit part filled.
    private static void DrawMoonGlyph(Graphics g, RectangleF rect, double phase, Color color)
    {
        float size = rect.Width;
        float radius = size * 0.36f;
        float stroke = Math.Max(1f, size / 14f);
        float cx = rect.X + size / 2, cy = rect.Y + rect.Height / 2;
        var disc = new RectangleF(cx - radius, cy - radius, radius * 2, radius * 2);

        // A thin outline, so a sliver of lit disc still reads thicker than it. At new moon it is
        // all there is.
        using (var pen = new Pen(color, stroke * 0.55f)) g.DrawEllipse(pen, disc);

        using GraphicsPath? lit = LitPath(disc, phase);
        if (lit is null) return;

        using var brush = new SolidBrush(color);
        g.FillPath(brush, lit);
    }

    // --- The cloud ---

    // A flat base with three bumps on top, as one shape. Positions in fractions of the box, up is up.
    private static GraphicsPath CloudPath(RectangleF box)
    {
        var path = new GraphicsPath(FillMode.Winding);

        var bottom = new RectangleF(box.X + 0.04f * box.Width, box.Bottom - 0.5f * box.Height,
                                    0.92f * box.Width, 0.5f * box.Height);
        float d = bottom.Height;
        path.StartFigure();
        path.AddArc(bottom.X, bottom.Y, d, d, 90, 180);
        path.AddArc(bottom.Right - d, bottom.Y, d, d, 270, 180);
        path.CloseFigure();

        foreach ((float x, float y, float r) in new[] { (0.26f, 0.50f, 0.20f), (0.52f, 0.62f, 0.27f), (0.78f, 0.46f, 0.18f) })
        {
            float cx = box.X + x * box.Width, cy = box.Bottom - y * box.Height, rr = r * box.Width;
            path.AddEllipse(cx - rr, cy - rr, rr * 2, rr * 2);
        }

        return path;
    }

    // Three drops a third of a loop apart, each falling from inside the cloud to the bottom edge.
    private static void FillDrops(Graphics g, RectangleF box, double progress, Color color)
    {
        using var path = new GraphicsPath(FillMode.Winding);
        float size = box.Width * 0.09f;

        foreach ((float x, double lag) in new[] { (0.25f, 0.0), (0.52f, 1.0 / 3), (0.78f, 2.0 / 3) })
        {
            float fall = (float)((progress + lag) % 1.0);
            float cx = box.X + x * box.Width;
            float cy = box.Bottom + box.Height * (0.55f * fall - 0.05f);

            path.AddEllipse(cx - size / 2, cy - size / 2, size, size);
            path.AddPolygon(new[]
            {
                new PointF(cx - size / 2, cy - size * 0.1f),
                new PointF(cx, cy - size * 1.1f),
                new PointF(cx + size / 2, cy - size * 0.1f)
            });
        }

        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    private static void DrawCloud(Graphics g, RectangleF box, bool rainy)
    {
        using GraphicsPath shape = CloudPath(box);

        // The outline first and the fill over it: the bumps overlap, and this leaves one clean edge.
        using (var pen = new Pen(rainy ? Rgb(0.30, 0.32, 0.36) : Rgb(0.45, 0.66, 0.86), Math.Max(1f, box.Width * 0.12f))
               { LineJoin = LineJoin.Round })
            g.DrawPath(pen, shape);

        RectangleF bounds = shape.GetBounds();
        bounds.Inflate(1, 1);
        using (var fill = new LinearGradientBrush(bounds,
                   rainy ? Rgb(0.66, 0.69, 0.73) : Rgb(1, 1, 1),
                   rainy ? Rgb(0.52, 0.55, 0.60) : Rgb(0.80, 0.89, 0.97),
                   LinearGradientMode.Vertical))
            g.FillPath(fill, shape);

        using var shine = new SolidBrush(Rgb(1, 1, 1, rainy ? 0.35 : 0.9));
        foreach ((float x, float y, float w, float h) in new[] { (0.40f, 0.72f, 0.18f, 0.12f), (0.14f, 0.40f, 0.14f, 0.12f) })
            g.FillEllipse(shine, box.X + x * box.Width, box.Bottom - (y + h) * box.Height, w * box.Width, h * box.Height);
    }

    // In one colour: an outlined cloud, filled when it rains. What is behind it is cleared first,
    // so its edge does not merge with the sun or the moon.
    private static void DrawCloudGlyph(Graphics g, RectangleF box, bool rainy, Color color)
    {
        using GraphicsPath shape = CloudPath(box);
        float stroke = Math.Max(1f, box.Width * 0.09f);

        using var clear = new SolidBrush(Color.Transparent);
        using (var margin = new Pen(Color.Transparent, stroke * 3) { LineJoin = LineJoin.Round })
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawPath(margin, shape);
            g.FillPath(clear, shape);
            g.CompositingMode = CompositingMode.SourceOver;
        }

        using (var pen = new Pen(color, stroke * 2) { LineJoin = LineJoin.Round }) g.DrawPath(pen, shape);

        if (rainy)
        {
            using var brush = new SolidBrush(color);
            g.FillPath(brush, shape);
        }
        else
        {
            // The inner half of the stroke would trace the overlapping bumps: clear it away.
            g.CompositingMode = CompositingMode.SourceCopy;
            g.FillPath(clear, shape);
            g.CompositingMode = CompositingMode.SourceOver;
        }
    }
}
