//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using SystemSpinnerX64.Configuration;
using Xunit;

namespace SystemSpinnerX64.Tests;

/// <summary>The exclusion list: which full-screen applications a config name stands for. A wrong
/// match means the panel disappears over a game or stays over the Snipping Tool.</summary>
public class ProcessPatternTests
{
    private const string HandBrake = @"C:\Program Files\HandBrake\HandBrake.exe";
    private const string Snipping = @"C:\Program Files\WindowsApps\Microsoft.ScreenSketch_11.2\SnippingTool\SnippingTool.exe";

    [Theory]
    [InlineData("HandBrake.exe")]
    [InlineData("HandBrake")]
    [InlineData("handbrake")]                              // case does not matter
    [InlineData("Hand*")]
    [InlineData("*brake*")]
    [InlineData("HandBrake.???")]
    [InlineData("H?ndBrake")]
    [InlineData(@"C:\Program Files\HandBrake\*")]          // a path is held against the whole path
    [InlineData(@"c:\program files\*\handbrake.exe")]
    public void Имя_из_списка_узнаёт_приложение(string pattern) =>
        Assert.True(new ProcessPattern(pattern).Matches(HandBrake));

    [Theory]
    [InlineData("HandBrake")]                              // the whole name, not a part of it
    [InlineData("Snip")]
    [InlineData("SnippingTool.ex")]
    [InlineData("*.dll")]
    [InlineData(@"D:\*")]
    [InlineData("SnippingTool?")]                          // ? is exactly one character, not none
    public void Чужое_имя_не_подходит(string pattern) =>
        Assert.False(new ProcessPattern(pattern).Matches(Snipping));

    [Fact]
    public void Точка_в_имени_не_является_любым_символом() =>
        Assert.False(new ProcessPattern("HandBrake.exe").Matches(@"C:\HandBrakeXexe"));

    [Fact]
    public void Пробелы_вокруг_имени_не_мешают()
    {
        List<ProcessPattern> patterns = ProcessPattern.Compile(new[] { "  HandBrake  " });

        Assert.Equal("HandBrake", patterns[0].Text);
        Assert.True(patterns[0].Matches(HandBrake));
    }

    [Fact]
    public void Пустые_записи_пропускаются()
    {
        // "HandBrake, ,SnippingTool" must not turn into a pattern that matches every application.
        List<ProcessPattern> patterns = ProcessPattern.Compile(new[] { "HandBrake", " ", "" });

        Assert.Single(patterns);
    }

    [Fact]
    public void Находится_первая_подходящая_запись()
    {
        List<ProcessPattern> patterns = ProcessPattern.Compile(new[] { "vlc", "Snipping*", "*.exe" });

        Assert.Equal("Snipping*", ProcessPattern.Find(patterns, Snipping)!.Text);
        Assert.Equal("*.exe", ProcessPattern.Find(patterns, HandBrake)!.Text);
        Assert.Null(ProcessPattern.Find(patterns, @"C:\Games\Doom\Doom.bin"));
    }
}
