//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using SystemSpinnerX64.Configuration;
using SystemSpinnerX64.Monitoring;
using Xunit;

namespace SystemSpinnerX64.Tests;

/// <summary>
/// Sorting detected fans into the panel slots. The decisions fixed here are easy to "simplify"
/// back: an empty AIO slot, and case fans as fallbacks for the processor.
/// </summary>
public class FanConfigTests
{
    private static FanSensor Fan(string name, FanRole role, double? rpm) =>
        new(name, "Nuvoton NCT6798D", role, rpm);

    [Fact]
    public void Вентиляторы_раскладываются_по_ролям()
    {
        var config = new FanConfig();

        Assert.True(config.ApplyDetected(new List<FanSensor>
        {
            Fan("CPU Fan", FanRole.Cpu, 540),
            Fan("AIO Pump", FanRole.Aio, 2220),
            Fan("GPU Fan 1", FanRole.Gpu, 1000)
        }));

        Assert.Equal(new[] { "CPU Fan" }, config.Cpu);
        Assert.Equal(new[] { "AIO Pump" }, config.Aio);
        Assert.Equal(new[] { "GPU Fan 1" }, config.Gpu);
    }

    [Fact]
    public void Корпусные_идут_в_слот_процессора_запасными()
    {
        // The slot is labelled just RPM: on boards where the cooler hangs off SYS_FAN it is better
        // to show something real than a dash.
        var config = new FanConfig();

        config.ApplyDetected(new List<FanSensor>
        {
            Fan("Chassis Fan", FanRole.Case, 700),
            Fan("CPU Fan", FanRole.Cpu, 540)
        });

        Assert.Equal(new[] { "CPU Fan", "Chassis Fan" }, config.Cpu);
    }

    [Fact]
    public void Слот_водянки_остаётся_пустым_если_насоса_нет()
    {
        // The key decision: a case fan in the AIO slot would look like a working reading from
        // a pump that does not exist in this system.
        var config = new FanConfig();

        config.ApplyDetected(new List<FanSensor>
        {
            Fan("CPU Fan", FanRole.Cpu, 540),
            Fan("Chassis Fan", FanRole.Case, 700)
        });

        Assert.Empty(config.Aio);
    }

    [Fact]
    public void Крутящиеся_вентиляторы_идут_раньше_молчащих()
    {
        var config = new FanConfig();

        config.ApplyDetected(new List<FanSensor>
        {
            Fan("GPU Fan 1", FanRole.Gpu, 0),
            Fan("GPU Fan 2", FanRole.Gpu, 1200),
            Fan("GPU Fan 3", FanRole.Gpu, 900)
        });

        Assert.Equal(new[] { "GPU Fan 2", "GPU Fan 3", "GPU Fan 1" }, config.Gpu);
    }

    [Fact]
    public void Одинаковые_имена_не_дублируются()
    {
        var config = new FanConfig();

        config.ApplyDetected(new List<FanSensor>
        {
            Fan("Fan #1", FanRole.Case, 500),
            new("Fan #1", "Другой контроллер", FanRole.Case, 700)
        });

        Assert.Single(config.Cpu);
    }

    [Fact]
    public void Пустое_сканирование_ничего_не_меняет()
    {
        var config = new FanConfig { Cpu = { "CPU Fan" } };

        Assert.False(config.ApplyDetected(new List<FanSensor>()));
        Assert.Equal(new[] { "CPU Fan" }, config.Cpu);
    }

    [Fact]
    public void Список_считается_пустым_только_когда_пусты_все_три()
    {
        Assert.True(new FanConfig().IsEmpty);
        Assert.False(new FanConfig { Gpu = { "GPU Fan 1" } }.IsEmpty);
    }

    [Fact]
    public void Готовый_список_дополнительных_сканирование_не_трогает()
    {
        // A list written by hand outranks the scan: a rescan must not rearrange it.
        var config = new FanConfig { Extra = { "System Fan #2" } };

        config.ApplyDetected(new List<FanSensor>
        {
            Fan("CPU Fan", FanRole.Cpu, 540),
            Fan("Chassis Fan", FanRole.Case, 700)
        });

        Assert.Equal(new[] { "System Fan #2" }, config.Extra);
    }

    [Fact]
    public void Корпусные_получают_свои_ячейки()
    {
        var config = new FanConfig();

        config.ApplyDetected(new List<FanSensor>
        {
            Fan("CPU Fan", FanRole.Cpu, 540),
            Fan("Chassis Fan", FanRole.Case, 700),
            Fan("Chassis Fan #2", FanRole.Case, 900)
        });

        // The faster one first, as everywhere else.
        Assert.Equal(new[] { "Chassis Fan #2", "Chassis Fan" }, config.Extra);
    }

    [Fact]
    public void Пустые_разъёмы_ячеек_не_получают()
    {
        // A board with seven headers and two fans in them: five steady zeros would be five cells
        // that say nothing.
        var config = new FanConfig();

        config.ApplyDetected(new List<FanSensor>
        {
            Fan("Fan #1", FanRole.Case, 782),
            Fan("Fan #2", FanRole.Case, 600),
            Fan("Fan #3", FanRole.Case, 0),
            Fan("Fan #4", FanRole.Case, 0)
        });

        // Fan #1 is what the processor slot shows, so it is not repeated as a case fan.
        Assert.Equal(new[] { "Fan #1", "Fan #2", "Fan #3", "Fan #4" }, config.Cpu);
        Assert.Equal(new[] { "Fan #2" }, config.Extra);
    }

    [Fact]
    public void Показанный_как_процессорный_не_повторяется()
    {
        var config = new FanConfig();

        config.ApplyDetected(new List<FanSensor> { Fan("Chassis Fan", FanRole.Case, 700) });

        Assert.Equal(new[] { "Chassis Fan" }, config.Cpu);
        Assert.Empty(config.Extra);
    }
}
