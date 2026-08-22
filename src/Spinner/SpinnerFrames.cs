using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using SystemSpinnerX64.Diagnostics;

namespace SystemSpinnerX64.Spinner;

// Prepares a set of frames for the tray: pulls the png out of the assembly resources, fits it into
// the icon square and repaints it as a silhouette when asked.
internal static class SpinnerFrames
{
    // Frames fitted into a size square.
    public static List<Bitmap> Load(SpinnerStyle style, SpinnerEffect effect, int size, bool lightTheme)
    {
        var sources = new List<Bitmap>(style.FrameCount);

        for (int index = 0; index < style.FrameCount; index++)
        {
            string resource = SpinnerCatalog.ResourceName(style, index);

            try
            {
                using Stream? stream = typeof(SpinnerFrames).Assembly.GetManifestResourceStream(resource);
                if (stream is null)
                {
                    Log.Warn($"spinner \"{style.Name}\": frame {index} is missing from the assembly");
                    break;
                }

                sources.Add(new Bitmap(stream));
            }
            catch (Exception ex)
            {
                Log.Error($"spinner \"{style.Name}\": frame {index} did not load", ex);
                break;
            }
        }

        try
        {
            Rectangle content = Content(sources);
            double scale = Scale(content, size);

            var frames = new List<Bitmap>(sources.Count);

            foreach (Bitmap source in sources)
                frames.Add(Fit(source, content, scale, size,
                               style.SupportsEffect ? effect : SpinnerEffect.Original, lightTheme));

            return frames;
        }
        finally
        {
            foreach (Bitmap source in sources) source.Dispose();
        }
    }

    // The part of the sheet that is drawn on anywhere in the set: every frame's own outline taken
    // together. One rectangle for the whole set, and deliberately so — cropping each frame to
    // itself would re-centre it on its own outline, and a drawing that shifts inside its frame
    // as it turns would then swim about the icon instead of spinning on the spot.
    internal static Rectangle Content(IReadOnlyList<Bitmap> frames)
    {
        Rectangle content = Rectangle.Empty;

        foreach (Bitmap frame in frames)
        {
            Rectangle drawn = Bounds(frame) ?? new Rectangle(0, 0, frame.Width, frame.Height);
            content = content.IsEmpty ? drawn : Rectangle.Union(content, drawn);
        }

        return content;
    }

    // By how much that rectangle is reduced to land in the icon square.
    internal static double Scale(Rectangle content, int size)
    {
        if (content.Width <= 0 || content.Height <= 0) return 1;

        // Proportions are kept: the "Cat" frame is wider than it is tall, and a cat stretched to
        // a square looks squashed.
        return Math.Min((double)size / content.Width, (double)size / content.Height);
    }

    // The drawn-on part of one frame, or null when it is empty.
    private static Rectangle? Bounds(Bitmap frame)
    {
        int left = frame.Width, top = frame.Height, right = 0, bottom = 0;

        BitmapData data = frame.LockBits(new Rectangle(0, 0, frame.Width, frame.Height),
                                         ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            // One row at a time: a frame can be half a megapixel, and there is no reason to hold
            // a copy of the whole thing to look at its alpha.
            var row = new byte[data.Stride];

            for (int y = 0; y < frame.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(
                    data.Scan0 + y * data.Stride, row, 0, data.Stride);

                for (int x = 0; x < frame.Width; x++)
                {
                    // Bgra in memory, so the alpha is the fourth byte of each pixel.
                    if (row[x * 4 + 3] < AppParameters.Spinning.OpaqueEnough) continue;

                    if (x < left) left = x;
                    if (x >= right) right = x + 1;
                    if (y < top) top = y;
                    bottom = y + 1;
                }
            }
        }
        finally
        {
            frame.UnlockBits(data);
        }

        return right > left && bottom > top ? Rectangle.FromLTRB(left, top, right, bottom) : null;
    }

    // Which silhouette colour matches the effect and the current taskbar theme.
    private static Color? Silhouette(SpinnerEffect effect, bool lightTheme) => effect switch
    {
        SpinnerEffect.White => Color.White,
        SpinnerEffect.Black => Color.Black,
        // The icon sits on the taskbar, not on the desktop: a light taskbar shows a dark one.
        SpinnerEffect.Auto => lightTheme ? Color.Black : Color.White,
        _ => null
    };

    private static Bitmap Fit(Bitmap source, Rectangle content, double scale, int size,
                              SpinnerEffect effect, bool lightTheme)
    {
        var target = new Bitmap(size, size, PixelFormat.Format32bppArgb);

        // The set's rectangle, reduced and centred in the icon: the same box for every frame, so
        // each drawing keeps the place it holds on the sheet.
        int width = Math.Max(1, (int)Math.Round(content.Width * scale));
        int height = Math.Max(1, (int)Math.Round(content.Height * scale));
        int left = (size - width) / 2, top = (size - height) / 2;

        // The frames of a set need not share a canvas, so the rectangle can reach past this one.
        // Then only the overlap is drawn, at the place inside the box it belongs to — trimming the
        // rectangle and centring what is left would put the frame back to swimming.
        Rectangle from = Rectangle.Intersect(content, new Rectangle(0, 0, source.Width, source.Height));
        if (from.Width <= 0 || from.Height <= 0) return target;

        var box = new Rectangle(
            left + (int)Math.Round((from.X - content.X) * scale),
            top + (int)Math.Round((from.Y - content.Y) * scale),
            Math.Max(1, (int)Math.Round(from.Width * scale)),
            Math.Max(1, (int)Math.Round(from.Height * scale)));

        using var g = Graphics.FromImage(target);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;

        Color? tint = Silhouette(effect, lightTheme);
        if (tint is null)
        {
            g.DrawImage(source, box, from.X, from.Y, from.Width, from.Height, GraphicsUnit.Pixel);
            return target;
        }

        // The colour is replaced outright while the alpha stays: that turns the drawing into
        // a silhouette rather than a filled rectangle.
        var matrix = new ColorMatrix(new[]
        {
            new[] { 0f, 0f, 0f, 0f, 0f },
            new[] { 0f, 0f, 0f, 0f, 0f },
            new[] { 0f, 0f, 0f, 0f, 0f },
            new[] { 0f, 0f, 0f, AppParameters.Spinning.SilhouetteAlpha, 0f },
            new[] { tint.Value.R / 255f, tint.Value.G / 255f, tint.Value.B / 255f, 0f, 1f }
        });

        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(matrix);
        g.DrawImage(source, box, from.X, from.Y, from.Width, from.Height, GraphicsUnit.Pixel, attributes);

        return target;
    }
}
