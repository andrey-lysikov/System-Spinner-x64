using SystemSpinnerX64.Monitoring;
using LibreHardwareMonitor.Hardware;
using Xunit;

namespace SystemSpinnerX64.Tests;

/// <summary>
/// Sorting fans into roles. Worth testing because it is the only guess in the project: every
/// board names its headers differently, and a mistake here is not a failure but a quietly wrong
/// reading — a case fan passed off as a pump.
/// </summary>
public class FanClassifierTests
{
    [Theory]
    [InlineData("AIO Pump")]
    [InlineData("Pump Fan")]
    [InlineData("Water Pump")]
    [InlineData("PUMP")]                 // case must not matter
    [InlineData("Kraken Fan")]
    public void Насос_узнаётся_по_слову_в_имени(string sensor) =>
        Assert.Equal(FanRole.Aio, FanClassifier.Classify(sensor, "Nuvoton NCT6798D", HardwareType.SuperIO, onGpu: false));

    [Fact]
    public void Вентилятор_на_контроллере_водянки_считается_частью_водянки() =>
        Assert.Equal(FanRole.Aio,
            FanClassifier.Classify("Fan #1", "NZXT Kraken X", HardwareType.Cooler, onGpu: false));

    [Fact]
    public void Тот_же_вентилятор_на_плате_водянкой_не_считается() =>
        Assert.Equal(FanRole.Case,
            FanClassifier.Classify("Fan #1", "Nuvoton NCT6798D", HardwareType.SuperIO, onGpu: false));

    [Theory]
    [InlineData("CPU Fan")]
    [InlineData("cpu fan")]
    public void Кулер_процессора_узнаётся_по_слову_cpu(string sensor) =>
        Assert.Equal(FanRole.Cpu, FanClassifier.Classify(sensor, "Nuvoton NCT6798D", HardwareType.SuperIO, onGpu: false));

    [Fact]
    public void Всё_прочее_идёт_в_корпусные() =>
        Assert.Equal(FanRole.Case,
            FanClassifier.Classify("System Fan #2", "Nuvoton NCT6798D", HardwareType.SuperIO, onGpu: false));

    [Fact]
    public void Датчик_видеокарты_всегда_её_собственный()
    {
        // Even if the name looks like a pump: physically it is a fan on the card.
        Assert.Equal(FanRole.Gpu,
            FanClassifier.Classify("Pump", "NVIDIA GeForce RTX 5070 Ti", HardwareType.GpuNvidia, onGpu: true));
    }
}
