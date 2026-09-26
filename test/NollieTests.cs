//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System.Linq;
using SystemSpinnerX64.Lighting;
using Xunit;

namespace SystemSpinnerX64.Tests;

/// <summary>The Nollie reports, as OpenRGB lays them out.</summary>
public class NollieTests
{
    private static NollieModel Model(string name) => NollieDevice.Models.Single(m => m.Name == name);

    [Fact]
    public void Канал_высокоскоростного_Nollie_в_одном_отчёте_GRB()
    {
        var packet = new byte[NollieDevice.HighSpeedReport];
        NollieDevice.HighSpeedPacket(packet, channel: 3, count: 256, new Rgb(0x10, 0x20, 0x30));

        Assert.Equal(0x00, packet[0]);
        Assert.Equal(3, packet[1]);
        Assert.Equal(0, packet[2]);
        Assert.Equal(0x01, packet[3]);    // 256, big-endian
        Assert.Equal(0x00, packet[4]);
        Assert.Equal(new byte[] { 0x20, 0x10, 0x30 }, packet[5..8]);
        Assert.Equal(new byte[] { 0x20, 0x10, 0x30 }, packet[(5 + 3 * 255)..(8 + 3 * 255)]);
        Assert.Equal(0, packet[8 + 3 * 255]);
    }

    [Theory]
    [InlineData(15, 1)]
    [InlineData(31, 2)]
    [InlineData(16, 0)]
    public void Половина_каналов_показывается_по_флагу(int channel, byte flag)
    {
        var packet = new byte[NollieDevice.HighSpeedReport];
        NollieDevice.HighSpeedPacket(packet, channel, count: 1, Rgb.Black);

        Assert.Equal(flag, packet[2]);
    }

    [Fact]
    public void Nollie_8_нумерует_пакеты_по_шесть_на_канал_GRB()
    {
        var packet = new byte[NollieDevice.FullSpeedReport];
        NollieDevice.FullSpeedPacket(packet, Model("Nollie 8CH"), channel: 2, index: 1, count: 21, new Rgb(1, 2, 3));

        Assert.Equal(1 + 2 * 6, packet[1]);
        Assert.Equal(new byte[] { 2, 1, 3 }, packet[2..5]);
        Assert.Equal(new byte[] { 2, 1, 3 }, packet[62..65]);
    }

    [Fact]
    public void Nollie_28_берёт_RGB_и_двадцать_пять_пакетов_на_канал()
    {
        var packet = new byte[NollieDevice.FullSpeedReport];
        NollieDevice.FullSpeedPacket(packet, Model("Nollie 28 L1"), channel: 7, index: 24, count: 5, new Rgb(1, 2, 3));

        Assert.Equal(24 + 7 * 25, packet[1]);
        Assert.Equal(new byte[] { 1, 2, 3 }, packet[2..5]);
        Assert.Equal(0, packet[2 + 3 * 5]);
    }

    [Fact]
    public void Каждая_модель_умещает_свои_каналы_в_отчёт()
    {
        foreach (NollieModel m in NollieDevice.Models)
        {
            if (m.Wire == NollieWire.HighSpeed)
                Assert.True(5 + 3 * m.MaxLeds <= m.ReportLength, m.Name);
            else
            {
                Assert.Equal(0, m.MaxLeds % NollieDevice.LedsPerPacket);
                Assert.True(m.Channels.Length * m.MaxLeds / NollieDevice.LedsPerPacket < 0xFF, m.Name);
            }
        }
    }
}
