using System;
using System.Collections.Generic;
using System.Linq;

namespace SystemSpinnerX64.Configuration;

// A value the overlay can show. ExtraFans is not one value but however many names stand in
// ExtraFan under [Hardware].
public enum OverlayMetric
{
    CpuLoad,
    CpuTemp,
    CpuPower,
    CpuClock,
    SysMemory,
    CpuFan,
    AioFan,
    ExtraFans,
    GpuLoad,
    GpuTemp,
    GpuPower,
    GpuClock,
    GpuMemory,
    GpuFan,
    Fps,
    FrameTime
}

// One row of the overlay: its tag and the values along it, in the order they are shown.
public sealed class OverlayRow
{
    public OverlayRow(string title, IEnumerable<OverlayMetric> metrics)
    {
        Title = title;
        Metrics = metrics.ToList();
    }

    public string Title { get; }

    public List<OverlayMetric> Metrics { get; }

    // The rows as they were before any of this could be configured.
    public static List<OverlayRow> Default() => new()
    {
        new OverlayRow("CPU", new[]
        {
            OverlayMetric.CpuLoad, OverlayMetric.CpuTemp, OverlayMetric.CpuPower,
            OverlayMetric.CpuClock, OverlayMetric.SysMemory, OverlayMetric.CpuFan,
            OverlayMetric.AioFan, OverlayMetric.ExtraFans
        }),
        new OverlayRow("GPU", new[]
        {
            OverlayMetric.GpuLoad, OverlayMetric.GpuTemp, OverlayMetric.GpuPower,
            OverlayMetric.GpuClock, OverlayMetric.GpuMemory, OverlayMetric.GpuFan
        }),
        new OverlayRow("FPS", new[] { OverlayMetric.Fps, OverlayMetric.FrameTime })
    };

    // "CPU: CpuLoad, CpuTemp" — the tag before the colon, the values after it. A row without
    // a colon is all values and no tag; an empty one is not shown at all.
    //
    // A mistake does not stop the app over the looks of a panel: the row is dropped and the
    // reason goes into problem, from where the caller writes it to the log.
    public static OverlayRow? Parse(string text, out string? problem)
    {
        problem = null;

        string title = "";
        string list = text;

        int colon = text.IndexOf(':');
        if (colon >= 0)
        {
            title = text[..colon].Trim();
            list = text[(colon + 1)..];
        }

        var metrics = new List<OverlayMetric>();
        foreach (string name in list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse(name, ignoreCase: true, out OverlayMetric metric))
            {
                problem = $"\"{name}\" is not a value the panel knows. Available: {Names}";
                return null;
            }

            metrics.Add(metric);
        }

        // A tag with nothing after it is a mistake too: the row would be a label and no numbers.
        if (metrics.Count == 0 && title.Length > 0)
        {
            problem = $"\"{title}\" has no values after the colon";
            return null;
        }

        return metrics.Count == 0 ? null : new OverlayRow(title, metrics);
    }

    public override string ToString() =>
        Title.Length == 0
            ? string.Join(", ", Metrics)
            : $"{Title}: {string.Join(", ", Metrics)}";

    public static string Names => string.Join(", ", Enum.GetNames<OverlayMetric>());
}
