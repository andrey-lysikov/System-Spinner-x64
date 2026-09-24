//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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

// ByIp is false while the IP lookup has not succeeded yet, so the latitude is a guess.
internal sealed record Location(double Latitude, double Longitude, string Source, bool ByIp);

// Latitude comes from the IP address, longitude from the system time zone: a person sets the
// zone, while an IP may point at a VPN exit. If both agree, longitude uses IP too.
internal static class Geo
{
    // Moscow, when nothing else works.
    private const double FallbackLatitude = 55.76;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

    // Where the sun is taken to be before the network has answered: the zone gives the
    // longitude, and the latitude is either the last one found or a guess.
    public static Location Guess(Location? previous = null)
    {
        (string zoneName, double lonFromTz) = Zone();

        return previous is { ByIp: true }
            ? new Location(previous.Latitude, lonFromTz, $"{zoneName}, earlier IP", ByIp: true)
            : new Location(FallbackLatitude, lonFromTz, $"{zoneName}, default latitude", ByIp: false);
    }

    public static async Task<Location> ResolveAsync(Location? previous, CancellationToken ct)
    {
        (string zoneName, double lonFromTz) = Zone();

        (double Latitude, double Longitude)? byIp = await FromIpAsync(ct).ConfigureAwait(false);
        if (byIp is null) return Guess(previous);

        // Half a zone: beyond that the gap can no longer be explained by sitting near one edge
        // of the time zone.
        bool agree = Math.Abs(byIp.Value.Longitude - lonFromTz) <= 15.0;

        if (!agree)
            Log.Info($"sky: IP gives longitude {byIp.Value.Longitude:F2}, time zone \"{zoneName}\" " +
                     $"gives {lonFromTz:F2}; the time zone wins");

        return new Location(
            byIp.Value.Latitude,
            agree ? byIp.Value.Longitude : lonFromTz,
            agree ? "IP" : $"latitude by IP, longitude by {zoneName}",
            ByIp: true);
    }

    public static string Describe(Location l) => string.Create(CultureInfo.InvariantCulture,
        $"{l.Latitude:F2}, {l.Longitude:F2} ({l.Source})");

    // The system zone, by its IANA name, and the longitude its offset stands for.
    private static (string Name, double Longitude) Zone()
    {
        TimeZoneInfo local = TimeZoneInfo.Local;
        string name = TimeZoneInfo.TryConvertWindowsIdToIanaId(local.Id, out string? iana) ? iana : local.Id;
        return (name, Math.Clamp(local.GetUtcOffset(DateTime.UtcNow).TotalHours * 15.0, -180, 180));
    }

    private static async Task<(double Latitude, double Longitude)?> FromIpAsync(CancellationToken ct)
    {
        try
        {
            const string url = "http://ip-api.com/json/?fields=status,lat,lon";
            using JsonDocument doc = JsonDocument.Parse(await Http.GetStringAsync(url, ct).ConfigureAwait(false));
            JsonElement root = doc.RootElement;

            if (root.GetProperty("status").GetString() != "success") return null;

            double lat = root.GetProperty("lat").GetDouble();
            double lon = root.GetProperty("lon").GetDouble();
            if (Math.Abs(lat) > 90 || Math.Abs(lon) > 180) return null;

            return (lat, lon);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Log.Info($"sky: IP geolocation unavailable — {ex.Message}");
            return null;
        }
    }
}

// Where the sun and the moon stand for this machine. Set up by the spinner settings and shared
// with the lighting: both need the same place, and it is looked up over the network only once.
internal static class Sky
{
    private static readonly object Gate = new();

    private static SpinnerConfig _cfg = new();
    private static Location? _location;
    private static Task? _resolving;
    private static DateTime _lastAttempt = DateTime.MinValue;

    // Whether an IP lookup has finished at all, found or not. Only the first one is waited for.
    private static bool _lookedUp;

    private static WeatherReport? _report;
    private static DateTime _reportStamp = DateTime.MinValue;
    private static DateTime _weatherAttempt = DateTime.MinValue;
    private static Task<WeatherReport?>? _weatherFetch;

    // The thresholds and the time zone, from [Spinner].
    public static SpinnerConfig Config => _cfg;

    public static void Configure(SpinnerConfig cfg) => _cfg = cfg;

    public static Location Location
    {
        get
        {
            lock (Gate) return _location ??= Geo.Guess();
        }
    }

    // The weather is kept here for whoever needs it, and asked for by nobody else: the lighting
    // when it is on and the evening is near, the Sun & Moon spinner while it is the spinner. Both
    // share one answer, and together they ask no more often than WeatherRefresh.

    // Whether an answer, or a failed try, is recent enough that asking again would be too soon.
    public static bool WeatherChecked
    {
        get
        {
            lock (Gate)
                return _weatherFetch is not { IsCompleted: false } &&
                       DateTime.UtcNow - _weatherAttempt < AppParameters.Aura.WeatherRefresh;
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
                return _report is not null && DateTime.UtcNow - _reportStamp < AppParameters.Aura.SkyWeatherStale
                    ? _report.Weather
                    : SkyWeather.Clear;
        }
    }

    // The weather: the one known while it is fresh, the request already on its way, or a new one.
    // A failed request is not repeated sooner either, and leaves the last answer in place.
    public static Task<WeatherReport?> WeatherAsync()
    {
        lock (Gate)
        {
            if (_weatherFetch is { IsCompleted: false }) return _weatherFetch;
            if (DateTime.UtcNow - _weatherAttempt < AppParameters.Aura.WeatherRefresh) return Task.FromResult(_report);

            _weatherAttempt = DateTime.UtcNow;
            Location place = _location ??= Geo.Guess();

            return _weatherFetch = Task.Run(async () =>
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
            });
        }
    }

    // For the Sun & Moon spinner. The first IP lookup is waited for — a few seconds more than the
    // weather for the time zone's guess. Later ones are not: while the IP service fails, a new
    // lookup starts every minute, and waiting on each would mean no weather at all.
    public static void EnsureWeather()
    {
        lock (Gate)
        {
            if (!_lookedUp && _resolving is { IsCompleted: false }) return;
        }

        _ = WeatherAsync();
    }

    public static double Elevation(DateTime utc)
    {
        Location l = Location;
        return Sun.Elevation(utc, l.Latitude, l.Longitude);
    }

    // Starts the IP lookup when the latitude is still a guess. At boot the network is often not
    // up yet, so a failed attempt is retried, though not more often than the refresh period.
    public static void EnsureResolved()
    {
        lock (Gate)
        {
            if (_location is { ByIp: true }) return;
            if (_resolving is { IsCompleted: false }) return;
            if (DateTime.UtcNow - _lastAttempt < AppParameters.Aura.LocationRetry) return;

            _lastAttempt = DateTime.UtcNow;
            Location? previous = _location;

            _resolving = Task.Run(async () =>
            {
                Location found = await Geo.ResolveAsync(previous, CancellationToken.None).ConfigureAwait(false);
                lock (Gate)
                {
                    _location = found;
                    _lookedUp = true;
                }
                Log.Info($"sky: location {Geo.Describe(found)}");
            });
        }
    }
}
