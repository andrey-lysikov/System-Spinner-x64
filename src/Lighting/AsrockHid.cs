//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SystemSpinnerX64.Diagnostics;

namespace SystemSpinnerX64.Lighting;

// One header or group of board LEDs on a Polychrome controller, as its config table gives it.
internal sealed record PolychromeZone(int Index, int LedCount, bool Addressable, bool RgSwap);

// The ASRock Polychrome USB controller on the motherboard, spoken to the way OpenRGB does: 65-byte
// reports, each one answered. The LED count of each header lives in the controller itself, set by
// ASRock's own software, and the colours are streamed to all of them at once.
internal sealed class PolychromeDevice : ILightDevice
{
    private const ushort AsrockVendor = 0x26CE;
    private static readonly ushort[] Products = [0x01A2, 0x01A6];

    private const int ReportLength = 65;

    private const byte CmdSetZone = 0x10;
    private const byte CmdReadHeader = 0x14;
    private const byte CmdWriteHeader = 0x15;

    private const byte CfgPresent = 0x01;
    private const byte CfgLedCount = 0x02;
    private const byte CfgRgSwap = 0x03;

    private const byte ModeDirect = 0x0F;
    private const byte SpeedDefault = 0xE0;

    // The first stream packet and the ones after it.
    private const byte ChunkFirst = 0xE3;
    private const byte ChunkNext = 0xE4;

    // A header's count when nothing is there.
    private const byte ZoneUnavailable = 0x1E;
    internal const int ZoneCount = 8;

    // The stream is sized as if every addressable header carried this many LEDs.
    internal const int AddressableMax = 100;

    // A reply comes back without the zero report id, one byte further on than hidapi shows it.
    private const int ReplyData = 5;

    private readonly FileStream _stream;
    private readonly byte[] _packet = new byte[ReportLength];
    private readonly byte _savedRgSwap;
    private readonly byte[] _frame;

    public string Path { get; }
    public IReadOnlyList<PolychromeZone> Zones { get; }

    private PolychromeDevice(string path, FileStream stream, IReadOnlyList<PolychromeZone> zones, byte rgSwap)
    {
        Path = path;
        _stream = stream;
        Zones = zones;
        _savedRgSwap = rgSwap;
        _frame = new byte[FrameLength(zones)];
    }

    public static PolychromeDevice? Open()
    {
        foreach (string path in Hid.Paths())
        {
            if (!Products.Any(pid => Hid.Is(path, AsrockVendor, pid))) continue;
            if (Hid.Caps(path)?.OutputReportLength != ReportLength) continue;

            FileStream? stream = null;
            try
            {
                stream = Hid.Open(path);

                byte[]? counts = ReadHeader(stream, CfgLedCount);
                byte[]? swap = ReadHeader(stream, CfgRgSwap);
                byte[]? present = ReadHeader(stream, CfgPresent);

                if (counts is null || swap is null || present is null)
                {
                    stream.Dispose();
                    continue;
                }

                var zones = ZonesFrom(counts.AsSpan(ReplyData, ZoneCount), swap[ReplyData], present[ReplyData]);
                if (zones.Count == 0)
                {
                    stream.Dispose();
                    Log.Info($"polychrome: {path} has no zones");
                    continue;
                }

                return new PolychromeDevice(path, stream, zones, swap[ReplyData]);
            }
            catch (Exception ex)
            {
                stream?.Dispose();
                Log.Info($"polychrome: {path} did not answer — {ex.Message}");
            }
        }

        return null;
    }

    // Zones the controller marks present and has LEDs on. The addressable ones are the two ARGB
    // headers and the third that shares its place with the audio LEDs.
    internal static List<PolychromeZone> ZonesFrom(ReadOnlySpan<byte> counts, byte rgSwap, byte present)
    {
        var zones = new List<PolychromeZone>();

        for (int i = 0; i < ZoneCount; i++)
        {
            if (counts[i] == ZoneUnavailable || ((present >> i) & 1) == 0) continue;

            bool addressable = i is 2 or 3 or 7;
            zones.Add(new PolychromeZone(i, counts[i], addressable, ((rgSwap >> i) & 1) != 0));
        }

        return zones;
    }

    // The stream carries every zone's LEDs one after another, padded to the most the controller
    // could hold plus eight, as OpenRGB sends it.
    internal static int FrameLength(IReadOnlyList<PolychromeZone> zones) =>
        (zones.Sum(z => z.Addressable ? AddressableMax : z.LedCount) + 8) * 3;

    internal static void FillFrame(byte[] frame, IReadOnlyList<PolychromeZone> zones, Rgb color)
    {
        Array.Clear(frame);

        int at = 0;
        foreach (PolychromeZone zone in zones)
        {
            // The controller swaps red and green on some zones in hardware; in direct mode that is
            // switched off, so it is done here instead.
            (byte a, byte b) = zone.RgSwap ? (color.R, color.G) : (color.G, color.R);
            for (int i = 0; i < zone.LedCount && at + 3 <= frame.Length; i++)
            {
                frame[at++] = a;
                frame[at++] = b;
                frame[at++] = color.B;
            }
        }
    }

    public string Technology => "Polychrome";

    public string Describe() =>
        "ASRock Polychrome: " + string.Join(", ", Zones.Select(z => $"zone {z.Index} {z.LedCount} LEDs"));

    // Every zone to direct mode, and the hardware red-green swap off: with it on the LEDs flash on
    // every frame. Nothing is committed to the controller's memory.
    public void TakeOver()
    {
        WriteHeader(CfgRgSwap, 0);

        foreach (PolychromeZone zone in Zones)
        {
            Array.Clear(_packet);
            _packet[1] = CmdSetZone;
            _packet[3] = (byte)zone.Index;
            _packet[4] = ModeDirect;
            _packet[8] = SpeedDefault;
            _packet[9] = 0xFF;
            Send();
        }
    }

    public void Show(Rgb color)
    {
        FillFrame(_frame, Zones, color);

        int sent = 0;
        bool first = true;
        while (sent < _frame.Length)
        {
            int start = first ? 9 : 5;
            int room = first ? 54 : 57;
            int n = Math.Min(room, _frame.Length - sent);

            Array.Clear(_packet);
            _packet[1] = CmdSetZone;
            _packet[3] = 0xFF;
            _packet[4] = first ? ChunkFirst : ChunkNext;
            if (first) _packet[7] = 0xFF;
            _packet[0x40] = 0x65;
            Array.Copy(_frame, sent, _packet, start, n);
            Send();

            sent += n;
            first = false;
        }
    }

    private void WriteHeader(byte cfg, byte value)
    {
        Array.Clear(_packet);
        _packet[1] = CmdWriteHeader;
        _packet[3] = cfg;
        _packet[4] = value;
        Send();
    }

    // Each report is answered; the answers are left for Windows to drop, nothing waits on them.
    private void Send() => _stream.Write(_packet, 0, ReportLength);

    private static byte[]? ReadHeader(FileStream stream, byte cfg)
    {
        var request = new byte[ReportLength];
        request[1] = CmdReadHeader;
        request[3] = cfg;
        stream.Write(request, 0, ReportLength);

        var reply = new byte[ReportLength];
        Task<int> read = stream.ReadAsync(reply, 0, ReportLength);
        return read.Wait(TimeSpan.FromSeconds(1)) && read.Result > ReplyData ? reply : null;
    }

    // The swap the controller had goes back, so its own effects show the right colours again.
    public void Dispose()
    {
        try
        {
            WriteHeader(CfgRgSwap, _savedRgSwap);
        }
        catch (Exception ex)
        {
            Log.Info($"polychrome: the colour order was not restored — {ex.Message}");
        }

        _stream.Dispose();
    }
}
