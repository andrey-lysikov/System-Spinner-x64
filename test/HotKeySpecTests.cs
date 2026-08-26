//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using SystemSpinnerX64.Devices;
using Xunit;

namespace SystemSpinnerX64.Tests;

/// <summary>
/// Reading the stand-in brightness keys out of the config. A wrong line there must leave the keys
/// unregistered and say why, never throw.
/// </summary>
public class HotKeySpecTests
{
    [Fact]
    public void Сочетание_разбирается()
    {
        HotKeySpec? spec = HotKeySpec.Parse("Ctrl+F1/F2", out string? problem);

        Assert.Null(problem);
        Assert.NotNull(spec);
        Assert.Equal(HotKeySpec.ModControl, spec!.Value.Modifiers);
        Assert.Equal(0x70, spec.Value.DownKey);   // VK_F1, the dimmer one comes first
        Assert.Equal(0x71, spec.Value.UpKey);
    }

    [Theory]
    [InlineData("ctrl+f1/f2")]
    [InlineData("CTRL+F1/F2")]
    [InlineData("  Ctrl + F1 / F2  ")]
    public void Регистр_и_пробелы_не_важны(string text)
    {
        HotKeySpec? spec = HotKeySpec.Parse(text, out string? problem);

        Assert.Null(problem);
        Assert.Equal("Ctrl+F1/F2", spec!.Value.Describe);
    }

    [Fact]
    public void Модификаторы_складываются()
    {
        HotKeySpec? spec = HotKeySpec.Parse("Ctrl+Shift+F11/F12", out _);

        Assert.Equal(HotKeySpec.ModControl | HotKeySpec.ModShift, spec!.Value.Modifiers);
        Assert.Equal("Ctrl+Shift+F11/F12", spec.Value.Describe);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("NONE")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Выключение_не_считается_ошибкой(string? text)
    {
        Assert.Null(HotKeySpec.Parse(text, out string? problem));
        Assert.Null(problem);
    }

    [Fact]
    public void Голая_клавиша_отвергается()
    {
        // F1 is help and F2 renames things: taking them from every application is not something
        // to do because a line in a config asked for it in passing.
        Assert.Null(HotKeySpec.Parse("F1/F2", out string? problem));
        Assert.Contains("every application", problem);
    }

    [Theory]
    [InlineData("Ctrl+F1", "pair of keys")]
    [InlineData("Ctrl+F1/F2/F3", "pair of keys")]
    [InlineData("Ctrl+A/B", "F1 to F24")]
    [InlineData("Ctrl+F0/F1", "F1 to F24")]
    [InlineData("Ctrl+F1/F25", "F1 to F24")]
    [InlineData("Hyper+F1/F2", "not Ctrl, Alt, Shift or Win")]
    [InlineData("Ctrl+F1/F1", "same key twice")]
    public void Ошибка_объясняется_и_ничего_не_регистрируется(string text, string expected)
    {
        Assert.Null(HotKeySpec.Parse(text, out string? problem));
        Assert.Contains(expected, problem);
    }
}
