//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Windows;
using System.Windows.Media;

namespace SystemSpinnerX64.Views;

// A scale of separate segments — the one the macOS version dropped the system indicator for:
// drawing it by hand took the GPU load there from seventy per cent to eight.
public sealed class SegmentedLevelControl : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(SegmentedLevelControl),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.None, OnValueChanged));

    public static readonly DependencyProperty SegmentCountProperty = DependencyProperty.Register(
        nameof(SegmentCount), typeof(int), typeof(SegmentedLevelControl),
        new FrameworkPropertyMetadata(20, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CriticalLevelProperty = DependencyProperty.Register(
        nameof(CriticalLevel), typeof(double), typeof(SegmentedLevelControl),
        new FrameworkPropertyMetadata(90.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillBrushProperty = DependencyProperty.Register(
        nameof(FillBrush), typeof(Brush), typeof(SegmentedLevelControl),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EmptyBrushProperty = DependencyProperty.Register(
        nameof(EmptyBrush), typeof(Brush), typeof(SegmentedLevelControl),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CriticalBrushProperty = DependencyProperty.Register(
        nameof(CriticalBrush), typeof(Brush), typeof(SegmentedLevelControl),
        new FrameworkPropertyMetadata(Brushes.OrangeRed, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public int SegmentCount
    {
        get => (int)GetValue(SegmentCountProperty);
        set => SetValue(SegmentCountProperty, value);
    }

    // The value the scale turns red at.
    public double CriticalLevel
    {
        get => (double)GetValue(CriticalLevelProperty);
        set => SetValue(CriticalLevelProperty, value);
    }

    public Brush FillBrush
    {
        get => (Brush)GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    public Brush EmptyBrush
    {
        get => (Brush)GetValue(EmptyBrushProperty);
        set => SetValue(EmptyBrushProperty, value);
    }

    public Brush CriticalBrush
    {
        get => (Brush)GetValue(CriticalBrushProperty);
        set => SetValue(CriticalBrushProperty, value);
    }

    private int _drawnSegments = -1;
    private bool _drawnCritical;

    private static void OnValueChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var control = (SegmentedLevelControl)sender;

        int filled = control.FilledSegments;
        bool critical = control.IsCritical;

        if (filled == control._drawnSegments && critical == control._drawnCritical) return;

        control._drawnSegments = filled;
        control._drawnCritical = critical;
        control.InvalidateVisual();
    }

    private bool IsCritical => CriticalLevel > 0 && Value >= CriticalLevel;

    private int FilledSegments =>
        (int)Math.Round(Math.Clamp(Value, 0, 100) / 100.0 * SegmentCount);

    protected override void OnRender(DrawingContext context)
    {
        int segments = SegmentCount;
        if (segments <= 0 || ActualWidth <= 0 || ActualHeight <= 0) return;

        const double spacing = 1;
        double width = (ActualWidth - (segments - 1) * spacing) / segments;
        if (width <= 0) return;

        double radius = Math.Min(2, Math.Min(width / 2, ActualHeight / 2));
        int filled = FilledSegments;
        Brush active = IsCritical ? CriticalBrush : FillBrush;

        for (int i = 0; i < segments; i++)
        {
            var box = new Rect(i * (width + spacing), 0, width, ActualHeight);
            context.DrawRoundedRectangle(i < filled ? active : EmptyBrush, null, box, radius, radius);
        }
    }
}
