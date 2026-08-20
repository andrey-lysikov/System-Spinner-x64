using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using SystemSpinnerX64.Spinner;
using Xunit;

namespace SystemSpinnerX64.Tests;

/// <summary>
/// Fitting a set into the tray icon. The sets are exported with transparent margins: those are
/// cut off, and what is left is reduced by one and the same amount for the whole set.
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

        Assert.Equal(new[] { new Rectangle(30, 20, 40, 50) }, SpinnerFrames.Content(new[] { frame }));
    }

    [Fact]
    public void Каждый_кадр_обрезается_по_своему_рисунку()
    {
        using Bitmap first = Frame(100, new Rectangle(30, 20, 40, 50));
        using Bitmap second = Frame(100, new Rectangle(10, 40, 30, 40));

        Assert.Equal(
            new[] { new Rectangle(30, 20, 40, 50), new Rectangle(10, 40, 30, 40) },
            SpinnerFrames.Content(new[] { first, second }));
    }

    [Fact]
    public void Пустой_кадр_не_обрезается_в_ничто()
    {
        using var empty = new Bitmap(64, 64, PixelFormat.Format32bppArgb);

        Assert.Equal(new[] { new Rectangle(0, 0, 64, 64) }, SpinnerFrames.Content(new[] { empty }));
    }

    [Fact]
    public void Набор_без_кадров_даёт_пустой_список()
    {
        Assert.Empty(SpinnerFrames.Content(new List<Bitmap>()));
    }

    [Fact]
    public void Масштаб_один_на_набор_и_считается_по_наибольшему_кадру()
    {
        // The tallest frame is 50 and the widest 40: the tallest decides, and it fills the icon.
        var drawn = new[] { new Rectangle(30, 20, 40, 50), new Rectangle(10, 40, 30, 40) };

        Assert.Equal(24 / 50.0, SpinnerFrames.Scale(drawn, 24), 6);
    }

    [Fact]
    public void Широкий_кадр_упирается_в_ширину()
    {
        // The running cat is wider than it is tall — the width is what limits it.
        var drawn = new[] { new Rectangle(0, 0, 80, 30) };

        Assert.Equal(24 / 80.0, SpinnerFrames.Scale(drawn, 24), 6);
    }

    [Fact]
    public void Смещение_кадра_не_меняет_масштаб()
    {
        // A figure that moves across its frame stays the same size: only its own outline counts,
        // not how far it has travelled.
        var still = new[] { new Rectangle(0, 0, 30, 30), new Rectangle(0, 0, 30, 30) };
        var moving = new[] { new Rectangle(0, 0, 30, 30), new Rectangle(60, 0, 30, 30) };

        Assert.Equal(SpinnerFrames.Scale(still, 24), SpinnerFrames.Scale(moving, 24), 6);
    }

    [Fact]
    public void Пустой_набор_прямоугольников_не_ломает_масштаб()
    {
        Assert.Equal(1, SpinnerFrames.Scale(new List<Rectangle>(), 24));
    }
}
