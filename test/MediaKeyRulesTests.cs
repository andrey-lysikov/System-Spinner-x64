//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using SystemSpinnerX64.Devices;
using Xunit;

namespace SystemSpinnerX64.Tests;

public class MediaKeyRulesTests
{
    [Theory]
    [InlineData(true, false, true)]    // a monitor over DDC: ours
    [InlineData(false, false, false)]  // nothing over DDC: Windows does it and says so
    [InlineData(false, true, true)]    // asked for in every case
    [InlineData(true, true, true)]
    public void Ключ_наш_только_при_DDC_или_по_требованию(bool ddc, bool always, bool takes)
    {
        Assert.Equal(takes, MediaKeyRules.Takes(ddc, always));
    }

    [Fact]
    public void Яркость_без_DDC_уходит_системе()
    {
        Assert.Equal(MediaKeyResult.PassThrough,
                     MediaKeyRules.Brightness(drivesOverDdc: false, alwaysCustomOsd: false,
                                              targetFound: true));
    }

    [Fact]
    public void Яркость_по_DDC_наша()
    {
        Assert.Equal(MediaKeyResult.Consumed,
                     MediaKeyRules.Brightness(drivesOverDdc: true, alwaysCustomOsd: false,
                                              targetFound: true));
    }

    [Fact]
    public void Яркость_без_экрана_для_регулировки()
    {
        // Nothing found to move: the key goes back unless the custom OSD was demanded.
        Assert.Equal(MediaKeyResult.PassThrough,
                     MediaKeyRules.Brightness(drivesOverDdc: true, alwaysCustomOsd: false,
                                              targetFound: false));

        Assert.Equal(MediaKeyResult.Consumed,
                     MediaKeyRules.Brightness(drivesOverDdc: true, alwaysCustomOsd: true,
                                              targetFound: false));
    }

    [Theory]
    [InlineData(true, false, MediaKeyResult.Consumed)]
    [InlineData(false, false, MediaKeyResult.PassThrough)]
    [InlineData(false, true, MediaKeyResult.Consumed)]
    public void Громкость_по_тому_сдвинулось_ли_что_нибудь(bool moved, bool always, MediaKeyResult expected)
    {
        Assert.Equal(expected, MediaKeyRules.Volume(moved, always));
    }
}
