using SystemSpinnerX64.Monitoring;
using Xunit;

namespace SystemSpinnerX64.Tests;

/// <summary>
/// Picking the graphics kernel event by the preference list from the config. The list is kept by
/// hand, so it matters that a name matches case-insensitively and a number only exactly.
/// </summary>
public class DxgKrnlTaskTests
{
    private static readonly string[] Tasks =
        { "PresentHistoryDetailed", "PresentHistory", "Present", "Flip" };

    [Fact]
    public void Место_в_списке_и_есть_приоритет()
    {
        Assert.Equal(0, FpsCounter.IndexOfTask(Tasks, "PresentHistoryDetailed", 184));
        Assert.Equal(2, FpsCounter.IndexOfTask(Tasks, "Present", 42));
        Assert.Equal(3, FpsCounter.IndexOfTask(Tasks, "Flip", 168));
    }

    [Fact]
    public void Имя_ищется_без_учёта_регистра() =>
        Assert.Equal(1, FpsCounter.IndexOfTask(Tasks, "presenthistory", 0));

    [Fact]
    public void Пробелы_вокруг_имени_не_мешают()
    {
        // "Present, Flip" leaves a name with a space after the comma — the list is edited by hand.
        Assert.Equal(0, FpsCounter.IndexOfTask(new[] { "  Present  " }, "Present", 42));
    }

    [Fact]
    public void Событие_можно_задать_номером()
    {
        // When the provider manifest does not load the event has no name and appears in the log
        // as EventID(215) — then a number is written into the list.
        Assert.Equal(0, FpsCounter.IndexOfTask(new[] { "215" }, "EventID(215)", 215));
    }

    [Fact]
    public void Номер_сравнивается_точно()
    {
        // "21" must not match event 215, or the counting would follow the wrong event.
        Assert.Equal(-1, FpsCounter.IndexOfTask(new[] { "21" }, "EventID(215)", 215));
    }

    [Fact]
    public void Чужое_событие_не_находится() =>
        Assert.Equal(-1, FpsCounter.IndexOfTask(Tasks, "QueuePacket", 178));

    [Fact]
    public void Пустой_список_ничего_не_находит() =>
        Assert.Equal(-1, FpsCounter.IndexOfTask(System.Array.Empty<string>(), "Present", 42));
}
