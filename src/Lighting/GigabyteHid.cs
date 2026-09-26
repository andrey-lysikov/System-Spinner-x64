//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using SystemSpinnerX64.Diagnostics;

namespace SystemSpinnerX64.Lighting;

// The Gigabyte RGB Fusion 2 USB controller on the motherboard (ITE IT8297, IT5702, IT5711), spoken
// to the way OpenRGB does: 64-byte feature reports with id 0xCC. The board LEDs and the 12 V
// headers take one static colour through the effect registers; the addressable headers take their
// LEDs directly. Which of them a board wires up does not matter when all show the same colour.
internal sealed class FusionDevice : ILightDevice
{
    private const ushort IteVendor = 0x048D;
    private const ushort GcUsbVendor = 0x0414;
    private const ushort FusionUsagePage = 0xFF89;
    private const ushort FusionUsage = 0xCC;

    internal const ushort It5711 = 0x5711;
    internal const ushort GcUsb = 0xA100;

    private static readonly (ushort Vendor, ushort Product)[] Products =
    [
        (IteVendor, 0x8297), (IteVendor, 0x8950), (IteVendor, 0x5702), (IteVendor, It5711), (GcUsbVendor, GcUsb)
    ];

    internal const int ReportLength = 64;
    private const byte ReportId = 0xCC;

    private const byte CmdApply = 0x28;
    private const byte CmdBeat = 0x31;
    private const byte CmdStripEffects = 0x32;
    private const byte CmdLedCount = 0x34;
    private const byte CmdLampArray = 0x48;
    private const byte CmdInfo = 0x60;
    private const byte CmdInfo5711 = 0x61;

    private const byte EffectStatic = 1;

    // A report has room for 19 LEDs after its five header bytes.
    internal const int LedsPerPacket = 19;

    // What an addressable header can be told to carry.
    internal const int MaxLeds = 1024;

    private static readonly TimeSpan StripEffectsPause = TimeSpan.FromMilliseconds(50);

    private readonly SafeFileHandle _handle;
    private readonly int _featureLength;
    private readonly byte[] _packet = new byte[ReportLength];

    public string Path { get; }
    public ushort Product { get; }
    public string Name { get; }
    public string Firmware { get; }
    public int LedCount { get; }

    // Report header of each addressable header, with the colour order the board keeps for it.
    public IReadOnlyList<(byte Header, ByteOrder Order)> Strips { get; }

    private readonly bool _lampArray;

    private FusionDevice(string path, SafeFileHandle handle, int featureLength, ushort product, byte[] info,
                         byte[]? info5711, int ledCount)
    {
        Path = path;
        _handle = handle;
        _featureLength = featureLength;
        Product = product;
        LedCount = ledCount;

        Name = Encoding.ASCII.GetString(info, 12, 28).Split('\0')[0].Trim();
        uint fw = BinaryPrimitives.ReadUInt32LittleEndian(info.AsSpan(4));
        Firmware = $"{fw & 0xFF}.{(fw >> 8) & 0xFF}.{(fw >> 16) & 0xFF}.{fw >> 24}";
        _lampArray = (info[11] & 0x02) != 0;

        Strips = StripsOf(product, info, info5711);
    }

    public static FusionDevice? Open(int ledsPerChannel)
    {
        foreach (string path in Hid.Paths())
        {
            (ushort vendor, ushort product) = Products.FirstOrDefault(p => Hid.Is(path, p.Vendor, p.Product));
            if (vendor == 0) continue;

            HidCaps? caps = Hid.Caps(path);
            if (caps is not { UsagePage: FusionUsagePage, Usage: FusionUsage } || caps.Value.FeatureReportLength < ReportLength)
                continue;

            SafeFileHandle? handle = null;
            try
            {
                handle = Hid.OpenForFeatures(path);
                int featureLength = caps.Value.FeatureReportLength;

                byte[]? info = Query(handle, featureLength, CmdInfo);
                if (info is null)
                {
                    handle.Dispose();
                    continue;
                }

                byte[]? info5711 = product == It5711 ? Query(handle, featureLength, CmdInfo5711) : null;

                return new FusionDevice(path, handle, featureLength, product, info, info5711,
                                        Math.Clamp(ledsPerChannel, 0, MaxLeds));
            }
            catch (Exception ex)
            {
                handle?.Dispose();
                Log.Info($"fusion: {path} did not answer — {ex.Message}");
            }
        }

        return null;
    }

    // The addressable headers the controller has: two, six on an IT5711, one on the GC-USB card.
    // Each keeps its colour order in the info report: the offset of red, green and blue in an LED.
    internal static List<(byte Header, ByteOrder Order)> StripsOf(ushort product, byte[] info, byte[]? info5711)
    {
        uint Cal(byte[]? report, int at) => report is null ? 0 : BinaryPrimitives.ReadUInt32LittleEndian(report.AsSpan(at));

        var strips = new List<(byte, ByteOrder)> { (0x58, ByteOrder.From(Cal(info, 44))) };
        if (product == GcUsb) return strips;

        strips.Add((0x59, ByteOrder.From(Cal(info, 48))));
        if (product != It5711) return strips;

        strips.Add((0x62, ByteOrder.From(Cal(info5711, 4))));
        strips.Add((0x63, ByteOrder.From(Cal(info5711, 8))));
        strips.Add((0x64, ByteOrder.From(Cal(info5711, 12))));
        strips.Add((0x65, ByteOrder.From(Cal(info5711, 16))));
        return strips;
    }

    public string Technology => "RGB Fusion";

    public string Describe() =>
        $"Gigabyte RGB Fusion 2 {(Name.Length > 0 ? Name : $"0x{Product:X4}")} {Firmware}: " +
        $"board LEDs, {Strips.Count} addressable headers × {LedCount} LEDs";

    // Sets the controller up as OpenRGB does before direct colours: Windows Dynamic Lighting and the
    // music effect off, the effect registers cleared, the addressable headers sized and taken off
    // their own effects. Nothing is written to the controller's flash.
    public void TakeOver()
    {
        if (_lampArray) SendCommand(CmdLampArray, 0);

        for (byte reg = 0x20; reg <= 0x27; reg++) SendCommand(reg, 0, 0);
        if (Product == It5711)
            for (byte reg = 0x90; reg <= 0x92; reg++) SendCommand(reg, 0, 0);
        SendCommand(CmdApply, 0xFF, Product == It5711 ? (byte)0x07 : (byte)0x00);

        SendCommand(CmdBeat, 0);

        byte count = CountCode(LedCount);
        SendCommand(CmdLedCount, (byte)(count << 4 | count), (byte)(count << 4 | count), (byte)(count << 4 | count));

        byte strips = Product switch { GcUsb => 0x01, It5711 => 0x7B, _ => 0x03 };
        SendCommand(CmdStripEffects, strips);
        Thread.Sleep(StripEffectsPause);
    }

    public void Show(Rgb color)
    {
        // Every effect register at once, static in this colour, then applied.
        uint zones = Product == It5711 ? 0x07FFu : 0xFFu;
        EffectPacket(_packet, zones, color);
        Send();

        Array.Clear(_packet);
        _packet[0] = ReportId;
        _packet[1] = CmdApply;
        BinaryPrimitives.WriteUInt32LittleEndian(_packet.AsSpan(2), zones);
        Send();

        foreach ((byte header, ByteOrder order) in Strips)
            for (int start = 0; start < LedCount; start += LedsPerPacket)
            {
                StripPacket(_packet, header, order, start, Math.Min(LedsPerPacket, LedCount - start), color);
                Send();
            }
    }

    // A static effect on the effect registers the mask names: header 0x20 addresses them all.
    internal static void EffectPacket(byte[] packet, uint zones, Rgb color)
    {
        Array.Clear(packet);
        packet[0] = ReportId;
        packet[1] = 0x20;
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(2), zones);
        packet[11] = EffectStatic;
        packet[12] = 0xFF;          // max brightness
        packet[14] = color.B;       // colour as little-endian 0x00RRGGBB
        packet[15] = color.G;
        packet[16] = color.R;
    }

    // count LEDs of an addressable header from LED start: the byte offset, the byte count, then the
    // colours in the header's own order.
    internal static void StripPacket(byte[] packet, byte header, ByteOrder order, int start, int count, Rgb color)
    {
        Array.Clear(packet);
        packet[0] = ReportId;
        packet[1] = header;
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2), (ushort)(start * 3));
        packet[4] = (byte)(count * 3);

        for (int i = 0; i < count; i++)
        {
            int at = 5 + 3 * i;
            packet[at + order.R] = color.R;
            packet[at + order.G] = color.G;
            packet[at + order.B] = color.B;
        }
    }

    // The size code of an addressable header: 32, 64, 256, 512 or 1024 LEDs.
    internal static byte CountCode(int leds) => leds switch
    {
        <= 32 => 0,
        <= 64 => 1,
        <= 256 => 2,
        <= 512 => 3,
        _ => 4
    };

    private void SendCommand(byte command, byte a, byte b = 0, byte c = 0)
    {
        Array.Clear(_packet);
        _packet[0] = ReportId;
        _packet[1] = command;
        _packet[2] = a;
        _packet[3] = b;
        _packet[4] = c;
        Send();
    }

    private void Send() => Hid.SetFeature(_handle, _packet, ReportLength, _featureLength);

    // A command whose answer is read back as the next feature report.
    private static byte[]? Query(SafeFileHandle handle, int featureLength, byte command)
    {
        var request = new byte[ReportLength];
        request[0] = ReportId;
        request[1] = command;
        Hid.SetFeature(handle, request, ReportLength, featureLength);

        var reply = new byte[featureLength];
        reply[0] = ReportId;
        return Hid.GetFeature(handle, reply) >= ReportLength && reply[0] == ReportId ? reply : null;
    }

    public void Dispose() => _handle.Dispose();
}

// Where red, green and blue go within an LED's three bytes.
internal readonly record struct ByteOrder(int R, int G, int B)
{
    public static readonly ByteOrder Plain = new(0, 1, 2);

    // The board's calibration word: the offset of blue, green and red in its low three bytes.
    // Unset or unreadable, the LEDs are sent as RGB.
    public static ByteOrder From(uint calibration)
    {
        int b = (int)(calibration & 0xFF), g = (int)((calibration >> 8) & 0xFF), r = (int)((calibration >> 16) & 0xFF);
        bool valid = calibration >> 24 == 0 && r < 3 && g < 3 && b < 3 && r != g && r != b && g != b;
        return valid ? new ByteOrder(r, g, b) : Plain;
    }
}
