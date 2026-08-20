using SystemSpinnerX64.Platform;
using Xunit;

namespace SystemSpinnerX64.Tests;

/// <summary>
/// Parsing key combinations from the config. The string is written by hand, and every mistake in
/// it has to be explained in words: a silently dead brightness key looks like a broken program.
/// </summary>
public class HotKeyTests
{
    [Fact]
    public void Сочетание_разбирается()
    {
        HotKey? key = HotKey.Parse("Ctrl+Alt+F2", out string? problem);

        Assert.Null(problem);
        Assert.NotNull(key);
        Assert.True(key!.Modifiers.HasFlag(HotKeyModifiers.Control));
        Assert.True(key.Modifiers.HasFlag(HotKeyModifiers.Alt));
        Assert.Equal(0x71, key.VirtualKey);   // VK_F2
    }

    [Fact]
    public void Повторение_не_принимается()
    {
        // Without NoRepeat, holding the key would drive brightness to the end in a fraction of a second.
        HotKey? key = HotKey.Parse("Ctrl+Up", out _);

        Assert.True(key!.Modifiers.HasFlag(HotKeyModifiers.NoRepeat));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("off")]
    [InlineData("Off")]
    public void Пустая_запись_означает_отказ_от_клавиши(string text)
    {
        Assert.Null(HotKey.Parse(text, out string? problem));
        Assert.Null(problem);   // not an error, nothing to explain
    }

    [Theory]
    [InlineData("F2")]                 // without a modifier the key would be taken from everyone
    [InlineData("Ctrl+Alt+F99")]
    [InlineData("Hyper+F2")]
    [InlineData("Ctrl+")]
    public void Ошибка_объясняется(string text)
    {
        Assert.Null(HotKey.Parse(text, out string? problem));
        Assert.False(string.IsNullOrEmpty(problem));
    }

    [Fact]
    public void Запись_и_разбор_сходятся()
    {
        // The app writes the combination back into config.conf — it must be able to read its own.
        HotKey? first = HotKey.Parse("Win+Shift+PageDown", out _);
        HotKey? again = HotKey.Parse(first!.ToString(), out string? problem);

        Assert.Null(problem);
        Assert.Equal(first, again);
    }

    [Theory]
    [InlineData("ctrl+alt+f2")]
    [InlineData("CTRL+ALT+F2")]
    [InlineData("Ctrl + Alt + F2")]
    public void Регистр_и_пробелы_не_важны(string text) =>
        Assert.Equal(HotKey.Parse("Ctrl+Alt+F2", out _), HotKey.Parse(text, out _));
}
