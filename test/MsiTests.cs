//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System.Linq;
using SystemSpinnerX64.Lighting;
using Xunit;

namespace SystemSpinnerX64.Tests;

/// <summary>The MSI Mystic Light reports, as OpenRGB lays them out.</summary>
public class MsiTests
{
    [Fact]
    public void Все_зоны_статичные_в_одном_цвете_без_сохранения()
    {
        var packet = new byte[MysticLightDevice.ZonesReportLength];
        packet[184] = 1;                // a save left over from whatever wrote it last
        packet[74 + 8] = 0x31;          // the board LEDs syncing both pipes and themselves

        MysticLightDevice.ZonesFrame(packet, new Rgb(1, 2, 3));

        Assert.Equal(0x52, packet[0]);
        foreach (int at in MysticLightDevice.ZoneOffsets)
        {
            Assert.Equal(new byte[] { 1, 1, 2, 3, 10 << 2 }, packet[at..(at + 5)]);
            Assert.Equal(0x80, packet[at + 8] & 0x80);
        }

        Assert.Equal(0x81, packet[74 + 8]);
        Assert.Equal(0, packet[184]);
        Assert.Equal(0, packet[53]);    // the Corsair header is not touched
    }

    [Fact]
    public void Разъём_новых_плат_в_отдельном_отчёте()
    {
        var packet = new byte[MysticLightDevice.HeaderReportLength];
        MysticLightDevice.HeaderFrame(packet, 0x04, 2, count: 240, new Rgb(1, 2, 3));

        Assert.Equal(727, packet.Length);
        Assert.Equal(new byte[] { 0x51, 0x09, 0x04, 0x02, 0, 0, 240, 1, 2, 3 }, packet[..10]);
        Assert.Equal(new byte[] { 1, 2, 3 }, packet[^3..]);
    }

    [Fact]
    public void Настройка_новых_плат_включает_только_адресные_разъёмы()
    {
        byte[] setup = MysticLightDevice.SetupReport();

        Assert.Equal(290, setup.Length);
        Assert.Equal(0x50, setup[0]);
        Assert.Equal(0x78, setup[16]);                  // JARGB 1 on
        Assert.Equal(0x15, setup[4 * 16 - 1]);          // JAF on
        Assert.True(setup[(1 + 4 * 16)..(1 + 17 * 16)].Where((b, i) => i % 16 != 15).All(b => b == 0));
        Assert.Equal(0x95, setup[17 * 16 + 15]);        // the last row as it is
        Assert.Equal(0, setup[289]);
    }

    [Fact]
    public void Платы_до_2020_года_не_берутся()
    {
        Assert.DoesNotContain((ushort)0x7C37, MysticLightDevice.Products);    // X570 Gaming Plus, 2019
        Assert.Contains((ushort)0x7C91, MysticLightDevice.Products);          // B550 Tomahawk, 2020
        Assert.Equal(MysticLightDevice.Products.Length, MysticLightDevice.Products.Distinct().Count());
    }
}
