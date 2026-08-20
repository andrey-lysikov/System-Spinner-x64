using SystemSpinnerX64.Monitoring;
using Xunit;

namespace SystemSpinnerX64.Tests;

/// <summary>
/// The frame window: counting FPS and the leeway for a delayed ETW batch. Three bugs were caught
/// here, which showed the panel either tens of thousands of frames or a flickering dash.
/// </summary>
public class FrameWindowTests
{
    private const double Window = 1.0;
    private const double Stale = 2.0;

    private static FrameWindow Filled(double fps, double seconds, double wall = 100)
    {
        var w = new FrameWindow(Window, Stale);
        double step = 1.0 / fps;

        // The frame stamp and "now" share one scale: the event is parsed as soon as it arrives.
        for (double t = 0; t <= seconds; t += step) w.Add(t, wall + t);
        return w;
    }

    [Fact]
    public void Ровный_поток_даёт_свой_fps()
    {
        FrameWindow w = Filled(fps: 60, seconds: 3);

        double? fps = w.Average(wallNow: 103);

        Assert.NotNull(fps);
        Assert.InRange(fps!.Value, 59, 61);
    }

    [Fact]
    public void Время_кадра_обратно_частоте()
    {
        FrameWindow w = Filled(fps: 50, seconds: 3);

        double? ms = w.FrameTimeMs(wallNow: 103);

        Assert.NotNull(ms);
        Assert.InRange(ms!.Value, 19.5, 20.5);
    }

    [Fact]
    public void Меньше_двух_кадров_это_нет_данных()
    {
        var w = new FrameWindow(Window, Stale);
        Assert.Null(w.Average(0));

        w.Add(1.0, 100);
        Assert.Null(w.Average(100));   // one frame is not enough: there is no interval yet
    }

    [Fact]
    public void Пачка_событий_не_завышает_fps()
    {
        // Exactly the bug that sent the FPS into the tens of thousands: an ETW batch is parsed in
        // microseconds, and had the frames been stamped at parse time the intervals would collapse.
        // The stamps follow the trace clock, so the arrival of a batch does not affect the count.
        var w = new FrameWindow(Window, Stale);
        for (int i = 0; i < 60; i++) w.Add(i / 60.0, wallNow: 100.0 + i * 0.000_001);

        double? fps = w.Average(wallNow: 100.0001);

        Assert.NotNull(fps);
        Assert.InRange(fps!.Value, 55, 65);
    }

    [Fact]
    public void Задержка_пачки_не_роняет_показание_в_прочерк()
    {
        // The frames ran out a second ago — none falls inside the strict window, but the source is
        // still alive: the last known value is shown, or a dash flickers on the panel.
        FrameWindow w = Filled(fps: 60, seconds: 3);

        Assert.NotNull(w.Average(wallNow: 104.5));
    }

    [Fact]
    public void Долгое_молчание_считается_остановкой()
    {
        // The game was minimised or closed: stretching the window further would make the panel
        // show a number that has long been wrong.
        FrameWindow w = Filled(fps: 60, seconds: 3);

        Assert.Null(w.Average(wallNow: 110));
    }

    [Fact]
    public void Метки_вразнобой_не_ломают_порядок()
    {
        // Events from different providers can arrive with stamps out of order, and the list is
        // binary-searched — which needs a non-decreasing sequence.
        var w = new FrameWindow(Window, Stale);
        w.Add(0.10, 100);
        w.Add(0.05, 100);   // a stamp "from the past"
        w.Add(0.20, 100);

        Assert.Equal(3, w.Count);
        Assert.NotNull(w.Average(100));
    }

    [Fact]
    public void Старые_кадры_выбрасываются()
    {
        // Otherwise the list would grow all session: an hour of play is millions of values.
        var w = new FrameWindow(Window, Stale);
        for (int i = 0; i < 6000; i++) w.Add(i / 60.0, 100 + i / 60.0);

        Assert.InRange(w.Count, 2, 400);   // the window plus slack, not all 6000
    }

    [Fact]
    public void Сброс_кадров_сохраняет_связку_часов()
    {
        // What happens on a source change: the collected frames must not mix, but the clock is the same.
        FrameWindow w = Filled(fps: 60, seconds: 3);
        w.ClearFrames();

        Assert.Equal(0, w.Count);
        Assert.Null(w.Average(103));

        w.Add(3.1, 103.1);
        w.Add(3.2, 103.2);
        Assert.NotNull(w.Average(103.2));
    }

    [Fact]
    public void Полный_сброс_забывает_и_часы()
    {
        // What happens on a game change: another process has its own time scale.
        FrameWindow w = Filled(fps: 60, seconds: 3);
        w.Reset();

        Assert.Equal(0, w.Count);
        Assert.Null(w.Average(103));
    }
}
