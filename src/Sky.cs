//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Geolocation;
using SystemSpinnerX64.Configuration;
using SystemSpinnerX64.Diagnostics;

namespace SystemSpinnerX64.Spinner;

internal static class Sun
{
    private static double Rad(double deg) => deg * Math.PI / 180.0;

    // Sun elevation in degrees, NOAA algorithm. Driven by UTC: the time zone has no bearing on
    // where the sun is, only the coordinates do.
    public static double Elevation(DateTime utc, double latDeg, double lonDeg)
    {
        // days since the J2000.0 epoch
        double n = (utc - new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc)).TotalDays;

        double meanLongitude = 280.460 + 0.9856474 * n;
        double meanAnomaly = Rad(357.528 + 0.9856003 * n);

        double eclipticLon = Rad(meanLongitude
                                 + 1.915 * Math.Sin(meanAnomaly)
                                 + 0.020 * Math.Sin(2 * meanAnomaly));
        double obliquity = Rad(23.439 - 0.0000004 * n);

        double declination = Math.Asin(Math.Sin(obliquity) * Math.Sin(eclipticLon));
        double rightAscension = Math.Atan2(Math.Cos(obliquity) * Math.Sin(eclipticLon),
                                           Math.Cos(eclipticLon)) * 180.0 / Math.PI;

        double gmst = (18.697374558 + 24.06570982441908 * n) % 24.0;
        if (gmst < 0) gmst += 24.0;

        double localSidereal = gmst * 15.0 + lonDeg;          // in degrees
        double hourAngle = Rad(localSidereal - rightAscension);

        double lat = Rad(latDeg);
        double sinAlt = Math.Sin(lat) * Math.Sin(declination)
                      + Math.Cos(lat) * Math.Cos(declination) * Math.Cos(hourAngle);

        return Math.Asin(Math.Clamp(sinAlt, -1.0, 1.0)) * 180.0 / Math.PI;
    }

    // The sun when the place is not known: the local clock alone, up at 7, highest at 13 and down
    // at 19, forty degrees at noon. No season and no latitude, and nothing to look up.
    public static double ClockElevation(DateTime local) =>
        40.0 * Math.Cos(2 * Math.PI * (local.TimeOfDay.TotalHours - 13.0) / 24.0);

    // Where the sun sits between the two thresholds: 0 at the dimming point, 1 once it is low
    // enough for full brightness.
    public static double Factor(double elevation, SpinnerConfig sky) =>
        SmoothStep((sky.DimAbove - elevation) / (sky.DimAbove - sky.FullBelow));

    public static double SmoothStep(double x)
    {
        x = Math.Clamp(x, 0.0, 1.0);
        return x * x * (3 - 2 * x);
    }
}

// Moon phase from the mean synodic month. Good to a few hours, which is far more than
// a sixteen-pixel icon can show.
internal static class Moon
{
    private static readonly DateTime KnownNewMoon = new(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);
    private const double SynodicMonth = 29.530588853;

    // 0 and 1 are new moon, 0.5 is full. Waxing below 0.5, waning above.
    public static double Phase(DateTime utc)
    {
        double days = (utc - KnownNewMoon).TotalDays % SynodicMonth;
        if (days < 0) days += SynodicMonth;
        return days / SynodicMonth;
    }

    // Fraction of the disc that is lit, 0 at new moon and 1 at full.
    public static double Illumination(double phase) =>
        (1.0 - Math.Cos(2 * Math.PI * phase)) / 2.0;

    // Ecliptic latitude from the mean argument of latitude. The orbit is tilted about 5.13°, and
    // only near zero can the Earth's shadow reach the Moon.
    public static double Latitude(DateTime utc)
    {
        double d = (utc - new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc)).TotalDays;
        double argument = (93.272 + 13.229350 * d) % 360.0;
        return 5.128 * Math.Sin(argument * Math.PI / 180.0);
    }

    // A total lunar eclipse turns the Moon red, and it needs a full Moon sitting close to a node.
    // Roughly a couple of nights a year.
    public static bool IsBlood(DateTime utc) =>
        Illumination(Phase(utc)) > 0.985 && Math.Abs(Latitude(utc)) < 0.9;
}

// What the sky looks like overhead, coarse enough for a picture.
internal enum SkyWeather
{
    Clear,
    Cloudy,
    Rainy
}

// One answer from a weather service. Cloud cover is 0 for a clear sky and 1 for heavy overcast;
// the source says where it came from, for the log.
internal sealed record WeatherReport(double CloudCover, SkyWeather Weather, string Source);

// The weather services, in order: Open-Meteo, and ProjectEOL when it does not answer — from some
// networks api.open-meteo.com is out of reach while the rest of the internet is not.
internal static class WeatherSources
{
    // Fifteen seconds for each: long enough for a slow answer, short enough that an unreachable
    // first service does not hold the second one back for long.
    internal static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    // From half the sky covered a cloud is drawn.
    internal const double CloudyFrom = 0.5;

    public static async Task<WeatherReport?> FetchAsync(Location l, CancellationToken ct) =>
        await OpenMeteo.FetchAsync(l, ct).ConfigureAwait(false)
        ?? await ProjectEol.FetchAsync(l, ct).ConfigureAwait(false);
}

// Open-Meteo: free and without a key. One request carries what both the lighting and the Sun &
// Moon spinner need.
internal static class OpenMeteo
{
    // WMO weather interpretation codes, as Open-Meteo reports them: 2 and 3 are partly cloudy and
    // overcast, 45 and 48 fog, 51 and up drizzle, rain, snow, showers and thunderstorms.
    public static SkyWeather FromCode(int code) => code switch
    {
        2 or 3 or 45 or 48 => SkyWeather.Cloudy,
        >= 51 => SkyWeather.Rainy,
        _ => SkyWeather.Clear
    };

    // A cloud is drawn from half the sky covered whatever the code says: Open-Meteo reports
    // a "mainly clear" 1 under a good deal of cloud.
    public static SkyWeather Classify(int code, double cloudCover)
    {
        SkyWeather weather = FromCode(code);
        return weather == SkyWeather.Clear && cloudCover >= WeatherSources.CloudyFrom ? SkyWeather.Cloudy : weather;
    }

    // Cloud cover directly, not derived from radiation: at high latitudes a clear sky never
    // reaches the radiation of a tropical noon.
    public static string Url(Location l) => string.Create(CultureInfo.InvariantCulture,
        $"https://api.open-meteo.com/v1/forecast?latitude={l.Latitude:F2}&longitude={l.Longitude:F2}&current=cloud_cover,weather_code");

    // Null for an answer that is not one.
    public static WeatherReport? Parse(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement current = doc.RootElement.GetProperty("current");

            double cover = Math.Clamp(current.GetProperty("cloud_cover").GetDouble() / 100.0, 0, 1);
            int code = current.GetProperty("weather_code").GetInt32();

            return new WeatherReport(cover, Classify(code, cover), $"Open-Meteo, code {code}");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    public static async Task<WeatherReport?> FetchAsync(Location l, CancellationToken ct)
    {
        try
        {
            return Parse(await WeatherSources.Http.GetStringAsync(Url(l), ct).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Log.Info($"sky: Open-Meteo unavailable — {ex.Message}");
            return null;
        }
    }
}

// ProjectEOL: the NOAA GFS forecast, through the keyless endpoint it keeps for MCP clients. One
// JSON-RPC call to its forecast tool for the current hour, no session needed.
internal static class ProjectEol
{
    public const string Url = "https://weatherapi.projecteol.ru/mcp/";

    private const string Cloud = "surface.cloud_area_fraction";
    private const string Precipitation = "surface.precipitation_flux";

    // Rain from a tenth of a millimetre an hour: below that the model's drizzle is noise.
    private const double RainFrom = 0.1 / 3600;   // kg m-2 s-1

    public static string Request(Location l, DateTime utc) => JsonSerializer.Serialize(new
    {
        jsonrpc = "2.0",
        id = 1,
        method = "tools/call",
        @params = new
        {
            name = "get_weather_forecast",
            arguments = new
            {
                latitude = Math.Round(l.Latitude, 2),
                longitude = Math.Round(l.Longitude, 2),
                start = new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc)
                    .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                hours = 1,
                parameters = new[] { Cloud, Precipitation }
            }
        }
    });

    public static SkyWeather Classify(double cloudCover, double precipitationFlux) =>
        precipitationFlux >= RainFrom ? SkyWeather.Rainy
        : cloudCover >= WeatherSources.CloudyFrom ? SkyWeather.Cloudy
        : SkyWeather.Clear;

    // Null for an error, or an answer that is not one.
    public static WeatherReport? Parse(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement result = doc.RootElement.GetProperty("result");
            if (result.TryGetProperty("isError", out JsonElement error) && error.ValueKind == JsonValueKind.True) return null;

            JsonElement values = result.GetProperty("structuredContent").GetProperty("forecast")[0].GetProperty("values");
            double cover = Math.Clamp(values.GetProperty(Cloud).GetProperty("value").GetDouble(), 0, 1);
            double flux = Math.Max(0, values.GetProperty(Precipitation).GetProperty("value").GetDouble());

            return new WeatherReport(cover, Classify(cover, flux),
                string.Create(CultureInfo.InvariantCulture, $"ProjectEOL, {flux * 3600:0.##} mm/h"));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException
                                       or FormatException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    public static async Task<WeatherReport?> FetchAsync(Location l, CancellationToken ct)
    {
        try
        {
            using var content = new StringContent(Request(l, DateTime.UtcNow), System.Text.Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, Url) { Content = content };
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.Accept.ParseAdd("text/event-stream");

            using HttpResponseMessage response = await WeatherSources.Http.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            WeatherReport? report = Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            if (report is null) Log.Info("sky: ProjectEOL gave no forecast");
            return report;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Log.Info($"sky: ProjectEOL unavailable — {ex.Message}");
            return null;
        }
    }
}

// A place Windows location gave: where the machine is, or the default location set in Windows.
internal sealed record Location(double Latitude, double Longitude, string Source);

// Windows location via the CsWinRT projection: Wi-Fi, else the default location from settings.
// Needs "Let desktop apps access your location"; an unpackaged app cannot prompt for it.
internal static class Geo
{
    public static async Task<Location?> FindAsync(CancellationToken ct)
    {
        // Always true past PlatformGuard; here for the platform analyzer, as the projection is
        // marked for Windows 10 and the TFM names no version.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 10240)) return null;

        try
        {
            Geoposition position = await new Geolocator()
                .GetGeopositionAsync(AppParameters.Sky.LocationMaximumAge, AppParameters.Sky.LocationTimeout)
                .AsTask(ct).ConfigureAwait(false);

            BasicGeoposition point = position.Coordinate.Point.Position;
            return new Location(point.Latitude, point.Longitude, string.Create(CultureInfo.InvariantCulture,
                $"Windows location, ±{position.Coordinate.Accuracy:0} m"));
        }
        catch (UnauthorizedAccessException)
        {
            Log.Info("sky: Windows location is off for desktop apps");
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Log.Info($"sky: Windows location unavailable — {ex.Message}");
            return null;
        }
    }

    public static string Describe(Location l) => string.Create(CultureInfo.InvariantCulture,
        $"{l.Latitude:F2}, {l.Longitude:F2} ({l.Source})");
}

// Sun, moon and weather shared by the spinner and the lighting. Without a place: no weather,
// the sun follows the clock, and after the last retry the user is told once per run.
internal static class Sky
{
    private static readonly object Gate = new();

    private static SpinnerConfig _cfg = new();

    private static Location? _location;
    private static Task? _locating;
    private static bool _lookupStarted;
    private static DateTime _nextLookup = DateTime.MinValue;
    private static int _retriesLeft = AppParameters.Sky.LocationMaxRetries;
    private static bool _gaveUp;

    // Whether anyone has asked for the weather, and whether the user has heard there will be none.
    private static bool _weatherWanted;
    private static bool _missingAnnounced;

    private static WeatherReport? _report;
    private static DateTime _reportStamp = DateTime.MinValue;
    private static DateTime _weatherAttempt = DateTime.MinValue;
    private static Task<WeatherReport?>? _weatherFetch;

    // Raised once per run, off the UI thread: the place is not known and the weather was wanted.
    public static event Action? LocationMissing;

    // The thresholds, from [Spinner].
    public static SpinnerConfig Config => _cfg;

    public static void Configure(SpinnerConfig cfg) => _cfg = cfg;

    // Null while the place is not known, and for the rest of the run once the tries are over.
    public static Location? Location
    {
        get
        {
            lock (Gate) return _location;
        }
    }

    // Still looking: an answer may yet come.
    public static bool Locating
    {
        get
        {
            lock (Gate) return _location is null && !_gaveUp;
        }
    }

    // Weather is fetched only for the lighting (evenings) and the Sun & Moon spinner,
    // one shared answer, no more often than WeatherRefresh.

    // Whether an answer, or a failed try, is recent enough that asking again would be too soon.
    // Without a place nothing is on its way, so there is nothing to wait for either.
    public static bool WeatherChecked
    {
        get
        {
            lock (Gate)
                return _location is null ||
                       (_weatherFetch is not { IsCompleted: false } &&
                        DateTime.UtcNow - _weatherAttempt < AppParameters.Sky.WeatherRefresh);
        }
    }

    // The last cloud cover known, however old: what the lighting goes on while a new one comes.
    public static double CloudCover
    {
        get
        {
            lock (Gate) return _report?.CloudCover ?? 0.0;
        }
    }

    // What the Sun & Moon spinner shows overhead: clear until an answer comes, and again once it
    // is too old.
    public static SkyWeather Weather
    {
        get
        {
            lock (Gate)
                return _report is not null && DateTime.UtcNow - _reportStamp < AppParameters.Sky.WeatherStale
                    ? _report.Weather
                    : SkyWeather.Clear;
        }
    }

    // Fresh cached answer, the pending request, or a new one; failures also wait WeatherRefresh.
    // Without a place, none.
    public static Task<WeatherReport?> WeatherAsync()
    {
        bool announce;

        lock (Gate)
        {
            _weatherWanted = true;

            if (_location is { } place)
            {
                if (_weatherFetch is { IsCompleted: false }) return _weatherFetch;
                if (DateTime.UtcNow - _weatherAttempt < AppParameters.Sky.WeatherRefresh) return Task.FromResult(_report);

                _weatherAttempt = DateTime.UtcNow;
                return _weatherFetch = Task.Run(() => FetchWeatherAsync(place));
            }

            announce = TakeAnnouncement();
        }

        if (announce) LocationMissing?.Invoke();
        return Task.FromResult<WeatherReport?>(null);
    }

    private static async Task<WeatherReport?> FetchWeatherAsync(Location place)
    {
        WeatherReport? found = await WeatherSources.FetchAsync(place, CancellationToken.None).ConfigureAwait(false);

        lock (Gate)
        {
            if (found is null) return _report;

            _report = found;
            _reportStamp = DateTime.UtcNow;
        }

        Log.Info($"sky: weather {found.Weather}, cloud cover {found.CloudCover:P0} ({found.Source}) " +
                 $"at {Geo.Describe(place)}");
        return found;
    }

    // For the Sun & Moon spinner. Until the place is known this only marks the weather as wanted:
    // it is fetched the moment the place comes.
    public static void EnsureWeather() => _ = WeatherAsync();

    // By the place when it is known, by the local clock when it is not.
    public static double Elevation(DateTime utc)
    {
        Location? l = Location;
        return l is null
            ? Sun.ClockElevation(TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.Local))
            : Sun.Elevation(utc, l.Latitude, l.Longitude);
    }

    // Asks Windows location, by the same rule as the external address: the first try after a
    // short delay, then MaxRetries more, RetryDelay apart. Called often; does nothing in between.
    public static void EnsureResolved()
    {
        lock (Gate)
        {
            if (_location is not null || _gaveUp) return;
            if (_locating is { IsCompleted: false }) return;
            if (DateTime.UtcNow < _nextLookup) return;

            bool first = !_lookupStarted;
            _lookupStarted = true;

            _locating = Task.Run(async () =>
            {
                if (first) await Task.Delay(AppParameters.Sky.LocationFirstDelay).ConfigureAwait(false);

                Location? found = await Geo.FindAsync(CancellationToken.None).ConfigureAwait(false);

                bool fetch = false, announce = false, gaveUp = false;
                lock (Gate)
                {
                    if (found is not null)
                    {
                        _location = found;
                        fetch = _weatherWanted;
                    }
                    else if (_retriesLeft-- > 0)
                    {
                        _nextLookup = DateTime.UtcNow + AppParameters.Sky.LocationRetryDelay;
                    }
                    else
                    {
                        _gaveUp = gaveUp = true;
                        announce = TakeAnnouncement();
                    }
                }

                if (found is not null)
                    Log.Info($"sky: location {Geo.Describe(found)}");
                else if (gaveUp)
                    Log.Warn("sky: no location — no weather until restart, the sun follows the clock");

                if (fetch) _ = WeatherAsync();
                if (announce) LocationMissing?.Invoke();
            });
        }
    }

    // Under the lock: whether now is the one time to tell the user there is no place.
    private static bool TakeAnnouncement()
    {
        if (!_gaveUp || !_weatherWanted || _missingAnnounced) return false;
        _missingAnnounced = true;
        return true;
    }
}
