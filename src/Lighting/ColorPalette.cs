//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SystemSpinnerX64.Lighting;

// The colours the lighting can be given from the menu: a 12 by 12 grid. The top row runs from
// white to black; below it every column is one hue, 30° apart from red round to rose, going from
// pastel through the full colour to dark.
internal static class Palette
{
    public const int Size = 12;

    public static Rgb At(int row, int column)
    {
        if (row == 0) return ColorMath.FromHsv(0, 0, 1 - column / (double)(Size - 1));

        double hue = column * 360.0 / Size;
        double t = (row - 1) / (double)(Size - 2);

        // The upper half gains colour at full value, the lower half keeps it and darkens.
        return t <= 0.5
            ? ColorMath.FromHsv(hue, 0.2 + 1.6 * t, 1)
            : ColorMath.FromHsv(hue, 1, 1 - (t - 0.5) * 1.4);
    }
}

// A control of ours in a menu — the palette, the brightness slider — at the size the control has.
// The palette is the widest thing in its menu, which then frames it evenly, as long as that menu
// shows no check-mark strip to stay empty down its left side. Stretching a control to a menu made
// wide by something else is not done: the menu sizes itself from its items, and an item that grows
// with the menu makes it grow again. The menu's colours reach the control as BackColor and ForeColor.
internal sealed class MenuHost : ToolStripControlHost
{
    public MenuHost(Control control) : base(control)
    {
        AutoSize = false;
        Margin = Padding.Empty;
        Padding = Padding.Empty;
        Size = control.Size;
    }

    // The control refits itself when the scale changes, and the item keeps up with it:
    // a size taken once would clip it, or leave a gap, after a move to another monitor.
    protected override void OnSubscribeControlEvents(Control? control)
    {
        base.OnSubscribeControlEvents(control);
        if (control is not null) control.SizeChanged += FollowControl;
    }

    protected override void OnUnsubscribeControlEvents(Control? control)
    {
        base.OnUnsubscribeControlEvents(control);
        if (control is not null) control.SizeChanged -= FollowControl;
    }

    private void FollowControl(object? sender, EventArgs e)
    {
        if (Control.Size != Size) Size = Control.Size;
    }
}

// The palette as a menu item: a grid of swatches with the current colour framed. Its caption is a
// label of the menu's own, drawn as the menu draws every item. A click picks the colour and closes
// the menu, as picking any other item would.
internal sealed class PaletteControl : Control
{
    private Rgb? _selected;
    private (int Row, int Column)? _hover;

    public event Action<Rgb>? Picked;

    public PaletteControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

        Fit();
    }

    // In device pixels: the menu runs at the monitor's scale. The menu itself frames the palette,
    // so it keeps only a small margin of its own, and only above and below.
    private int Swatch => LogicalToDeviceUnits(14);
    private int Gap => LogicalToDeviceUnits(2);
    private int Inset => LogicalToDeviceUnits(4);
    private int Step => Swatch + Gap;

    private int GridTop => Inset;
    private int GridLeft => 0;
    private int GridSide => Palette.Size * Step - Gap;

    private void Fit() => Size = new Size(GridSide, GridTop + GridSide + Inset);

    // Framed in the grid, when it is one of the palette's colours.
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Rgb? Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            Invalidate();
        }
    }

    // The swatches are in device pixels, and so is the size they add up to.
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Fit();
    }

    private (int Row, int Column)? CellAt(Point p)
    {
        int top = GridTop, left = GridLeft;
        int column = (p.X - left) / Step;
        int row = (p.Y - top) / Step;

        if (p.X < left || p.Y < top || column >= Palette.Size || row >= Palette.Size) return null;

        // The gap between two swatches belongs to neither.
        if ((p.X - left) % Step >= Swatch || (p.Y - top) % Step >= Swatch) return null;

        return (row, column);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var cell = CellAt(e.Location);
        if (cell == _hover) return;

        _hover = cell;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = null;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button != MouseButtons.Left || CellAt(e.Location) is not { } cell) return;

        Rgb color = Palette.At(cell.Row, cell.Column);
        Selected = color;
        Picked?.Invoke(color);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.None;

        float frame = Math.Max(1f, LogicalToDeviceUnits(2) * 0.75f);
        using var accent = new Pen(ForeColor, frame);
        using var hoverPen = new Pen(Color.FromArgb(160, ForeColor), frame);

        int top = GridTop, left = GridLeft;

        for (int row = 0; row < Palette.Size; row++)
        {
            for (int column = 0; column < Palette.Size; column++)
            {
                Rgb c = Palette.At(row, column);
                var box = new Rectangle(left + column * Step, top + row * Step, Swatch, Swatch);

                using (var brush = new SolidBrush(Color.FromArgb(c.R, c.G, c.B)))
                    g.FillRectangle(brush, box);

                bool chosen = _selected == c;
                bool hovered = _hover == (row, column);
                if (!chosen && !hovered) continue;

                // Drawn just outside the swatch, so the colour itself stays whole.
                Rectangle ring = Rectangle.Inflate(box, 1, 1);
                g.DrawRectangle(chosen ? accent : hoverPen, ring);
            }
        }
    }
}
