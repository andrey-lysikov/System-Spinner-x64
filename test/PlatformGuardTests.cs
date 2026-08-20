using System;
using SystemSpinnerX64.Platform;
using Xunit;

namespace SystemSpinnerX64.Tests;

/// <summary>
/// Hardware requirements. A refusal here is expensive — the app simply will not start — so both
/// what gets filtered out and what has to pass are checked.
/// </summary>
public class PlatformGuardTests
{
    [Theory]
    [InlineData("Intel Core Ultra 7 265K")]
    [InlineData("13th Gen Intel Core i7-13700K")]
    [InlineData("intel core i5-12400")]     // case does not matter
    public void Intel_проходит(string cpu) =>
        Assert.Null(PlatformGuard.DescribeHardware(cpu, "NVIDIA GeForce RTX 4070"));

    [Theory]
    [InlineData("AMD Ryzen 9 7950X")]
    [InlineData("Apple M4")]
    public void Другой_производитель_объясняется(string cpu)
    {
        string? problem = PlatformGuard.DescribeHardware(cpu, "NVIDIA GeForce RTX 4070");

        Assert.False(string.IsNullOrEmpty(problem));
        Assert.Contains(cpu, problem);
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 5090")]
    [InlineData("Intel Arc A770")]
    [InlineData("Intel UHD Graphics 770")]
    [InlineData("AMD Radeon RX 7900 XTX")]
    [InlineData("")]
    public void Видеокарта_подходит_любая(string gpu)
    {
        // Load, temperature, clock and memory arrive under the same names from every vendor:
        // refusing over the card would be refusing for no reason.
        Assert.Null(PlatformGuard.DescribeHardware("Intel Core Ultra 7 265K", gpu));
    }

    [Fact]
    public void Пустое_имя_процессора_не_отсеивается() =>
        // The sensors did not open — a separate message says so, no need to repeat it.
        Assert.Null(PlatformGuard.DescribeHardware(null, null));

    [Fact]
    public void Имена_датчиков_от_AMD_машины_объясняются()
    {
        string? problem = PlatformGuard.DescribeSensorNames(
            "Intel Core Ultra 7 265K",
            new[] { "Core (Tctl/Tdie)", "CCDs Max (Tdie)" });

        Assert.False(string.IsNullOrEmpty(problem));
        Assert.Contains("CpuTemp", problem);
    }

    [Fact]
    public void Хотя_бы_одно_интеловское_имя_проходит() =>
        Assert.Null(PlatformGuard.DescribeSensorNames(
            "Intel Core i7-13700K",
            new[] { "Core (Tctl/Tdie)", "CPU Package" }));

    [Fact]
    public void Пустой_список_имён_это_осознанный_отказ_от_температуры() =>
        // "CpuTemp =" means "do not show it", like "Aio =" for the pump.
        Assert.Null(PlatformGuard.DescribeSensorNames("Intel Core i7-13700K", Array.Empty<string>()));

    [Fact]
    public void Версия_Windows_проверяется()
    {
        // The tests run on the same machine as the app: since it built, this is Windows 11.
        string? problem = PlatformGuard.DescribeOs();

        Assert.True(problem is null || problem.Contains("Windows 11"));
    }
}
