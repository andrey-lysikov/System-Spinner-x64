//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using SystemSpinnerX64.Monitoring;
using LibreHardwareMonitor.Hardware;
using Xunit;

namespace SystemSpinnerX64.Tests;

/// <summary>Telling graphics in the processor from a card: a mistake shows a dash for the card's
/// temperature, or hides the memory of a real one.</summary>
public class GpuKindTests
{
    [Fact]
    public void Встроенная_графика_AMD_по_слову_драйвера() =>
        Assert.True(GpuKind.IsIntegrated(HardwareType.GpuAmd, "/gpu-amd/3", index => index == 3));

    [Fact]
    public void Дискретная_карта_AMD_по_слову_драйвера() =>
        Assert.False(GpuKind.IsIntegrated(HardwareType.GpuAmd, "/gpu-amd/0", _ => false));

    [Fact]
    public void Без_ответа_драйвера_AMD_считается_картой() =>
        Assert.False(GpuKind.IsIntegrated(HardwareType.GpuAmd, "/gpu-amd/0", _ => null));

    [Theory]
    [InlineData("/gpu-amd/0", 0)]
    [InlineData("/gpu-amd/12", 12)]
    public void Индекс_ADL_берётся_из_идентификатора(string identifier, int expected) =>
        Assert.Equal(expected, GpuKind.AdlIndex(identifier));

    [Theory]
    [InlineData("/gpu-nvidia/0")]
    [InlineData("/gpu-amd/")]
    [InlineData("/gpu-amd/x")]
    public void Чужой_идентификатор_индекса_не_даёт(string identifier) =>
        Assert.Null(GpuKind.AdlIndex(identifier));

    [Fact]
    public void Встроенная_графика_Intel_узнаётся_по_идентификатору() =>
        Assert.True(GpuKind.IsIntegrated(HardwareType.GpuIntel,
            @"/gpu-intel-integrated/\\?\pci#ven_8086&dev_7d67", _ => null));

    [Fact]
    public void Arc_как_отдельная_карта_не_считается_встроенной() =>
        Assert.False(GpuKind.IsIntegrated(HardwareType.GpuIntel, "/gpu-intel/0", _ => true));

    [Fact]
    public void NVIDIA_всегда_отдельная_карта() =>
        Assert.False(GpuKind.IsIntegrated(HardwareType.GpuNvidia, "/gpu-nvidia/0", _ => true));
}
