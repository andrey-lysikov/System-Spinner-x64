//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using SystemSpinnerX64.Configuration;
using SystemSpinnerX64.Diagnostics;
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
            Sensors = { CpuLoad = { "CPU Core Max" }, GpuMemory = { "D3D Dedicated Memory Used" } },
            Fans = { Cpu = { "CPU Fan" }, Extra = { "System Fan #2", "PSU Fan" }, AverageCpu = true },
            Warn = { Color = "Gold", CpuTemp = 90, GpuTemp = 0, SysMem = 75, GpuMem = 80 },
            Osd = { AdjustmentSteps = 24, ControlExternalBrightness = false },
            Stats = { HistoryPoints = 300, TopProcesses = 5, ShowExternalAddress = false },
            Appearance = { FontFamily = "Consolas", TextColor = "#00FF00", Margin = 24 }
        };

        AppConfig read = ConfFormat.Read(ConfFormat.Write(written));

        Assert.Equal(Localization.Language.Ja, read.Language);
        Assert.Equal(1500, read.UpdateIntervalMs);   // the file speaks seconds, the timers milliseconds
        Assert.Equal(1, read.GpuIndex);
        Assert.False(read.ShowOverlayInGames);
        Assert.False(read.SpinOnDesktop);
        Assert.True(read.Debug);
        Assert.Contains("CPU Core Max", read.Sensors.CpuLoad);
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
    }

    [Fact]
    public void Решётка_внутри_значения_не_считается_комментарием()
    {
        AppConfig cfg = ConfFormat.Read("[AppearanceFullScreen]\nTextColor = #FFAA00\n");

        Assert.Equal("#FFAA00", cfg.Appearance.TextColor);
    }

    [Fact]
    public void Комментарии_и_пустые_строки_пропускаются()
    {
        AppConfig cfg = ConfFormat.Read("""
            # пояснение
            ; и такое тоже

            [Hardware]
              GpuIndex = 2
            """);

        Assert.Equal(2, cfg.GpuIndex);
    }

    [Fact]
    public void Дробное_число_принимается_и_с_запятой()
    {
        Assert.Equal(0.5, ConfFormat.Read("[AppearanceFullScreen]\nTextOpacity = 0,5\n").Appearance.TextOpacity);
        Assert.Equal(0.5, ConfFormat.Read("[AppearanceFullScreen]\nTextOpacity = 0.5\n").Appearance.TextOpacity);
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
    [InlineData("[Hardware]\nGpuIndex = не число\n")]
    [InlineData("[General]\nShowOverlayInGames = ага\n")]
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
    public void Прежний_порядок_датчиков_видеопамяти_заменяется()
    {
        // What older versions wrote, word for word. The card's own count of used memory sticks at
        // the peak on NVIDIA, so a file carrying that order is carrying a fault, not a decision.
        AppConfig read = ConfFormat.Read(
            "[Hardware]\nGpuMemory = GPU Memory Used, D3D Dedicated Memory Used, GPU Memory Dedicated Used\n");

        Assert.Equal("D3D Dedicated Memory Used", read.Sensors.GpuMemory[0]);
    }

    [Fact]
    public void Выбранный_вручную_датчик_видеопамяти_остаётся()
    {
        // Anything but that exact list is somebody's own choice and is left alone — including
        // the very sensor the default moved away from.
        AppConfig read = ConfFormat.Read("[Hardware]\nGpuMemory = GPU Memory Used\n");

        Assert.Equal(new[] { "GPU Memory Used" }, read.Sensors.GpuMemory);
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
