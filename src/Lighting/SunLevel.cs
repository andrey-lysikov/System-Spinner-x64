//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Threading;
using System.Threading.Tasks;
using SystemSpinnerX64.Configuration;
using SystemSpinnerX64.Spinner;

namespace SystemSpinnerX64.Lighting;
// Zero by day, rising towards the ceiling as the sun sets. Below DimAbove cloud cover adds on
// top, so an overcast evening goes dark earlier.
internal sealed class SunLevel
{
    public double LastElevation { get; private set; }

    // The last level was worked out with cloud cover older than it should be, while a fresh one
    // was being fetched: the next look is worth taking soon.
    public bool CloudStale { get; private set; }

    // The weather is shared with the Sun & Moon spinner: whichever asked last, the answer serves
    // both. A failed request counts as checked, so it is not repeated sooner than the refresh.
    private static bool CloudFresh => Sky.WeatherChecked;

    private static async Task<double> CloudFactorAsync(CancellationToken ct)
    {
        WeatherReport? report = await Sky.WeatherAsync().WaitAsync(ct).ConfigureAwait(false);
        return report?.CloudCover ?? 0.0;
    }

    // Ceiling from [Aura], sun span from [Spinner]. Without waitForWeather, uses the known
    // cloud cover now and refreshes behind it, so a click never waits on the network.
    public async Task<double> TargetAsync(AuraConfig cfg, bool waitForWeather, CancellationToken ct)
    {
        SpinnerConfig sky = Sky.Config;
        double elevation = Sky.Elevation(DateTime.UtcNow);
        LastElevation = elevation;

        double sun = Sun.Factor(elevation, sky);

        // Clouds only add to the sun, never replace it. Their share fades in over the upper half
        // of the band, so there is no step at DimAbove.
        double band = sky.DimAbove - sky.FullBelow;
        double cloudWeight = Sun.SmoothStep((sky.DimAbove - elevation) / (band / 2));
        // Once the sun alone asks for full brightness the sky has nothing left to add, and the
        // weather is not asked for at all.
        bool cloudMatters = cfg.WeatherCloud && cloudWeight > 0 && sun < 0.999;

        double cloud = 0.0;
        CloudStale = false;

        if (cloudMatters)
        {
            if (waitForWeather || CloudFresh)
            {
                cloud = await CloudFactorAsync(ct).ConfigureAwait(false);
            }
            else
            {
                cloud = Sky.CloudCover;
                CloudStale = true;
                _ = Sky.WeatherAsync();
            }
        }

        double level = sun + (1.0 - sun) * cloud * cloudWeight * 0.6;
        level = Math.Clamp(level * cfg.Brightness / 100.0, 0, 1);

        // Below this the LEDs show nothing at all, so the lamps are simply dark. The step to the
        // first visible level is still faded by the engine.
        return level < cfg.VisibleFrom / 100.0 ? 0.0 : level;
    }
}
