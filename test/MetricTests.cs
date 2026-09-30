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
