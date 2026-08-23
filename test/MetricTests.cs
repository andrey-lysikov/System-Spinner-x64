//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using SystemSpinnerX64.ViewModels;
using Xunit;

namespace SystemSpinnerX64.Tests;

/// <summary>One panel value: formatting and the rule for a disappearing cell.</summary>
public class MetricTests
{
    [Fact]
    public void Значения_округляются_до_заданного_знака()
    {
        var metric = new Metric("W", 3);

        metric.Update(63.4);
        Assert.Equal("63", metric.Value);

        metric.Update(21.34, decimals: 1);
        Assert.Equal("21.3", metric.Value);
    }

    [Fact]
    public void Разделитель_всегда_точка()
    {
        // The panel must not depend on the system language: a comma would shift the column width.
        var metric = new Metric("GB", 4);

        metric.Update(21.34, decimals: 1);
        Assert.Contains('.', metric.Value);
    }

    [Fact]
    public void Отсутствие_данных_показывается_прочерком()
    {
        var metric = new Metric("°C", 3);

        metric.Update(null);
        Assert.Equal("—", metric.Value);
        Assert.True(metric.Visible);   // the sensor exists and is merely silent — the cell stays
    }

    [Fact]
    public void Значение_подсвечивается_с_порога_и_выше()
    {
        var metric = new Metric("°C", 3);

        metric.Update(84.0, threshold: 85);
        Assert.False(metric.Warning);

        metric.Update(85.0, threshold: 85);
        Assert.True(metric.Warning);   // exactly at the threshold is already an alarm

        metric.Update(90.0, threshold: 85);
        Assert.True(metric.Warning);

        metric.Update(70.0, threshold: 85);
        Assert.False(metric.Warning);  // cooled down — the highlight goes away
    }

    [Fact]
    public void Нулевой_порог_отключает_подсветку()
    {
        // This is how the load highlight is off by default: 99 % in a game is normal, not alarming.
        var metric = new Metric("%", 3);

        metric.Update(100.0, threshold: 0);
        Assert.False(metric.Warning);
    }

    [Fact]
    public void Отсутствие_значения_не_считается_превышением()
    {
        var metric = new Metric("°C", 3);

        metric.Update(null, threshold: 85);
        Assert.False(metric.Warning);
    }

    [Fact]
    public void Ячейка_исчезает_когда_железа_нет()
    {
        // A dash would mean "the sensor is silent", while the truth here is "no such hardware".
        var metric = new Metric("RPM/AIO", 4);

        metric.UpdateOrHide(null);
        Assert.False(metric.Visible);

        metric.UpdateOrHide(2220);
        Assert.True(metric.Visible);
        Assert.Equal("2220", metric.Value);
    }
}
