//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using SystemSpinnerX64.Lighting;
using Xunit;

namespace SystemSpinnerX64.Tests;

/// <summary>The Gigabyte RGB Fusion 2 reports, as OpenRGB lays them out.</summary>
public class GigabyteTests
{
    [Fact]
    public void Порядок_цветов_берётся_из_калибровки_платы()
    {
        // GRB as the board stores it: blue at 2, green at 0, red at 1.
        Assert.Equal(new ByteOrder(1, 0, 2), ByteOrder.From(0x00_01_00_02));
        Assert.Equal(ByteOrder.Plain, ByteOrder.From(0));
        Assert.Equal(ByteOrder.Plain, ByteOrder.From(0x00_01_01_02));    // not a permutation
    }

    [Fact]
    public void Пакет_ленты_со_смещением_в_байтах()
    {
        var packet = new byte[FusionDevice.ReportLength];
        FusionDevice.StripPacket(packet, 0x59, new ByteOrder(1, 0, 2), start: 19, count: 19, new Rgb(0x10, 0x20, 0x30));

        Assert.Equal(new byte[] { 0xCC, 0x59, 57, 0, 57 }, packet[..5]);
        Assert.Equal(new byte[] { 0x20, 0x10, 0x30 }, packet[5..8]);
        Assert.Equal(new byte[] { 0x20, 0x10, 0x30 }, packet[59..62]);
        Assert.Equal(0, packet[62]);
    }

    [Fact]
    public void Статичный_эффект_на_все_регистры()
    {
        var packet = new byte[FusionDevice.ReportLength];
        FusionDevice.EffectPacket(packet, 0x07FF, new Rgb(0x10, 0x20, 0x30));

        Assert.Equal(new byte[] { 0xCC, 0x20, 0xFF, 0x07, 0, 0 }, packet[..6]);
        Assert.Equal(1, packet[11]);
        Assert.Equal(0xFF, packet[12]);
        Assert.Equal(new byte[] { 0x30, 0x20, 0x10, 0 }, packet[14..18]);
    }

    [Theory]
    [InlineData(20, 0)]
    [InlineData(64, 1)]
    [InlineData(300, 3)]
    [InlineData(1000, 4)]
    public void Длина_ленты_кодом_размера(int leds, byte code) =>
        Assert.Equal(code, FusionDevice.CountCode(leds));

    [Fact]
    public void У_IT5711_шесть_адресных_разъёмов()
    {
        var info = new byte[64];
        var more = new byte[64];

        Assert.Equal(2, FusionDevice.StripsOf(0x5702, info, null).Count);
        Assert.Equal(6, FusionDevice.StripsOf(FusionDevice.It5711, info, more).Count);
        Assert.Single(FusionDevice.StripsOf(FusionDevice.GcUsb, info, null));
    }
}
