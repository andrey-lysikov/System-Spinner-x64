//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using SystemSpinnerX64.Lighting;
using Xunit;

namespace SystemSpinnerX64.Tests;

/// <summary>The ASRock Polychrome config table and colour stream, as OpenRGB reads and sends them.</summary>
public class AsrockTests
{
    [Fact]
    public void Зоны_берутся_из_таблицы_по_маске_присутствия()
    {
        // RGB 1, ARGB 2 with 60 LEDs and the PCH; 0x1E marks a zone that is not there.
        byte[] counts = [1, 0x1E, 0x1E, 60, 1, 0x1E, 0x1E, 0x1E];
        byte present = 0b0001_1001;    // RGB 1, ARGB 2, PCH
        byte swap = 0b0001_0000;       // PCH is wired R-G swapped

        var zones = PolychromeDevice.ZonesFrom(counts, swap, present);

        Assert.Equal(
            [new PolychromeZone(0, 1, false, false), new PolychromeZone(3, 60, true, false), new PolychromeZone(4, 1, false, true)],
            zones);
    }

    [Fact]
    public void Поток_идёт_подряд_по_зонам_GRB_и_с_запасом()
    {
        PolychromeZone[] zones = [new(0, 1, false, false), new(3, 2, true, false), new(4, 1, false, true)];
        var frame = new byte[PolychromeDevice.FrameLength(zones)];

        PolychromeDevice.FillFrame(frame, zones, new Rgb(1, 2, 3));

        Assert.Equal((1 + PolychromeDevice.AddressableMax + 1 + 8) * 3, frame.Length);
        Assert.Equal(new byte[] { 2, 1, 3, 2, 1, 3, 2, 1, 3, 1, 2, 3, 0 }, frame[..13]);
    }
}
