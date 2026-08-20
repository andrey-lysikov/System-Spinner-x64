using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace SystemSpinnerX64.Views;

/// <summary>
/// History chart: an area filled from zero to the value. The vertical scale is fixed from zero
/// to a hundred — otherwise a quiet hour would look like peak load, simply because the chart
/// would stretch to its own maximum.
///
/// There can be far more points than pixels across: then they are reduced to columns, taking the
/// largest in each. An average would smooth away exactly what the chart is looked at for.
/// </summary>
public sealed class SparklineControl : FrameworkElement
{
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points), typeof(IReadOnlyList<double>), typeof(SparklineControl),
        new FrameworkPropertyMetadata(Array.Empty<double>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(
        nameof(LineBrush), typeof(Brush), typeof(SparklineControl),
        new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AreaBrushProperty = DependencyProperty.Register(
        nameof(AreaBrush), typeof(Brush), typeof(SparklineControl),
        new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(
        nameof(GridBrush), typeof(Brush), typeof(SparklineControl),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double> Points
    {
        get => (IReadOnlyList<double>)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public Brush LineBrush
    {
        get => (Brush)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public Brush AreaBrush
    {
        get => (Brush)GetValue(AreaBrushProperty);
        set => SetValue(AreaBrushProperty, value);
    }

    public Brush GridBrush
    {
        get => (Brush)GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    /// <summary>
    /// Reduces the history to the given number of columns, taking the largest value in each.
    /// Separated out so it can be tested.
    /// </summary>
    internal static double[] Reduce(IReadOnlyList<double> points, int columns)
    {
        if (columns <= 0) return Array.Empty<double>();
        if (points.Count == 0) return Array.Empty<double>();
        if (points.Count <= columns) return System.Linq.Enumerable.ToArray(points);

        var result = new double[columns];
        for (int i = 0; i < columns; i++)
        {
            int from = (int)((long)i * points.Count / columns);
            int to = (int)((long)(i + 1) * points.Count / columns);
            if (to <= from) to = from + 1;

            double max = double.NegativeInfinity;
            for (int j = from; j < to && j < points.Count; j++)
                if (points[j] > max) max = points[j];

            result[i] = double.IsNegativeInfinity(max) ? 0 : max;
        }

        return result;
    }

    protected override void OnRender(DrawingContext context)
    {
        double width = ActualWidth;
        double height = ActualHeight;
        if (width <= 1 || height <= 1) return;

        // Quarter grid lines: without them forty per cent and sixty look the same on the chart.
        var grid = new Pen(GridBrush, 1);
        grid.Freeze();
        for (int i = 1; i < 4; i++)
        {
            double y = Math.Round(height * i / 4) + 0.5;
            context.DrawLine(grid, new Point(0, y), new Point(width, y));
        }

        double[] values = Reduce(Points, (int)Math.Max(2, Math.Round(width)));
        if (values.Length < 2) return;

        var figure = new PathFigure { StartPoint = new Point(0, height), IsClosed = true, IsFilled = true };
        var line = new PathFigure { StartPoint = new Point(0, Y(values[0], height)) };

        for (int i = 0; i < values.Length; i++)
        {
            double x = width * i / (values.Length - 1);
            var point = new Point(x, Y(values[i], height));

            figure.Segments.Add(new LineSegment(point, isStroked: false));
            if (i > 0) line.Segments.Add(new LineSegment(point, isStroked: true));
        }

        figure.Segments.Add(new LineSegment(new Point(width, height), isStroked: false));

        var area = new PathGeometry(new[] { figure });
        area.Freeze();
        context.DrawGeometry(AreaBrush, null, area);

        var stroke = new PathGeometry(new[] { line });
        stroke.Freeze();

        var pen = new Pen(LineBrush, 1.4);
        pen.Freeze();
        context.DrawGeometry(null, pen, stroke);
    }

    private static double Y(double value, double height) =>
        height - height * Math.Clamp(value, 0, 100) / 100.0;
}
