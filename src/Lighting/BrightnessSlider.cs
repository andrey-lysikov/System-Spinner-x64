//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SystemSpinnerX64.Lighting;

// A slider for the menu, drawn to match the palette above it: a caption with the value on the
// right, and a track with a round thumb below. The system TrackBar would stay light in a dark menu.
// The value moves while the thumb is dragged and is committed once the button is let go, so the
// light can follow the drag while the config is written only once.
internal sealed class BrightnessSlider : Control
{
    public const int Min = 5;
    public const int Max = 100;
    public const int Step = 5;

    private string _title = "";
    private int _value = Max;
    private bool _dragging;

    // While the thumb moves.
    public event Action<int>? ValueChanging;

    // When the thumb is let go, or the wheel has turned.
    public event Action<int>? ValueCommitted;

    public BrightnessSlider()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

        Font = SystemFonts.MenuFont ?? Font;
        FitWidth(LogicalToDeviceUnits(190));
    }

    private int Inset => LogicalToDeviceUnits(4);
    private int Gap => LogicalToDeviceUnits(4);
    private int Thumb => LogicalToDeviceUnits(12);
    private int Track => LogicalToDeviceUnits(4);
    private int LineHeight => TextRenderer.MeasureText("Ag", Font).Height;

    // The thumb's centre runs between these, so it never pokes past either end.
    private int TrackLeft => Thumb / 2;
    private int TrackRight => Width - Thumb / 2;
    private int TrackY => Inset + LineHeight + Gap + Thumb / 2;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Title
    {
        get => _title;
        set
        {
            _title = value ?? "";
            Invalidate();
        }
    }

    // Per cent, on the step.
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _value;
        set
        {
            _value = Snap(value);
            Invalidate();
        }
    }

    internal static int Snap(double value) =>
        Math.Clamp((int)Math.Round(value / Step) * Step, Min, Max);

    // Takes the width it is given — the palette's, so the two line up.
    public void FitWidth(int width) =>
        Size = new Size(width, Inset + LineHeight + Gap + Thumb + Inset);

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        FitWidth(Width);
    }

    private int ValueAt(int x)
    {
        double t = (x - TrackLeft) / (double)Math.Max(1, TrackRight - TrackLeft);
        return Snap(Min + Math.Clamp(t, 0, 1) * (Max - Min));
    }

    private void Slide(int value)
    {
        if (value == _value) return;

        _value = value;
        Invalidate();
        ValueChanging?.Invoke(value);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;

        _dragging = true;
        Slide(ValueAt(e.X));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging) Slide(ValueAt(e.X));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_dragging) return;

        _dragging = false;
        ValueCommitted?.Invoke(_value);
    }

    // The wheel moves one step at a time, and each step counts as let go.
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);

        int before = _value;
        Slide(Snap(_value + Math.Sign(e.Delta) * Step));
        if (_value != before) ValueCommitted?.Invoke(_value);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(BackColor);

        TextRenderer.DrawText(g, _title, Font, new Point(0, Inset), ForeColor, TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, $"{_value} %", Font, new Rectangle(0, Inset, Width, LineHeight), ForeColor,
                              TextFormatFlags.Right | TextFormatFlags.NoPrefix);

        g.SmoothingMode = SmoothingMode.AntiAlias;

        int y = TrackY;
        int x = TrackLeft + (int)Math.Round((_value - Min) / (double)(Max - Min) * (TrackRight - TrackLeft));

        // The whole track faint, the part up to the thumb solid.
        using (var rest = new Pen(Color.FromArgb(70, ForeColor), Track) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLine(rest, TrackLeft, y, TrackRight, y);

        using (var done = new Pen(ForeColor, Track) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLine(done, TrackLeft, y, x, y);

        using var thumb = new SolidBrush(ForeColor);
        g.FillEllipse(thumb, x - Thumb / 2f, y - Thumb / 2f, Thumb, Thumb);
    }
}
