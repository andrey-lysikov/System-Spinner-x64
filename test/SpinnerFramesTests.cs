//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using SystemSpinnerX64.Spinner;
using Xunit;

namespace SystemSpinnerX64.Tests;

/// <summary>
/// Fitting a set into the tray icon. The sets are exported with transparent margins: those are
/// cut off by one rectangle shared by the whole set, and what is left is reduced by one and the
/// same amount.
/// </summary>
public class SpinnerFramesTests
{
    private static Bitmap Frame(int size, Rectangle drawn)
    {
        var frame = new Bitmap(size, size, PixelFormat.Format32bppArgb);

        using var g = Graphics.FromImage(frame);
        g.FillRectangle(Brushes.Red, drawn);

        return frame;
    }

    [Fact]
    public void Прозрачные_поля_обрезаются()
    {
        using Bitmap frame = Frame(100, new Rectangle(30, 20, 40, 50));

        Assert.Equal(new Rectangle(30, 20, 40, 50), SpinnerFrames.Content(new[] { frame }));
    }

    [Fact]
    public void Набор_обрезается_по_общему_прямоугольнику()
    {
        // Not each frame by its own outline: a figure that shifts as it turns would then be
        // re-centred every frame, and the icon would swim.
        using Bitmap first = Frame(100, new Rectangle(30, 20, 40, 50));
        using Bitmap second = Frame(100, new Rectangle(10, 40, 30, 40));

        Assert.Equal(new Rectangle(10, 20, 60, 60), SpinnerFrames.Content(new[] { first, second }));
    }

    [Fact]
    public void Пустой_кадр_не_обрезается_в_ничто()
    {
        using var empty = new Bitmap(64, 64, PixelFormat.Format32bppArgb);

        Assert.Equal(new Rectangle(0, 0, 64, 64), SpinnerFrames.Content(new[] { empty }));
    }

    [Fact]
    public void Набор_без_кадров_даёт_пустой_прямоугольник()
    {
        Assert.Equal(Rectangle.Empty, SpinnerFrames.Content(new List<Bitmap>()));
    }

    [Fact]
    public void Масштаб_считается_по_общему_прямоугольнику()
    {
        // 60 across and 60 down: the rectangle fills the icon and the proportions hold.
        Assert.Equal(24 / 60.0, SpinnerFrames.Scale(new Rectangle(10, 20, 60, 60), 24), 6);
    }

    [Fact]
    public void Широкий_набор_упирается_в_ширину()
    {
        // The running cat is wider than it is tall — the width is what limits it.
        Assert.Equal(24 / 80.0, SpinnerFrames.Scale(new Rectangle(0, 0, 80, 30), 24), 6);
    }

    [Fact]
    public void Смещение_кадра_входит_в_масштаб()
    {
        // A figure that travels across its frame is shown travelling: the rectangle covers the
        // whole run, so the drawing is reduced enough for the far frame to stay inside the icon.
        using Bitmap first = Frame(100, new Rectangle(0, 0, 30, 30));
        using Bitmap far = Frame(100, new Rectangle(60, 0, 30, 30));

        Assert.Equal(24 / 90.0, SpinnerFrames.Scale(SpinnerFrames.Content(new[] { first, far }), 24), 6);
    }

    [Fact]
    public void Пустой_прямоугольник_не_ломает_масштаб()
    {
        Assert.Equal(1, SpinnerFrames.Scale(Rectangle.Empty, 24));
    }
}
