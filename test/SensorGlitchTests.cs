//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using SystemSpinnerX64.Monitoring;
using Xunit;

namespace SystemSpinnerX64.Tests;

/// <summary>Readings the sensors give that no hardware could: they must not reach the panel.</summary>
public class SensorGlitchTests
{
    [Theory]
    [InlineData(0f)]          // a card at idle stops its fans on purpose
    [InlineData(1001f)]
    [InlineData(3516f)]       // the AIO pump at full
    public void Настоящая_скорость_вентилятора_принимается(float rpm) =>
        Assert.True(HardwareMonitor.IsPlausibleFan(rpm));

    [Theory]
    [InlineData(322581f)]     // what the log caught as a card's fans stopped
    [InlineData(984144f)]
    [InlineData(-1f)]
    public void Выброс_тахометра_отбрасывается(float rpm) =>
        Assert.False(HardwareMonitor.IsPlausibleFan(rpm));
}
