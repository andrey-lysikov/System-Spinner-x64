//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SystemSpinnerX64.Diagnostics;

namespace SystemSpinnerX64.Lighting;

// One run of LEDs the controller drives as a whole: the board itself with its 12 V headers, or
// one addressable header. The effect channel switches the mode, the direct one takes colours.
internal sealed record AuraZone(string Name, byte EffectChannel, byte DirectChannel, int LedCount);

// The ASUS Aura USB controller on the motherboard, spoken to the way Armoury Crate and OpenRGB
// do: 65-byte reports starting 0xEC on a vendor HID interface. Windows Dynamic Lighting never
// sees this controller on most boards, which is why it is driven directly.
internal sealed class AuraDevice : ILightDevice
{
    private const ushort AsusVendor = 0x0B05;
    private const ushort AuraUsagePage = 0xFF72;

    private const int ReportLength = 65;
    private const byte ReportId = 0xEC;

    private const byte CmdFirmware = 0x82;
    private const byte CmdConfigTable = 0xB0;
    private const byte CmdEffect = 0x35;
    private const byte CmdDirect = 0x40;

    // Mode 0xFF of the effect command hands the zone over to direct colours.
    private const byte ModeDirect = 0xFF;

    // Set on the last packet of a zone: the controller shows what it was sent only then.
    private const byte ApplyFlag = 0x80;

    // A report has room for twenty LEDs after its five header bytes.
    private const int LedsPerPacket = 20;

    // The first LED of a packet is one byte, and the controller stops at 120 on a header anyway:
    // longer strips are simply lit up to here.
    internal const int MaxArgbLeds = 120;

    // Config table offsets, counted from the start of the table at byte 4 of the reply.
    private const int TableStart = 4;
    private const int ArgbHeadersAt = 0x02;
    private const int BoardLedsAt = 0x1B;
    private const int RgbHeadersAt = 0x1D;

    // The board channel takes colours on direct channel 4; addressable header i on channel i.
    private const byte BoardDirectChannel = 0x04;

    private readonly FileStream _stream;
    private readonly byte[] _packet = new byte[ReportLength];

    // One colour for a whole zone, kept so a frame allocates nothing.
    private readonly Rgb[] _fill;

    public string Path { get; }
    public string Firmware { get; }
    public IReadOnlyList<AuraZone> Zones { get; }

    private AuraDevice(string path, FileStream stream, string firmware, IReadOnlyList<AuraZone> zones)
    {
        Path = path;
        _stream = stream;
        Firmware = firmware;
        Zones = zones;
        _fill = new Rgb[zones.Count == 0 ? 0 : zones.Max(z => z.LedCount)];
    }

    // The controller, when the machine has one and it answers. ledsPerChannel is how many LEDs
    // every addressable header is driven with, up to what the protocol can address.
    public static AuraDevice? Open(int ledsPerChannel)
    {
        foreach (string path in Hid.Paths())
        {
            if (!Hid.Is(path, AsusVendor)) continue;
            if (Hid.Caps(path)?.UsagePage != AuraUsagePage) continue;

            FileStream? stream = null;
            try
            {
                stream = Hid.Open(path);

                byte[]? firmware = Query(stream, CmdFirmware);
                byte[]? table = Query(stream, CmdConfigTable);

                // Another ASUS device on the same page would not answer these the Aura way.
                if (firmware is null || table is null || firmware[1] != 0x02 || table[1] != 0x30)
                {
                    stream.Dispose();
                    continue;
                }

                string version = Encoding.ASCII.GetString(firmware, 2, 16).TrimEnd('\0', ' ');
                return new AuraDevice(path, stream, version, ZonesFrom(table, ledsPerChannel));
            }
            catch (Exception ex)
            {
                stream?.Dispose();
                Log.Info($"aura: {path} did not answer — {ex.Message}");
            }
        }

        return null;
    }

    // The board zone has as many LEDs as the table says; the addressable headers get what the
    // config says, clamped to what the protocol can address.
    internal static List<AuraZone> ZonesFrom(byte[] reply, int ledsPerChannel)
    {
        var zones = new List<AuraZone>();

        int boardLeds = reply[TableStart + BoardLedsAt] + reply[TableStart + RgbHeadersAt];
        if (boardLeds > 0) zones.Add(new AuraZone("board", 0x00, BoardDirectChannel, boardLeds));

        int argbHeaders = reply[TableStart + ArgbHeadersAt];
        int argbLeds = Math.Clamp(ledsPerChannel, 0, MaxArgbLeds);
        if (argbLeds > 0)
            for (int i = 0; i < argbHeaders; i++)
                zones.Add(new AuraZone($"ARGB {i + 1}", (byte)(i + 1), (byte)i, argbLeds));

        return zones;
    }

    public string Technology => "Aura";

    public string Describe() =>
        $"{Firmware}: " + string.Join(", ", Zones.Select(z => $"{z.Name} {z.LedCount} LEDs"));

    // Switches every zone to direct colours. Nothing is committed to the controller's memory: after
    // a power cycle it shows its own saved effect again.
    public void TakeOver()
    {
        foreach (AuraZone zone in Zones)
            Send(CmdEffect, zone.EffectChannel, 0x00, 0x00, ModeDirect);
    }

    public void Show(Rgb color)
    {
        Array.Fill(_fill, color);

        foreach (AuraZone zone in Zones)
            Write(zone, _fill.AsSpan(0, zone.LedCount));
    }

    private void Write(AuraZone zone, ReadOnlySpan<Rgb> colors)
    {
        int count = Math.Min(zone.LedCount, colors.Length);

        for (int start = 0; start < count; start += LedsPerPacket)
        {
            int n = Math.Min(LedsPerPacket, count - start);
            bool last = start + n >= count;

            Array.Clear(_packet);
            _packet[0] = ReportId;
            _packet[1] = CmdDirect;
            _packet[2] = (byte)(last ? ApplyFlag | zone.DirectChannel : zone.DirectChannel);
            _packet[3] = (byte)start;
            _packet[4] = (byte)n;

            for (int i = 0; i < n; i++)
            {
                Rgb c = colors[start + i];
                _packet[5 + 3 * i] = c.R;
                _packet[6 + 3 * i] = c.G;
                _packet[7 + 3 * i] = c.B;
            }

            _stream.Write(_packet, 0, ReportLength);
        }
    }

    private void Send(params byte[] command)
    {
        Array.Clear(_packet);
        _packet[0] = ReportId;
        Array.Copy(command, 0, _packet, 1, command.Length);
        _stream.Write(_packet, 0, ReportLength);
    }

    // A command that answers. Without an answer in a second the device is not an Aura controller,
    // or it is busy with someone else.
    private static byte[]? Query(FileStream stream, byte command)
    {
        var request = new byte[ReportLength];
        request[0] = ReportId;
        request[1] = command;
        stream.Write(request, 0, ReportLength);

        var reply = new byte[ReportLength];
        Task<int> read = stream.ReadAsync(reply, 0, ReportLength);
        return read.Wait(TimeSpan.FromSeconds(1)) && read.Result > 0 && reply[0] == ReportId ? reply : null;
    }

    public void Dispose() => _stream.Dispose();
}
