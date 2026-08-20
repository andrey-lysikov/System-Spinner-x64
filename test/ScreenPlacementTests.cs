using System.Windows;
using SystemSpinnerX64.Views;
using Xunit;

namespace SystemSpinnerX64.Tests;

/// <summary>
/// Where the popup windows land. Everything here is in pixels of the one grid the screens share:
/// a second monitor can sit to the left of the main one, and then its coordinates are negative.
/// </summary>
public class ScreenPlacementTests
{
    // A screen at 1920×1080 with the taskbar along its bottom edge, forty pixels of it.
    private static readonly Rect MainBounds = new(0, 0, 1920, 1080);
    private static readonly Rect MainWork = new(0, 0, 1920, 1040);

    private static readonly Size Window = new(230, 600);

    [Fact]
    public void Окно_центрируется_на_значке_и_прижимается_к_панели()
    {
        Point corner = ScreenPlacement.TrayCorner(
            MainBounds, MainWork, anchor: new Point(1700, 1060), Window, gap: 10);

        Assert.Equal(1700 - 115, corner.X);
        Assert.Equal(1040 - 600 - 10, corner.Y);
    }

    [Fact]
    public void У_края_экрана_окно_не_вылезает_за_него()
    {
        // The icon can sit right in the corner: half the window would hang off the screen.
        Point corner = ScreenPlacement.TrayCorner(
            MainBounds, MainWork, anchor: new Point(1915, 1060), Window, gap: 10);

        Assert.Equal(1920 - 230 - 10, corner.X);
    }

    [Fact]
    public void Вертикальная_панель_ставит_окно_сбоку()
    {
        // The taskbar on the left: the window sits against it and is centred on the icon vertically.
        var bounds = new Rect(0, 0, 1920, 1080);
        var work = new Rect(60, 0, 1860, 1080);

        Point corner = ScreenPlacement.TrayCorner(
            bounds, work, anchor: new Point(30, 500), Window, gap: 10);

        Assert.Equal(70, corner.X);
        Assert.Equal(200, corner.Y);
    }

    [Fact]
    public void На_втором_мониторе_слева_координаты_отрицательные()
    {
        // A second screen to the left of the main one: everything on it is below zero, and the
        // window has to stay there rather than jump back to the main screen.
        var bounds = new Rect(-1920, 0, 1920, 1080);
        var work = new Rect(-1920, 0, 1920, 1080); // no taskbar of its own

        Point corner = ScreenPlacement.TrayCorner(
            bounds, work, anchor: new Point(-300, 1070), Window, gap: 10);

        Assert.Equal(-300 - 115, corner.X);
        Assert.Equal(1080 - 600 - 10, corner.Y);
    }

    [Fact]
    public void На_мониторе_со_своим_масштабом_размер_окна_в_пикселях_другой()
    {
        // The window is 230 by 600 in its own units; on a screen at 150 per cent that is 345 by
        // 900 pixels, and it is those the placement counts with.
        var size = new Size(230 * 1.5, 600 * 1.5);

        Point corner = ScreenPlacement.TrayCorner(
            MainBounds, MainWork, anchor: new Point(1700, 1060), size, gap: 10 * 1.5);

        Assert.Equal(1700 - 345 / 2.0, corner.X);
        Assert.Equal(1040 - 900 - 15, corner.Y);
    }

    [Fact]
    public void Окно_с_графиком_становится_слева_от_окна_состояния()
    {
        var anchor = new Rect(1600, 400, 230, 600);

        Point corner = ScreenPlacement.BesideCorner(anchor, MainWork, new Size(440, 430), gap: 8);

        Assert.Equal(1600 - 440 - 8, corner.X);
        Assert.Equal(1000 - 430, corner.Y);
    }

    [Fact]
    public void Слева_не_помещается_становится_справа()
    {
        var anchor = new Rect(20, 400, 230, 600);

        Point corner = ScreenPlacement.BesideCorner(anchor, MainWork, new Size(440, 430), gap: 8);

        Assert.Equal(20 + 230 + 8, corner.X);
    }

    [Fact]
    public void Окно_с_графиком_остаётся_на_своём_мониторе()
    {
        // The status window is on the left-hand screen; the chart window goes beside it and must
        // not spill onto the main one.
        var work = new Rect(-1920, 0, 1920, 1040);
        var anchor = new Rect(-1800, 400, 230, 600);

        Point corner = ScreenPlacement.BesideCorner(anchor, work, new Size(440, 430), gap: 8);

        Assert.Equal(-1800 + 230 + 8, corner.X);
        Assert.True(corner.X + 440 <= work.Right);
    }
}
