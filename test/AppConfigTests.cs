//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using SystemSpinnerX64.Configuration;
using Xunit;

namespace SystemSpinnerX64.Tests;

/// <summary>
/// Where the settings live. The file format itself is checked in <see cref="ConfFormatTests"/> —
/// this is only about the choice of place.
/// </summary>
public class AppConfigTests
{
    [Fact]
    public void Запасная_папка_лежит_в_профиле_пользователя() =>
        Assert.Contains("System-Spinner", AppConfig.FallbackDirectory);

    [Fact]
    public void Журнал_кладётся_рядом_с_конфигом() =>
        Assert.Equal(System.IO.Path.GetDirectoryName(AppConfig.UserPath), AppConfig.FallbackDirectory);

    [Theory]
    [InlineData(@"C:\Program Files\System-Spinner")]
    [InlineData(@"c:\program files\systemspinnerwin")]              // case does not matter
    [InlineData(@"C:\Program Files (x86)\System-Spinner")]
    [InlineData(@"C:\Windows\System32")]
    [InlineData(@"C:\ProgramData\System-Spinner")]
    [InlineData(@"C:\Program Files")]                          // the folder itself, nothing inside
    [InlineData(@"C:\Program Files\")]                         // with a trailing slash
    [InlineData(@"D:\Program Files\System-Spinner")]              // system folders are not only on C
    [InlineData(@"E:\Windows\System32\config")]
    [InlineData(@"C:\Program Files\Vendor\Tools\System-Spinner")] // any depth
    public void Системные_папки_узнаются(string directory) =>
        Assert.True(AppConfig.IsSystemFolder(directory));

    [Theory]
    [InlineData(@"D:\Tools\System-Spinner")]
    [InlineData(@"C:\Users\Игрок\Downloads\System-Spinner")]
    [InlineData(@"C:\Program Files Backup\System-Spinner")]       // a different segment, similar as it looks
    [InlineData(@"C:\Games\Windows Mixed Reality")]            // "Windows" is part of the name here
    [InlineData(@"D:\MyWindows\System-Spinner")]
    public void Обычные_папки_системными_не_считаются(string directory) =>
        Assert.False(AppConfig.IsSystemFolder(directory));
}
