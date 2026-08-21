using System.Linq;
using SystemSpinnerX64.Configuration;
using SystemSpinnerX64.ViewModels;
using Xunit;

namespace SystemSpinnerX64.Tests;

/// <summary>
/// Column layout. The requirement is strict: the CPU, GPU and FPS numbers line up exactly, and
/// a column takes its width from the longest value in that column, not across the whole panel.
/// </summary>
public class OverlayLayoutTests
{
    /// <summary>A fake measurement: a digit is 10 wide, a label letter 5. That keeps the test
    /// independent of which fonts are installed.</summary>
    private static OverlayViewModel Laid(int extraFans = 0)
    {
        var vm = new OverlayViewModel(new WarnConfig(), OverlayRow.Default(), extraFans);
        vm.Layout(slots => slots * 10, unit => unit.Length * 5, unitGap: 2, columnGap: 6);
        return vm;
    }

    [Fact]
    public void Ячейки_одной_колонки_совпадают_по_ширине_во_всех_строках()
    {
        OverlayViewModel vm = Laid();
        int columns = vm.Groups.Max(g => g.Metrics.Count);

        for (int column = 0; column < columns; column++)
        {
            var cells = vm.Groups.Where(g => g.Metrics.Count > column)
                                 .Select(g => g.Metrics[column])
                                 .ToList();

            Assert.All(cells, cell => Assert.Equal(cells[0].ValueWidth, cell.ValueWidth));
            Assert.All(cells, cell => Assert.Equal(cells[0].CellWidth, cell.CellWidth));
        }
    }

    [Fact]
    public void Ширина_колонки_берётся_по_самому_длинному_значению_в_ней()
    {
        OverlayViewModel vm = Laid();

        // First column: load (three digits) and FPS (three as well) gives 30.
        // Fourth: the clock, four digits, gives 40.
        double load = vm.Groups[0].Metrics[0].ValueWidth;
        double clock = vm.Groups[0].Metrics[3].ValueWidth;

        Assert.Equal(30, load);
        Assert.Equal(40, clock);
    }

    [Fact]
    public void В_ширину_ячейки_входит_подпись()
    {
        // Otherwise "avg" in the FPS row would shift the next column against "%" in the rows above.
        OverlayViewModel vm = Laid();

        double cell = vm.Groups[0].Metrics[0].CellWidth;
        double value = vm.Groups[0].Metrics[0].ValueWidth;

        // The column label is the wider of "%" and "avg" — three letters of five.
        Assert.Equal(value + 2 + 15 + 6, cell);
    }

    [Fact]
    public void Дополнительные_вентиляторы_добавляют_ячейки_в_строку_процессора()
    {
        OverlayViewModel vm = Laid(extraFans: 2);

        Assert.Equal(9, vm.Groups[0].Metrics.Count);   // seven standard plus two of the user's
        Assert.Equal(6, vm.Groups[1].Metrics.Count);   // the GPU row does not change
    }

    [Fact]
    public void Без_дополнительных_вентиляторов_состав_прежний()
    {
        OverlayViewModel vm = Laid();

        Assert.Equal(7, vm.Groups[0].Metrics.Count);
        Assert.Equal(6, vm.Groups[1].Metrics.Count);
        Assert.Equal(2, vm.Groups[2].Metrics.Count);
    }
}
