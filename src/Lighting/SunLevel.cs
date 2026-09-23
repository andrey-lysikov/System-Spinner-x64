//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SystemSpinnerX64.Configuration;
using SystemSpinnerX64.Diagnostics;
using SystemSpinnerX64.Spinner;

namespace SystemSpinnerX64.Lighting;
// Zero by day, rising towards the ceiling as the sun sets. Below DimAbove cloud cover adds on
// top, so an overcast evening goes dark earlier.
internal sealed class SunLevel
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private DateTime _weatherStamp = DateTime.MinValue;
    private double _cloudFactor;
    private Task<double>? _fetch;

    public double LastElevation { get; private set; }

    // The last level was worked out with cloud cover older than it should be, while a fresh one
    // was being fetched: the next look is worth taking soon.
    public bool CloudStale { get; private set; }

    private bool CloudFresh => DateTime.UtcNow - _weatherStamp < AppParameters.Aura.WeatherRefresh;

    // One request at a time: a look that comes while one is on its way waits for that one.
    private Task<double> CloudFactorAsync(Location loc, CancellationToken ct)
    {
        if (CloudFresh) return Task.FromResult(_cloudFactor);
        if (_fetch is { IsCompleted: false }) return _fetch;

        return _fetch = FetchCloudAsync(loc, ct);
    }

    // 0.0 is clear sky, 1.0 is heavy overcast. Any failure quietly yields 0.
    private async Task<double> FetchCloudAsync(Location loc, CancellationToken ct)
    {
        try
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            string url = "https://api.open-meteo.com/v1/forecast" +
                         $"?latitude={loc.Latitude.ToString(inv)}" +
                         $"&longitude={loc.Longitude.ToString(inv)}" +
                         "&current=cloud_cover";

            // Cloud cover directly, not derived from radiation: at high latitudes a clear sky never
            // reaches the radiation of a tropical noon.
            using JsonDocument doc = JsonDocument.Parse(await Http.GetStringAsync(url, ct).ConfigureAwait(false));
            double cover = doc.RootElement.GetProperty("current").GetProperty("cloud_cover").GetDouble();
            _cloudFactor = Math.Clamp(cover / 100.0, 0, 1);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Log.Info($"aura: cloud cover unavailable — {ex.Message}");
            _cloudFactor = 0.0;
        }

        _weatherStamp = DateTime.UtcNow;
        return _cloudFactor;
    }

    // The ceiling is Brightness from [Aura]; the span the sun ramps over comes from [Spinner].
    // Without waitForWeather the level comes at once, from the cloud cover already known, and
    // a fresh reading is fetched behind it: a click must not wait out a slow weather service.
    public async Task<double> TargetAsync(AuraConfig cfg, bool waitForWeather, CancellationToken ct)
    {
        SpinnerConfig sky = Sky.Config;
        Location loc = Sky.Location;
        double elevation = Sun.Elevation(DateTime.UtcNow, loc.Latitude, loc.Longitude);
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
                cloud = await CloudFactorAsync(loc, ct).ConfigureAwait(false);
            }
            else
            {
                cloud = _cloudFactor;
                CloudStale = true;
                _ = CloudFactorAsync(loc, ct);
            }
        }

        double level = sun + (1.0 - sun) * cloud * cloudWeight * 0.6;
        level = Math.Clamp(level * cfg.Brightness / 100.0, 0, 1);

        // Below this the LEDs show nothing at all, so the lamps are simply dark. The step to the
        // first visible level is still faded by the engine.
        return level < cfg.VisibleFrom / 100.0 ? 0.0 : level;
    }
}
