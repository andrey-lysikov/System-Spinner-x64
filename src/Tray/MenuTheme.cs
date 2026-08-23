//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System.Drawing;
using System.Windows.Forms;

namespace SystemSpinnerX64.Tray;

// Colours of the tray menu, matched to the Windows theme.
internal sealed class MenuColors : ProfessionalColorTable
{
    private readonly bool _dark;

    public MenuColors(bool dark)
    {
        _dark = dark;

        // A flat menu, no gradients: that is how Windows 11 draws it, and an Office 2007 gradient
        // would give away a foreign program at a glance.
        UseSystemColors = false;
    }

    private Color Surface => _dark ? Color.FromArgb(0x2B, 0x2B, 0x2B) : Color.FromArgb(0xF9, 0xF9, 0xF9);
    private Color Hover => _dark ? Color.FromArgb(0x3D, 0x3D, 0x3D) : Color.FromArgb(0xE9, 0xE9, 0xE9);
    private Color Edge => _dark ? Color.FromArgb(0x45, 0x45, 0x45) : Color.FromArgb(0xE0, 0xE0, 0xE0);

    public override Color ToolStripDropDownBackground => Surface;

    // The strip on the left where the check marks and the animation previews sit.
    public override Color ImageMarginGradientBegin => Surface;
    public override Color ImageMarginGradientMiddle => Surface;
    public override Color ImageMarginGradientEnd => Surface;

    public override Color MenuItemSelected => Hover;
    public override Color MenuItemSelectedGradientBegin => Hover;
    public override Color MenuItemSelectedGradientEnd => Hover;
    public override Color MenuItemPressedGradientBegin => Surface;
    public override Color MenuItemPressedGradientMiddle => Surface;
    public override Color MenuItemPressedGradientEnd => Surface;

    public override Color MenuItemBorder => Hover;
    public override Color MenuBorder => Edge;

    public override Color SeparatorDark => Edge;
    public override Color SeparatorLight => Edge;

    // Backdrop of a check mark. No reason to differ from hover: the mark is visible anyway.
    public override Color CheckBackground => Hover;
    public override Color CheckSelectedBackground => Hover;
    public override Color CheckPressedBackground => Hover;
}

// The menu renderer. It differs from the standard one in two things: the text colour and the check
// mark colour — the colour table sets neither, they are taken from the item itself, and WinForms
// puts the system black there whatever the theme.
internal sealed class MenuRenderer : ToolStripProfessionalRenderer
{
    private readonly bool _dark;

    public MenuRenderer(bool dark) : base(new MenuColors(dark))
    {
        _dark = dark;
    }

    public Color Foreground => _dark ? Color.FromArgb(0xF0, 0xF0, 0xF0) : Color.FromArgb(0x1A, 0x1A, 0x1A);

    // A disabled item: the same colour at half strength.
    public Color Disabled => _dark ? Color.FromArgb(0x88, 0x88, 0x88) : Color.FromArgb(0x8A, 0x8A, 0x8A);

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Foreground : Disabled;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        // WinForms draws the check mark with a system bitmap — a black one. It is invisible on
        // a dark background, so ours is drawn instead: two lines at an angle, as Windows does.
        if (!_dark)
        {
            base.OnRenderItemCheck(e);
            return;
        }

        Rectangle box = e.ImageRectangle;
        using var pen = new Pen(Foreground, 1.6f);

        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.DrawLines(pen, new[]
        {
            new PointF(box.Left + box.Width * 0.28f, box.Top + box.Height * 0.52f),
            new PointF(box.Left + box.Width * 0.44f, box.Top + box.Height * 0.70f),
            new PointF(box.Left + box.Width * 0.74f, box.Top + box.Height * 0.32f)
        });
    }
}
