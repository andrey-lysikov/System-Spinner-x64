//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace SystemSpinnerX64.Spinner;

// The Sun & Moon spinner: drawn rather than shipped, as in sunlight-flow — the sun with rays that
// grow as it climbs, or the moon in its current phase.
internal static class SkyIcon
{
    // What the picture shows, coarse enough that a slow drift does not redraw it every minute.
    internal readonly record struct State(bool SunUp, int Fill, int Phase);

    private const int FillSteps = 8;
    private const int PhaseSteps = 16;

    // Drawn four times larger and scaled down, or at 16 px a diagonal ray looks thicker than
    // a straight one.
    private const int Supersample = 4;

    public static State Now()
    {
        DateTime utc = DateTime.UtcNow;
        double elevation = Sky.Elevation(utc);

        // Daylight left: 1 with the sun well up, 0 once it is low enough for full lighting.
        double daylight = 1.0 - Sun.Factor(elevation, Sky.Config);

        return new State(
            SunUp: elevation > 0,
            Fill: (int)Math.Round(Math.Clamp(daylight, 0, 1) * FillSteps),
            Phase: (int)Math.Round(Moon.Phase(utc) * PhaseSteps) % PhaseSteps);
    }

    public static Bitmap Render(State state, int size, Color color)
    {
        int large = size * Supersample;

        using var canvas = new Bitmap(large, large, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(canvas))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            if (state.SunUp) DrawSun(g, large, state.Fill / (double)FillSteps, color);
            else DrawMoon(g, large, state.Phase / (double)PhaseSteps, color);
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

    // Rays keep one thickness and grow in length as the sun climbs, which reads better at sixteen
    // pixels than counting lit ones.
    private static void DrawSun(Graphics g, int size, double fill, Color color)
    {
        float centre = size / 2f;
        float coreRadius = size * 0.22f;
        float rayInner = size * 0.30f;
        float rayLongest = size * 0.48f;
        float stroke = Math.Max(1f, size / 12f);

        float rayOuter = rayInner + (rayLongest - rayInner) * (float)fill;

        const int rays = 8;
        if (rayOuter - rayInner > 0.4f)
        {
            using var pen = new Pen(color, stroke * 0.9f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            for (int i = 0; i < rays; i++)
            {
                double angle = i * 2 * Math.PI / rays - Math.PI / 2;
                g.DrawLine(pen,
                    centre + (float)(rayInner * Math.Cos(angle)), centre + (float)(rayInner * Math.Sin(angle)),
                    centre + (float)(rayOuter * Math.Cos(angle)), centre + (float)(rayOuter * Math.Sin(angle)));
            }
        }

        var core = new RectangleF(centre - coreRadius, centre - coreRadius, coreRadius * 2, coreRadius * 2);
        using (var pen = new Pen(color, stroke)) g.DrawEllipse(pen, core);

        if (fill > 0.01)
        {
            float inner = coreRadius * (float)fill;
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, centre - inner, centre - inner, inner * 2, inner * 2);
        }
    }

    // The lit part is the disc minus a terminator ellipse whose width follows the phase, which is
    // what makes a crescent read as a crescent.
    private static void DrawMoon(Graphics g, int size, double phase, Color color)
    {
        float centre = size / 2f;
        float radius = size * 0.36f;
        float stroke = Math.Max(1f, size / 14f);

        var disc = new RectangleF(centre - radius, centre - radius, radius * 2, radius * 2);

        // Thin outline so a sliver of lit disc still reads as thicker than it. At new moon the
        // outline is all there is.
        using (var pen = new Pen(color, stroke * 0.55f)) g.DrawEllipse(pen, disc);

        double illumination = Moon.Illumination(phase);
        if (illumination < 0.005) return;

        bool waxing = phase < 0.5;
        float terminator = (float)Math.Abs(Math.Cos(2 * Math.PI * phase)) * radius;

        // A one per cent crescent is thinner than a pixel. Holding it to twice the stroke keeps it
        // distinct from the outline it sits on.
        terminator = Math.Min(terminator, radius - stroke * 2f);

        using var half = new GraphicsPath();
        half.AddArc(disc, waxing ? -90 : 90, 180);
        half.CloseFigure();

        using var oval = new GraphicsPath();
        oval.AddEllipse(centre - terminator, centre - radius, terminator * 2, radius * 2);

        // Below half the terminator ellipse bites into the lit half, above it the ellipse adds to
        // it. Regions keep that honest at any phase.
        using var lit = new Region(half);
        if (illumination < 0.5) lit.Exclude(oval);
        else lit.Union(oval);

        using var discRegion = new Region(disc);
        lit.Intersect(discRegion);

        using var brush = new SolidBrush(color);
        g.FillRegion(brush, lit);
    }
}
