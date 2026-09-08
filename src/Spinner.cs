//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using SystemSpinnerX64.Diagnostics;
using SystemSpinnerX64.Platform;

namespace SystemSpinnerX64.Spinner;

// One set of tray animation frames.
public sealed record SpinnerStyle(string Name, int FrameCount, bool SupportsEffect, int SpeedCoefficient);

// How the animation frames are coloured.
public enum SpinnerEffect
{
    // As drawn — the frame is shown unchanged.
    Original = 1,

    // White silhouette: suits a dark taskbar.
    White = 2,

    // Black silhouette: suits a light one.
    Black = 3,

    // Silhouette matching the taskbar: black on light, white on dark.
    Auto = 4
}

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

    // The part of the sheet drawn on anywhere in the set, as one rectangle for the whole set:
    // cropping each frame to itself would re-centre it, and the drawing would swim as it turned.
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
        // Only the overlap is drawn, in its own place: trimming it would put the frame to swimming.
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

// Spins the tray frames at a speed set by the CPU load: the busier it is, the faster they run.
public sealed class SpinnerAnimator : IDisposable
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background);

    private readonly List<Bitmap> _frames = new();
    private readonly List<Icon> _icons = new();
    private readonly List<IntPtr> _handles = new();

    private SpinnerStyle _style = SpinnerCatalog.Fallback;
    private int _current;
    private double _interval = -1;
    private double _lastLoad;

    // Where the next frame goes. This call is what changes the tray icon.
    public event Action<Icon>? FrameReady;

    // Spin the frames backwards.
    public bool Invert { get; set; }

    public SpinnerAnimator()
    {
        _timer.Tick += (_, _) => Advance();
    }

    // Whether there is anything to show: an empty set means the resources were missing.
    public bool HasFrames => _icons.Count > 0;

    // Whether the frames are running. A set of one never is: it is a picture, not an animation.
    public bool IsSpinning => _timer.IsEnabled;

    // Prepares the frames again. Called when the set, the effect, the theme or the icon size
    // changes — that is, whenever the old pictures stopped being usable.
    public void Load(SpinnerStyle style, SpinnerEffect effect, int iconSize, bool lightTheme)
    {
        _style = style;

        // The new frames are built before the old ones are freed: the tray icon points at an old
        // one until the first new frame is handed over, and would show a destroyed handle.
        var frames = new List<Bitmap>();
        var handles = new List<IntPtr>();
        var icons = new List<Icon>();

        foreach (Bitmap frame in SpinnerFrames.Load(style, effect, iconSize, lightTheme))
        {
            frames.Add(frame);

            // Icon.FromHandle does not take ownership of the handle — it is kept here and freed
            // in ReleaseFrames(), or an hour of work would leak thousands.
            IntPtr handle = frame.GetHicon();
            handles.Add(handle);
            icons.Add(Icon.FromHandle(handle));
        }

        List<Icon> previousIcons = new(_icons);
        List<IntPtr> previousHandles = new(_handles);
        List<Bitmap> previousFrames = new(_frames);

        _icons.Clear();
        _handles.Clear();
        _frames.Clear();

        _icons.AddRange(icons);
        _handles.AddRange(handles);
        _frames.AddRange(frames);

        _current = 0;
        _interval = -1;

        Log.Info($"spinner: \"{style.Name}\", {_icons.Count} frames, {iconSize} px, effect {effect}");

        if (_icons.Count > 0) FrameReady?.Invoke(_icons[0]);

        // The tray is showing a new frame now: the old ones can go.
        Release(previousIcons, previousHandles, previousFrames);

        // The speed is known from the last poll — otherwise the icon would stand still after
        // a set change until the sensors are read again.
        UpdateSpeed(_lastLoad);
    }

    // Matches the speed to the load in percent — whichever of the processor and the card is busier.
    public void UpdateSpeed(double loadPercent)
    {
        _lastLoad = loadPercent;
        if (_icons.Count == 0) return;

        // One frame is a picture, not an animation: the still set is chosen exactly so that the
        // tray holds still, and a timer for it would change nothing a hundred times a second.
        if (_icons.Count == 1)
        {
            _timer.Stop();
            return;
        }

        // The load is divided by the frame count: in a long set one frame is a smaller share of
        // the cycle, and without this long sets would spin visibly faster at the same load.
        double load = Math.Clamp(loadPercent / _icons.Count, 1.0, 100.0);
        double interval = Math.Max(AppParameters.Spinning.MinIntervalSeconds,
                                   0.25 / load * _style.SpeedCoefficient);

        if (_interval > 0 &&
            Math.Abs(interval - _interval) <= _interval * AppParameters.Spinning.SpeedTolerance) return;

        _interval = interval;
        _timer.Interval = TimeSpan.FromSeconds(interval);
        if (!_timer.IsEnabled) _timer.Start();
    }

    // Stops the spin, leaving the current frame in place.
    public void Stop()
    {
        _timer.Stop();
        _interval = -1;
    }

    // Returns the icon to the first frame — that reads as asleep rather than stuck.
    public void Rewind()
    {
        if (_icons.Count == 0) return;
        _current = 0;
        FrameReady?.Invoke(_icons[0]);
    }

    private void Advance()
    {
        if (_icons.Count == 0) return;

        _current += Invert ? -1 : 1;
        if (_current >= _icons.Count) _current = 0;
        else if (_current < 0) _current = _icons.Count - 1;

        FrameReady?.Invoke(_icons[_current]);
    }

    private void ReleaseFrames()
    {
        Release(_icons, _handles, _frames);

        _icons.Clear();
        _handles.Clear();
        _frames.Clear();
    }

    private static void Release(List<Icon> icons, List<IntPtr> handles, List<Bitmap> frames)
    {
        foreach (Icon icon in icons) icon.Dispose();
        foreach (IntPtr handle in handles) Win32.DestroyIcon(handle);
        foreach (Bitmap frame in frames) frame.Dispose();
    }

    public void Dispose()
    {
        _timer.Stop();
        ReleaseFrames();
    }
}
