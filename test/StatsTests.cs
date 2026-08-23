//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Linq;
using SystemSpinnerX64.Monitoring;
using SystemSpinnerX64.Views;
using Xunit;

namespace SystemSpinnerX64.Tests;

/// <summary>What the status window shows: history reduction, network rate, memory.</summary>
public class StatsTests
{
    [Fact]
    public void История_сворачивается_к_числу_столбцов()
    {
        double[] history = Enumerable.Range(0, 900).Select(i => (double)(i % 100)).ToArray();

        Assert.Equal(300, SparklineControl.Reduce(history, 300).Length);
    }

    [Fact]
    public void В_столбце_остаётся_наибольшее()
    {
        // An average would smooth away the spike — the one thing the chart is looked at for.
        double[] reduced = SparklineControl.Reduce(new double[] { 1, 99, 2, 3, 4, 5 }, 2);

        Assert.Equal(99, reduced[0]);
        Assert.Equal(5, reduced[1]);
    }

    [Fact]
    public void Короткая_история_не_растягивается()
    {
        double[] history = { 10, 20, 30 };

        Assert.Equal(history, SparklineControl.Reduce(history, 100));
    }

    [Fact]
    public void Пустая_история_не_роняет_свёртку() =>
        Assert.Empty(SparklineControl.Reduce(Array.Empty<double>(), 100));

    [Fact]
    public void Хвост_истории_ограничен_вместимостью()
    {
        var history = new History(10);
        for (int i = 0; i < 100; i++) history.Add(i);

        var points = history.Snapshot();

        Assert.Equal(10, points.Count);
        Assert.Equal(99, points[^1]);   // the last value has to survive
    }

    [Theory]
    [InlineData(512, "KB/s")]
    [InlineData(2 * 1024 * 1024, "MB/s")]
    [InlineData(3.0 * 1024 * 1024 * 1024, "GB/s")]
    public void Единица_скорости_подбирается_под_величину(double bytesPerSecond, string unit) =>
        Assert.Equal(unit, new Throughput(bytesPerSecond).Unit);

    [Fact]
    public void Адрес_выделяется_из_ответа_службы()
    {
        // The answer arrives as a page rather than a number: "<html><head><title>Current IP Check…".
        const string page = "<html><head><title>Current IP Check</title></head>" +
                            "<body>Current IP Address: 203.0.113.42</body></html>";

        Assert.Equal("203.0.113.42", NetworkMonitor.ParseAddress(page));
    }

    [Fact]
    public void Ответ_без_адреса_не_выдумывается() =>
        Assert.Null(NetworkMonitor.ParseAddress("<html><body>Service unavailable</body></html>"));

    [Fact]
    public void Адрес_IPv6_тоже_выделяется()
    {
        // On a machine with IPv6 alone the service answers with an address of that family.
        const string page = "<html><body>Current IP Address: 2001:db8::8a2e:370:7334</body></html>";

        Assert.Equal("2001:db8::8a2e:370:7334", NetworkMonitor.ParseAddress(page));
    }

    [Fact]
    public void IPv4_предпочитается_IPv6_в_одном_ответе()
    {
        const string page = "<html><body>IPv4: 203.0.113.42, IPv6: 2001:db8::1</body></html>";

        Assert.Equal("203.0.113.42", NetworkMonitor.ParseAddress(page));
    }

    [Fact]
    public void Разметка_не_принимается_за_IPv6() =>
        // Colons are everywhere in a page; only what the framework accepts as an address counts.
        Assert.Null(NetworkMonitor.ParseAddress("<html><body style=\"color: red\">no address here</body></html>"));

    [Fact]
    public void Скорость_спиннера_берётся_по_наибольшей_из_двух_загрузок()
    {
        // A game leans on the card while the processor idles: spinning by the CPU alone would
        // leave the icon nearly still at the busiest moment.
        Assert.Equal(87, new Readings { CpuLoad = 12, GpuLoad = 87 }.BusiestLoad);
        Assert.Equal(64, new Readings { CpuLoad = 64, GpuLoad = 9 }.BusiestLoad);
    }

    [Fact]
    public void Молчащий_датчик_не_мешает_второму()
    {
        // No card in the machine, or its load did not read: the processor decides alone.
        Assert.Equal(40, new Readings { CpuLoad = 40 }.BusiestLoad);
        Assert.Equal(0, new Readings().BusiestLoad);
    }

    [Fact]
    public void Занятая_память_считается_от_суммы_занятой_и_свободной()
    {
        var readings = new Readings { SysMemUsedGb = 24, SysMemFreeGb = 8 };

        Assert.Equal(75, readings.MemLoadPercent);
    }

    [Fact]
    public void Без_свободной_памяти_доля_не_выдумывается()
    {
        // LHM does not report the installed total separately — there is nothing to take a percentage of.
        var readings = new Readings { SysMemUsedGb = 24 };

        Assert.Null(readings.MemLoadPercent);
    }

    [Fact]
    public void Встроенная_графика_не_имеет_своей_памяти()
    {
        // Shared memory: the amount is the system one, and a scale of it would repeat the row above.
        Assert.False(new Readings { GpuLoad = 41 }.GpuHasOwnMemory);
        Assert.False(new Readings { GpuMemUsedGb = 1.2, GpuMemTotalGb = 0 }.GpuHasOwnMemory);
    }

    [Fact]
    public void Дискретная_карта_имеет_свою_память() =>
        Assert.True(new Readings { GpuMemUsedGb = 5.3, GpuMemTotalGb = 12 }.GpuHasOwnMemory);

    [Fact]
    public void Видеопамять_считается_от_всего_объёма()
    {
        var readings = new Readings { GpuMemUsedGb = 3, GpuMemTotalGb = 12 };

        Assert.Equal(25, readings.GpuMemLoadPercent);
    }

    [Fact]
    public void Без_объёма_видеопамяти_доля_не_выдумывается() =>
        // Integrated graphics may have no total at all — then the scale has nothing to count
        // from, and the row is hidden entirely.
        Assert.Null(new Readings { GpuMemUsedGb = 3 }.GpuMemLoadPercent);
}
