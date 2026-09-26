//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using SystemSpinnerX64.Configuration;
using Xunit;

namespace SystemSpinnerX64.Tests;

/// <summary>The settings file format: reading breaks on details like a hash inside a colour.</summary>
public class ConfFormatTests
{
    [Fact]
    public void Настройки_переживают_запись_и_чтение()
    {
        var written = new AppConfig
        {
            Language = Localization.Language.Ja,
            UpdateIntervalMs = 1500,
            GpuIndex = 1,
            ShowOverlayInGames = false,
            SpinOnDesktop = false,
            Debug = true,
            Sensors = { CpuLoad = { "CPU Core Max" }, VramUsed = { "My VRAM" }, CpuClockCores = "Core" },
            Fans = { Cpu = { "CPU Fan" }, Extra = { "System Fan #2", "PSU Fan" }, AverageCpu = true },
            Warn = { Color = "Gold", CpuTemp = 90, GpuTemp = 0, SysMem = 75, GpuMem = 80 },
            Osd = { AdjustmentSteps = 24, ControlExternalBrightness = false },
            Stats = { HistoryPoints = 300, TopProcesses = 5, ShowExternalAddress = false },
            Appearance = { FontFamily = "Consolas", TextColor = "#00FF00", Margin = 24,
                           BlackListApplications = new() { "SnippingTool*", "HandBrake.exe" } }
        };

        AppConfig read = ConfFormat.Read(ConfFormat.Write(written));

        Assert.Equal(Localization.Language.Ja, read.Language);
        Assert.Equal(1500, read.UpdateIntervalMs);   // the file speaks seconds, the timers milliseconds
        Assert.Equal(1, read.GpuIndex);
        Assert.False(read.ShowOverlayInGames);
        Assert.False(read.SpinOnDesktop);
        Assert.True(read.Debug);
        Assert.Contains("CPU Core Max", read.Sensors.CpuLoad);
        Assert.Contains("My VRAM", read.Sensors.VramUsed);
        Assert.Equal("Core", read.Sensors.CpuClockCores);
        Assert.Equal(new[] { "System Fan #2", "PSU Fan" }, read.Fans.Extra);
        Assert.True(read.Fans.AverageCpu);
        Assert.Equal("Gold", read.Warn.Color);
        Assert.Equal(90, read.Warn.CpuTemp);
        Assert.Equal(0, read.Warn.GpuTemp);      // zero has to survive a write and a read
        Assert.Equal(75, read.Warn.SysMem);      // written as "75 %", read back as a number
        Assert.Equal(80, read.Warn.GpuMem);
        Assert.Equal(24, read.Osd.AdjustmentSteps);
        Assert.False(read.Osd.ControlExternalBrightness);
        Assert.Equal(300, read.Stats.HistoryPoints);
        Assert.Equal(5, read.Stats.TopProcesses);
        Assert.False(read.Stats.ShowExternalAddress);
        Assert.Equal("Consolas", read.Appearance.FontFamily);
        Assert.Equal("#00FF00", read.Appearance.TextColor);
        Assert.Equal(24, read.Appearance.Margin);
        Assert.Equal(new[] { "SnippingTool*", "HandBrake.exe" }, read.Appearance.BlackListApplications);
    }

    [Fact]
    public void Пустой_чёрный_список_никого_не_исключает()
    {
        // "BlackListApplications =" means "keep away from nothing", not "take the standard list".
        AppConfig cfg = ConfFormat.Read("[FullScreenOverlay]\nBlackListApplications =\n");

        Assert.Empty(cfg.Appearance.BlackListApplications);
        Assert.Empty(ProcessPattern.Compile(cfg.Appearance.BlackListApplications));
    }

    [Fact]
    public void Без_параметра_действует_стандартный_чёрный_список()
    {
        AppConfig cfg = ConfFormat.Read("[FullScreenOverlay]\nMargin = 4\n");

        Assert.Equal(AppearanceConfig.DefaultBlackList(), cfg.Appearance.BlackListApplications);
        Assert.Contains("SnippingTool*", cfg.Appearance.BlackListApplications);
    }

    [Fact]
    public void Панель_выключается_параметром_Enable()
    {
        Assert.False(ConfFormat.Read("[FullScreenOverlay]\nEnable = false\n").ShowOverlayInGames);

        // The old name under [General] is not read any more: the section is written in anew instead.
        Assert.True(ConfFormat.Read("[General]\nShowOverlayInGames = false\n").ShowOverlayInGames);
    }

    [Fact]
    public void Недостающие_секции_называются_по_имени()
    {
        // A file without [Sensors], [FullScreenOverlay] or [Aura]; the startup writes the file back with them.
        AppConfig old = ConfFormat.Read("[General]\nDebug = false\nGpuIndex = 0\n[Spinner]\nStyle = Loader\n");

        Assert.Equal(new[] { "Sensors", "FullScreenOverlay", "Aura" }, old.MissingSections);
        Assert.Empty(ConfFormat.Read(ConfFormat.Write(new AppConfig())).MissingSections);
    }

    [Fact]
    public void Решётка_внутри_значения_не_считается_комментарием()
    {
        AppConfig cfg = ConfFormat.Read("[FullScreenOverlay]\nTextColor = #FFAA00\n");

        Assert.Equal("#FFAA00", cfg.Appearance.TextColor);
    }

    [Fact]
    public void Комментарии_и_пустые_строки_пропускаются()
    {
        AppConfig cfg = ConfFormat.Read("""
            # пояснение
            ; и такое тоже

            [General]
              GpuIndex = 2
            """);

        Assert.Equal(2, cfg.GpuIndex);
    }

    [Fact]
    public void Дробное_число_принимается_и_с_запятой()
    {
        Assert.Equal(0.5, ConfFormat.Read("[FullScreenOverlay]\nTextOpacity = 0,5\n").Appearance.TextOpacity);
        Assert.Equal(0.5, ConfFormat.Read("[FullScreenOverlay]\nTextOpacity = 0.5\n").Appearance.TextOpacity);
    }

    [Fact]
    public void Отсутствующий_параметр_оставляет_значение_по_умолчанию()
    {
        AppConfig cfg = ConfFormat.Read("[General]\nGpuIndex = 1\n");

        Assert.Equal(1000, cfg.UpdateIntervalMs);
        Assert.True(cfg.ShowOverlayInGames);
        Assert.Null(cfg.Debug);   // the "log the first run in full" rule rests on this
    }

    [Fact]
    public void Пустое_перечисление_читается_как_пустой_список()
    {
        // "Aio =" means "there is no pump", not "take the default".
        AppConfig cfg = ConfFormat.Read("[Fans]\nAio =\n");

        Assert.Empty(cfg.Fans.Aio);
    }

    [Theory]
    [InlineData("[General]\nGpuIndex = не число\n")]
    [InlineData("[FullScreenOverlay]\nEnable = ага\n")]
    [InlineData("[General]\nDebug = ага\n")]
    [InlineData("[General\nGpuIndex = 1\n")]
    [InlineData("GpuIndex = 1\n")]                       // a value outside any section
    public void Ошибка_в_файле_не_проходит_молча(string text) =>
        Assert.ThrowsAny<FormatException>(() => ConfFormat.Read(text));

    [Fact]
    public void Образец_в_корне_проекта_совпадает_с_настройками_по_умолчанию()
    {
        // sample.conf is the settings documentation — it must not go stale.
        string sample = File.ReadAllText(FindSample());

        Assert.Equal(Normalize(ConfFormat.Write(new AppConfig())), Normalize(sample));
    }

    [Fact]
    public void Датчики_читаются_из_своей_секции()
    {
        AppConfig read = ConfFormat.Read(
            "[Sensors]\nRamUsed = Used Memory\nVramUsed = GPU Memory Used\nCpuClockCores = Core\n");

        Assert.Equal(new[] { "Used Memory" }, read.Sensors.RamUsed);
        Assert.Equal(new[] { "GPU Memory Used" }, read.Sensors.VramUsed);
        Assert.Equal("Core", read.Sensors.CpuClockCores);
    }

    [Fact]
    public void Датчики_в_Hardware_больше_не_читаются()
    {
        // Configs from 1.6 and before are not carried over: [Hardware] and its old keys are ignored.
        AppConfig read = ConfFormat.Read("[Hardware]\nGpuMemory = GPU Memory Used\nCpuLoad = Something\n");

        Assert.Equal(new SensorNamesConfig().VramUsed, read.Sensors.VramUsed);
        Assert.Equal(new SensorNamesConfig().CpuLoad, read.Sensors.CpuLoad);
    }

    [Fact]
    public void Имена_ключей_датчиков_годятся_в_строку_панели()
    {
        OverlayRow? row = OverlayRow.Parse("MEM: RamUsed, VramUsed, CpuClockCores", out string? problem);

        Assert.Null(problem);
        Assert.Equal([OverlayMetric.SysMemory, OverlayMetric.GpuMemory, OverlayMetric.CpuClock], row!.Metrics);
        Assert.Equal("MEM: SysMemory, GpuMemory, CpuClock", row.ToString());    // written back by panel names
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n").TrimEnd();

    private static string FindSample()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "sample.conf");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("sample.conf was not found in any parent folder");
    }
}
