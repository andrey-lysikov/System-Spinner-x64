using System;
using System.Linq;
using SystemSpinnerX64.Configuration;
using SystemSpinnerX64.ViewModels;
using Xunit;

namespace SystemSpinnerX64.Tests;

// The rows of the overlay as the config states them: what stands where, and in which order.
public class OverlayRowTests
{
    [Fact]
    public void Строка_читается_с_тегом_и_значениями()
    {
        OverlayRow? row = OverlayRow.Parse("CPU: CpuLoad, CpuTemp, CpuFan", out _);

        Assert.NotNull(row);
        Assert.Equal("CPU", row!.Title);
        Assert.Equal(new[] { OverlayMetric.CpuLoad, OverlayMetric.CpuTemp, OverlayMetric.CpuFan },
                     row.Metrics);
    }

    [Fact]
    public void Порядок_значений_сохраняется()
    {
        OverlayRow? row = OverlayRow.Parse("CPU: CpuFan, CpuLoad, CpuTemp", out _);

        Assert.Equal(new[] { OverlayMetric.CpuFan, OverlayMetric.CpuLoad, OverlayMetric.CpuTemp },
                     row!.Metrics);
    }

    [Fact]
    public void Тег_не_обязателен()
    {
        OverlayRow? row = OverlayRow.Parse("Fps, FrameTime", out _);

        Assert.Equal("", row!.Title);
        Assert.Equal(2, row.Metrics.Count);
    }

    [Fact]
    public void Пустая_строка_убирает_ряд()
    {
        Assert.Null(OverlayRow.Parse("", out _));
        Assert.Null(OverlayRow.Parse("   ", out _));
    }

    [Fact]
    public void Регистр_значения_не_важен()
    {
        OverlayRow? row = OverlayRow.Parse("cpu: cpuload, GPULOAD", out _);

        Assert.Equal(new[] { OverlayMetric.CpuLoad, OverlayMetric.GpuLoad }, row!.Metrics);
    }

    [Fact]
    public void Неизвестное_значение_не_проходит_молча()
    {
        OverlayRow? row = OverlayRow.Parse("CPU: CpuLoad, Погода", out string? problem);

        // The row is dropped and the reason is named: the file is edited by hand.
        Assert.Null(row);
        Assert.Contains("Погода", problem);
        Assert.Contains("CpuLoad", problem);
    }

    [Fact]
    public void Значения_из_разных_устройств_живут_в_одной_строке()
    {
        // Nothing binds a value to a particular row: the point of the setting is to arrange them
        // as the user likes.
        OverlayRow? row = OverlayRow.Parse("ALL: CpuLoad, GpuLoad, Fps", out _);

        Assert.Equal(3, row!.Metrics.Count);
    }

    [Fact]
    public void Ряды_читаются_из_конфига_по_порядку()
    {
        AppConfig cfg = ConfFormat.Read(
            "[AppearanceFullScreen]\n" +
            "Row1 = FPS: Fps, FrameTime\n" +
            "Row2 = CPU: CpuLoad\n");

        Assert.Equal(new[] { "FPS", "CPU" }, cfg.Appearance.Rows.Select(r => r.Title));
    }

    [Fact]
    public void Без_параметров_ряды_остаются_по_умолчанию()
    {
        AppConfig cfg = ConfFormat.Read("[AppearanceFullScreen]\nMargin = 4\n");

        Assert.Equal(new[] { "CPU", "GPU", "FPS" }, cfg.Appearance.Rows.Select(r => r.Title));
    }

    [Fact]
    public void Пустой_параметр_убирает_ряд()
    {
        AppConfig cfg = ConfFormat.Read(
            "[AppearanceFullScreen]\n" +
            "Row1 = CPU: CpuLoad\n" +
            "Row2 =\n");

        Assert.Single(cfg.Appearance.Rows);
    }

    [Fact]
    public void Ряды_переживают_запись_и_чтение()
    {
        var cfg = new AppConfig();
        cfg.Appearance.Rows = new()
        {
            new OverlayRow("ALL", new[] { OverlayMetric.CpuLoad, OverlayMetric.GpuLoad }),
            new OverlayRow("FAN", new[] { OverlayMetric.CpuFan, OverlayMetric.ExtraFans })
        };

        AppConfig read = ConfFormat.Read(ConfFormat.Write(cfg));

        Assert.Equal(new[] { "ALL", "FAN" }, read.Appearance.Rows.Select(r => r.Title));
        Assert.Equal(new[] { OverlayMetric.CpuFan, OverlayMetric.ExtraFans },
                     read.Appearance.Rows[1].Metrics);
    }

    [Fact]
    public void Панель_строится_ровно_по_конфигу()
    {
        var rows = new[]
        {
            new OverlayRow("FPS", new[] { OverlayMetric.Fps }),
            new OverlayRow("CPU", new[] { OverlayMetric.CpuTemp, OverlayMetric.CpuLoad })
        };

        var vm = new OverlayViewModel(new WarnConfig(), rows);

        Assert.Equal(new[] { "FPS", "CPU" }, vm.Groups.Select(g => g.Title));
        Assert.Single(vm.Groups[0].Metrics);
        Assert.Equal("°C", vm.Groups[1].Metrics[0].Unit);
    }

    [Fact]
    public void ExtraFans_разворачивается_в_ячейку_на_каждый_вентилятор()
    {
        var rows = new[] { new OverlayRow("FAN", new[] { OverlayMetric.CpuFan, OverlayMetric.ExtraFans }) };

        var vm = new OverlayViewModel(new WarnConfig(), rows, extraFans: 3);

        Assert.Equal(4, vm.Groups[0].Metrics.Count);
    }

    [Fact]
    public void Ошибка_в_ряду_не_ломает_остальные()
    {
        // A mistake in the looks of a panel must not keep the app from starting: the row is
        // dropped, the reason goes to the log, the rest stands.
        AppConfig cfg = ConfFormat.Read(
            "[AppearanceFullScreen]\n" +
            "Row1 = CPU: CpuLoad\n" +
            "Row2 = GPU: Погода\n" +
            "Row3 = FPS: Fps\n");

        Assert.Equal(new[] { "CPU", "FPS" }, cfg.Appearance.Rows.Select(r => r.Title));
    }

    [Fact]
    public void Тег_без_значений_считается_ошибкой()
    {
        Assert.Null(OverlayRow.Parse("CPU:", out string? problem));
        Assert.Contains("CPU", problem);
    }

    [Fact]
    public void Одно_значение_в_двух_рядах_получает_свои_ячейки()
    {
        // The same value may stand twice; each cell measures its own column, so they must not
        // share one object.
        var rows = new[]
        {
            new OverlayRow("A", new[] { OverlayMetric.CpuLoad }),
            new OverlayRow("B", new[] { OverlayMetric.CpuTemp, OverlayMetric.CpuLoad })
        };

        var vm = new OverlayViewModel(new WarnConfig(), rows);
        vm.Layout(slots => slots * 10, unit => unit.Length * 5, unitGap: 2, columnGap: 6);

        Metric first = vm.Groups[0].Metrics[0];
        Metric second = vm.Groups[1].Metrics[1];

        Assert.NotSame(first, second);
        Assert.NotEqual(first.CellWidth, second.CellWidth);
    }

    [Fact]
    public void Значение_обновляется_во_всех_ячейках()
    {
        var rows = new[]
        {
            new OverlayRow("A", new[] { OverlayMetric.CpuLoad }),
            new OverlayRow("B", new[] { OverlayMetric.CpuLoad })
        };

        var vm = new OverlayViewModel(new WarnConfig(), rows);
        vm.Apply(new SystemSpinnerX64.Monitoring.Readings { CpuLoad = 42 });

        Assert.Equal("42", vm.Groups[0].Metrics[0].Value);
        Assert.Equal("42", vm.Groups[1].Metrics[0].Value);
    }

    [Fact]
    public void Ряд_без_значений_не_попадает_на_панель()
    {
        var rows = new[]
        {
            new OverlayRow("CPU", new[] { OverlayMetric.CpuLoad }),
            new OverlayRow("FAN", new[] { OverlayMetric.ExtraFans })   // no extra fans configured
        };

        var vm = new OverlayViewModel(new WarnConfig(), rows);

        Assert.Single(vm.Groups);
    }
}
