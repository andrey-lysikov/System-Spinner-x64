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

// ByIp is false while the IP lookup has not succeeded yet, so the latitude is a guess.
internal sealed record Location(double Latitude, double Longitude, string Source, bool ByIp);

// Latitude comes from the IP address, longitude from the time zone: a person sets the zone,
// while an IP may point at a VPN exit. If both agree, longitude uses IP too.
internal static class Geo
{
    // Moscow, when nothing else works.
    private const double FallbackLatitude = 55.76;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

    // Where the sun is taken to be before the network has answered: the zone gives the
    // longitude, and the latitude is either the last one found or a guess.
    public static Location Guess(string timeZone, Location? previous = null)
    {
        (string zoneName, TimeSpan offset) = ResolveZone(timeZone);
        double lonFromTz = Math.Clamp(offset.TotalHours * 15.0, -180, 180);

        return previous is { ByIp: true }
            ? new Location(previous.Latitude, lonFromTz, $"{zoneName}, earlier IP", ByIp: true)
            : new Location(FallbackLatitude, lonFromTz, $"{zoneName}, default latitude", ByIp: false);
    }

    public static async Task<Location> ResolveAsync(string timeZone, Location? previous, CancellationToken ct)
    {
        (string zoneName, TimeSpan offset) = ResolveZone(timeZone);
        double lonFromTz = Math.Clamp(offset.TotalHours * 15.0, -180, 180);

        (double Latitude, double Longitude)? byIp = await FromIpAsync(ct).ConfigureAwait(false);
        if (byIp is null) return Guess(timeZone, previous);

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

    // Auto means the system zone. Otherwise an IANA id such as Europe/Moscow, a Windows zone
    // name, or a plain UTC offset like +03:00.
    private static (string Name, TimeSpan Offset) ResolveZone(string setting)
    {
        DateTime now = DateTime.UtcNow;

        if (string.IsNullOrWhiteSpace(setting) ||
            setting.Trim().Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            TimeZoneInfo local = TimeZoneInfo.Local;
            return (ToIana(local.Id), local.GetUtcOffset(now));
        }

        string value = setting.Trim().Trim('"');

        try
        {
            TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(value);
            return (zone.Id, zone.GetUtcOffset(now));
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            if (TryParseOffset(value, out TimeSpan offset)) return (value, offset);

            Log.Warn($"[Spinner] TimeZone: unknown zone \"{value}\" — the system one is used");
            TimeZoneInfo local = TimeZoneInfo.Local;
            return (ToIana(local.Id), local.GetUtcOffset(now));
        }
    }

    private static string ToIana(string windowsId) =>
        TimeZoneInfo.TryConvertWindowsIdToIanaId(windowsId, out string? iana) ? iana : windowsId;

    internal static bool TryParseOffset(string value, out TimeSpan offset)
    {
        offset = TimeSpan.Zero;

        string text = value.StartsWith("UTC", StringComparison.OrdinalIgnoreCase) ? value[3..] : value;
        text = text.Trim();
        if (text.Length == 0) return true;

        int sign = text[0] == '-' ? -1 : 1;
        if (text[0] is '+' or '-') text = text[1..];

        if (!TimeSpan.TryParse(text.Contains(':') ? text : $"{text}:00",
                               CultureInfo.InvariantCulture, out TimeSpan parsed))
            return false;

        offset = sign < 0 ? -parsed : parsed;
        return Math.Abs(offset.TotalHours) <= 14;
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

    // The thresholds and the time zone, from [Spinner].
    public static SpinnerConfig Config => _cfg;

    public static void Configure(SpinnerConfig cfg) => _cfg = cfg;

    public static Location Location
    {
        get
        {
            lock (Gate) return _location ??= Geo.Guess(_cfg.TimeZone);
        }
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
            string zone = _cfg.TimeZone;

            _resolving = Task.Run(async () =>
            {
                Location found = await Geo.ResolveAsync(zone, previous, CancellationToken.None).ConfigureAwait(false);
                lock (Gate) _location = found;
                Log.Info($"sky: location {Geo.Describe(found)}");
            });
        }
    }
}
