using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SystemSpinnerX64.Diagnostics;

namespace SystemSpinnerX64.Monitoring;

// Rate on a network interface. The unit is picked to fit, as in the macOS version.
public readonly record struct Throughput(double BytesPerSecond)
{
    public static readonly Throughput Zero = new(0);

    private const double Kilobyte = 1024.0;

    public double Value => Scaled().Value;

    public string Unit => Scaled().Unit;

    private (double Value, string Unit) Scaled()
    {
        double megabyte = Kilobyte * Kilobyte;
        double gigabyte = megabyte * Kilobyte;

        return BytesPerSecond switch
        {
            >= 1024 * 1024 * 1024 * 1024.0 => (BytesPerSecond / (gigabyte * Kilobyte), "TB/s"),
            >= 1024 * 1024 * 1024.0 => (BytesPerSecond / gigabyte, "GB/s"),
            >= 1024 * 1024.0 => (BytesPerSecond / megabyte, "MB/s"),
            _ => (BytesPerSecond / Kilobyte, "KB/s")
        };
    }

    public string Describe() =>
        Value.ToString(Value >= 100 ? "0" : "0.0", CultureInfo.InvariantCulture) + " " + Unit;
}

// What the network line of the status window shows.
public sealed class NetworkUsage
{
    public string Address { get; init; } = "";
    public Throughput Inbound { get; init; } = Throughput.Zero;
    public Throughput Outbound { get; init; } = Throughput.Zero;

    public static readonly NetworkUsage Empty = new();
}

// Receive and send rates and the address of the machine.
public sealed class NetworkMonitor
{
    // One client for the whole app: each new one takes its own socket, and that lingers for two
    // more minutes after closing — over a day that would add up.
    private static readonly HttpClient Http = new() { Timeout = AppParameters.Network.RequestTimeout };

    // IPv4 first, then IPv6: the service answers over whichever family the request went out on,
    // and a machine with IPv6 alone would otherwise show nothing.
    private static readonly Regex AddressInPage = new(
        @"\b(?<ip>(?:\d{1,3}\.){3}\d{1,3})\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex AddressV6InPage = new(
        @"(?<ip>[0-9A-Fa-f]{0,4}(?::[0-9A-Fa-f]{0,4}){2,7})",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private long _previousIn;
    private long _previousOut;
    private bool _hasBaseline;

    private string _localAddress = "";
    private string _externalAddress = "";
    private bool _lookupRunning;
    private DateTime _lookupAllowedAt = DateTime.MinValue;
    private DateTime _retryAt = DateTime.MinValue;
    private int _retriesLeft = AppParameters.Network.MaxRetries;
    private bool _wasResolving = true;

    public NetworkUsage Usage { get; private set; } = NetworkUsage.Empty;

    // A poll. seconds is the time since the last one.
    public void Update(double seconds, bool resolveExternalAddress)
    {
        (long inBytes, long outBytes, string address) = ReadCounters();

        if (!resolveExternalAddress) _externalAddress = "";

        // The network changed — the old external address is no longer ours.
        if (address != _localAddress)
        {
            _localAddress = address;
            _externalAddress = "";

            // A different network is a different answer — the tries start over.
            _retriesLeft = AppParameters.Network.MaxRetries;
            RequestLookup(resolveExternalAddress);
        }
        else if (resolveExternalAddress && !_wasResolving)
        {
            RequestLookup(true);
        }
        else if (resolveExternalAddress && _externalAddress.Length == 0 &&
                 _retriesLeft > 0 && DateTime.UtcNow >= _retryAt)
        {
            // The last attempt came to nothing — timed out, no route, the service down. A couple
            // of tries later is the only way the address appears without a restart; past that the
            // local address stands and the app stops knocking.
            _retriesLeft--;
            RequestLookup(true);
        }

        _wasResolving = resolveExternalAddress;

        double elapsed = Math.Max(seconds, 0.001);

        // Counters can go backwards: the interface was brought up again and they restarted at zero.
        double inbound = _hasBaseline && inBytes >= _previousIn ? (inBytes - _previousIn) / elapsed : 0;
        double outbound = _hasBaseline && outBytes >= _previousOut ? (outBytes - _previousOut) / elapsed : 0;

        _previousIn = inBytes;
        _previousOut = outBytes;
        _hasBaseline = true;

        Usage = new NetworkUsage
        {
            Address = _externalAddress.Length > 0 ? _externalAddress : _localAddress,
            Inbound = new Throughput(inbound),
            Outbound = new Throughput(outbound)
        };
    }

    private static (long In, long Out, string Address) ReadCounters()
    {
        long inBytes = 0;
        long outBytes = 0;
        string address = "";

        try
        {
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                if (adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;

                IPInterfaceStatistics statistics = adapter.GetIPStatistics();
                inBytes += statistics.BytesReceived;
                outBytes += statistics.BytesSent;

                if (address.Length > 0) continue;

                // The address is taken from the interface with a gateway: virtual bridges and
                // Hyper-V have none, and without this the address of a VM would be reported as ours.
                IPInterfaceProperties properties = adapter.GetIPProperties();
                if (properties.GatewayAddresses.Count == 0) continue;

                address = PickAddress(properties.UnicastAddresses);
            }
        }
        catch (NetworkInformationException ex)
        {
            Log.Error("the network counters were not read", ex);
        }

        return (inBytes, outBytes, address);
    }

    // IPv4 if there is one, otherwise a routable IPv6. Link-local (fe80::) is skipped: it says
    // nothing about where the machine is on the network.
    private static string PickAddress(UnicastIPAddressInformationCollection addresses)
    {
        IPAddress? v4 = addresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address;
        if (v4 is not null) return v4.ToString();

        IPAddress? v6 = addresses
            .Select(a => a.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetworkV6 &&
                                 !a.IsIPv6LinkLocal && !a.IsIPv6SiteLocal && !a.IsIPv6Teredo);

        return v6?.ToString() ?? "";
    }

    private void RequestLookup(bool enabled)
    {
        if (!enabled || _lookupRunning) return;

        _lookupRunning = true;

        // The delay is not politeness towards someone else's service but towards ours: right after
        // a network change the route may not be up yet and the request would be wasted.
        _lookupAllowedAt = DateTime.UtcNow.AddSeconds(AppParameters.Network.LookupDelaySeconds);

        Task.Run(async () =>
        {
            try
            {
                TimeSpan wait = _lookupAllowedAt - DateTime.UtcNow;
                if (wait > TimeSpan.Zero) await Task.Delay(wait);

                string? found = await FetchExternalAddress();
                if (found is { Length: > 0 })
                {
                    // The assignment is atomic and only the poll reads the string — no lock needed.
                    _externalAddress = found;
                    Log.Info($"external address: {found}");
                }
                else
                {
                    _retryAt = DateTime.UtcNow + AppParameters.Network.RetryDelay;
                }
            }
            finally
            {
                _lookupRunning = false;
            }
        });
    }

    private static async Task<string?> FetchExternalAddress()
    {
        try
        {
            string page = await Http.GetStringAsync(AppParameters.Network.ExternalAddressUrl);
            Match match = AddressInPage.Match(page);
            return match.Success ? match.Groups["ip"].Value : null;
        }
        catch (Exception ex)
        {
            // No network or the service is down — the local address remains. Not a failure.
            Log.Warn($"the external address was not looked up: {ex.Message}");
            return null;
        }
    }

    // Extracting the address from the service answer.
    internal static string? ParseAddress(string page)
    {
        Match match = AddressInPage.Match(page);
        if (match.Success) return match.Groups["ip"].Value;

        // IPv6 is only accepted when the framework agrees it is an address: the pattern alone
        // would also match a fragment of markup or a timestamp.
        foreach (Match candidate in AddressV6InPage.Matches(page))
        {
            string text = candidate.Groups["ip"].Value;
            if (IPAddress.TryParse(text, out IPAddress? parsed) &&
                parsed.AddressFamily == AddressFamily.InterNetworkV6)
                return parsed.ToString();
        }

        return null;
    }
}
