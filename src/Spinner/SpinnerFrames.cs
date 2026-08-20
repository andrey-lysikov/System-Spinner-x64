using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using SystemSpinnerX64.Diagnostics;

namespace SystemSpinnerX64.Spinner;

/// <summary>
/// Prepares a set of frames for the tray: pulls the png out of the assembly resources, fits it
/// into the icon square and repaints it as a silhouette when asked.
///
/// Done once per set change rather than per frame: the icon changes up to a hundred times a
/// second, and scaling a picture inside that loop would be its most expensive part.
/// </summary>
internal static class SpinnerFrames
{
    /// <summary>
    /// Frames fitted into a <paramref name="size"/> square. An empty list means the resources
    /// were not found, and the caller shows the plain app icon instead.
    /// </summary>
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
            Rectangle[] drawn = Content(sources);
            double scale = Scale(drawn, size);

            var frames = new List<Bitmap>(sources.Count);

            for (int index = 0; index < sources.Count; index++)
                frames.Add(Fit(sources[index], drawn[index], scale, size,
                               style.SupportsEffect ? effect : SpinnerEffect.Original, lightTheme));

            return frames;
        }
        finally
        {
            foreach (Bitmap source in sources) source.Dispose();
        }
    }

    /// <summary>
    /// The part of each frame that is actually drawn on. The sets come exported with generous
    /// transparent margins, and scaling those along with the drawing is what leaves the tray icon
    /// looking half empty. A frame with nothing on it keeps its full size.
    /// </summary>
    internal static Rectangle[] Content(IReadOnlyList<Bitmap> frames)
    {
        var drawn = new Rectangle[frames.Count];

        for (int index = 0; index < frames.Count; index++)
            drawn[index] = Bounds(frames[index])
                           ?? new Rectangle(0, 0, frames[index].Width, frames[index].Height);

        return drawn;
    }

    /// <summary>
    /// One scale for the whole set: the widest frame and the tallest one together decide it, and
    /// every frame is then reduced by that same amount.
    ///
    /// Shared rather than per frame, because a frame is drawn where its own outline is: a running
    /// cat shifts across its frame, and scaling each one to fill the icon would leave the cat
    /// growing and shrinking as it runs. Shared rather than one rectangle for the set, because
    /// that rectangle would span the whole run and the cat inside it would come out tiny.
    /// </summary>
    internal static double Scale(IReadOnlyList<Rectangle> drawn, int size)
    {
        int width = 0, height = 0;

        foreach (Rectangle rect in drawn)
        {
            width = Math.Max(width, rect.Width);
            height = Math.Max(height, rect.Height);
        }

        if (width <= 0 || height <= 0) return 1;

        // Proportions are kept: the "Cat" frame is wider than it is tall, and a cat stretched to
        // a square looks squashed.
        return Math.Min((double)size / width, (double)size / height);
    }

    /// <summary>The drawn-on part of one frame, or null when it is empty.</summary>
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

    /// <summary>Which silhouette colour matches the effect and the current taskbar theme.</summary>
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

        // Only the drawn-on part is taken, at the scale the whole set shares, centred in the icon.
        Rectangle from = Rectangle.Intersect(content, new Rectangle(0, 0, source.Width, source.Height));
        if (from.Width <= 0 || from.Height <= 0) from = new Rectangle(0, 0, source.Width, source.Height);

        int width = Math.Max(1, (int)Math.Round(from.Width * scale));
        int height = Math.Max(1, (int)Math.Round(from.Height * scale));
        var box = new Rectangle((size - width) / 2, (size - height) / 2, width, height);

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
