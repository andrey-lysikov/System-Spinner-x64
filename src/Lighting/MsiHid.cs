//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Linq;
using Microsoft.Win32.SafeHandles;
using SystemSpinnerX64.Diagnostics;

namespace SystemSpinnerX64.Lighting;

// How a Mystic Light controller takes its colours. Boards from Z490 and B550 up to Z790 and B650
// keep all their zones in one 185-byte feature report; X870, B850 and Z890 take each addressable
// header in a report of its own.
internal enum MysticWire { Zones185, Headers761 }

// The MSI Mystic Light controller on the motherboard, spoken to the way OpenRGB does. Only boards
// sold from 2020 on: the older ones speak layouts of their own that differ board by board.
internal sealed class MysticLightDevice : ILightDevice
{
    private const ushort MsiVendor = 0x1462;
    private const ushort CommonVendor = 0x0DB0;
    private const ushort CommonProduct = 0x0076;

    // Z490, B460, B550, A520 (2020); Z590, B560, X570S, Z690 (2021); B660, H610, X670, B650, Z790
    // (2022); B760 (2023); Z890, X870, B850 (2024-2025). The same list OpenRGB detects, less the
    // boards from before 2020.
    internal static readonly ushort[] Products =
    [
        0x7C56, 0x7C71, 0x7C73, 0x7C75, 0x7C76, 0x7C77, 0x7C79, 0x7C80, 0x7C81, 0x7C82, 0x7C83, 0x7C86,
        0x7C90, 0x7C91, 0x7C92, 0x7C94, 0x7C95, 0x7C98,
        0x7D03, 0x7D04, 0x7D06, 0x7D07, 0x7D08, 0x7D09, 0x7D13, 0x7D14, 0x7D15, 0x7D17, 0x7D18, 0x7D19,
        0x7D20, 0x7D25, 0x7D27, 0x7D28, 0x7D29, 0x7D30, 0x7D31, 0x7D32, 0x7D33, 0x7D36, 0x7D37, 0x7D38,
        0x7D40, 0x7D41, 0x7D42, 0x7D43, 0x7D46, 0x7D50, 0x7D51, 0x7D52, 0x7D53, 0x7D54, 0x7D59, 0x7D67,
        0x7D69, 0x7D70, 0x7D73, 0x7D74, 0x7D75, 0x7D76, 0x7D77, 0x7D78, 0x7D86, 0x7D88, 0x7D89, 0x7D90,
        0x7D91, 0x7D93, 0x7D96, 0x7D97, 0x7D98, 0x7D99,
        0x7E01, 0x7E03, 0x7E06, 0x7E07, 0x7E09, 0x7E10, 0x7E12, 0x7E16, 0x7E20, 0x7E24, 0x7E26, 0x7E27,
        0x7E32, 0x7E34, 0x7E59, 0x7E66, 0x7E70, 0x7E76, 0x7E80, 0x7E81, 0x7E86,
    ];

    // --- the 185-byte layout ---

    internal const int ZonesReportLength = 185;
    private const byte ZonesReportId = 0x52;

    // Where each zone's ten bytes start. The Corsair header and its outer ring are left alone.
    internal static readonly int[] ZoneOffsets =
        [1, 11, 21, 31, 42, 74, 84, 94, 104, 114, 124, 134, 144, 154, 164, 174];

    private const int OnboardZone = 74;
    private const int SaveFlag = 184;

    private const byte ModeStatic = 1;
    private const byte BrightnessFull = 10 << 2;
    private const byte CustomColor = 0x80;
    private const byte SyncPipes = 0x30;

    // --- the 761 layout ---

    private const byte SetupReportId = 0x50;
    internal const int SetupReportLength = 290;
    private const byte HeaderReportId = 0x51;
    internal const int HeaderReportLength = 7 + 3 * HeaderLeds;

    // What a header report holds, and so what every addressable header is lit up to.
    internal const int HeaderLeds = 240;

    // JAF, then JARGB 1 to 3: the kind of header and its number.
    private static readonly (byte Kind, byte Index)[] Headers = [(0x08, 0), (0x04, 0), (0x04, 1), (0x04, 2)];

    private readonly SafeFileHandle _handle;
    private readonly int _featureLength;
    private readonly byte[] _packet;
    private readonly int _ledCount;

    public string Path { get; }
    public ushort Product { get; }
    public MysticWire Wire { get; }

    private MysticLightDevice(string path, ushort product, SafeFileHandle handle, int featureLength, MysticWire wire,
                              byte[] packet, int ledCount)
    {
        Path = path;
        Product = product;
        _handle = handle;
        _featureLength = featureLength;
        Wire = wire;
        _packet = packet;
        _ledCount = ledCount;
    }

    public static MysticLightDevice? Open(int ledsPerChannel)
    {
        foreach (string path in Hid.Paths())
        {
            ushort product = Products.FirstOrDefault(pid => Hid.Is(path, MsiVendor, pid));
            if (product == 0 && Hid.Is(path, CommonVendor, CommonProduct)) product = CommonProduct;
            if (product == 0) continue;

            HidCaps? caps = Hid.Caps(path);
            if (caps is not { } c || c.FeatureReportLength < ZonesReportLength) continue;

            SafeFileHandle? handle = null;
            try
            {
                handle = Hid.OpenForFeatures(path);

                // Which layout the board speaks shows in how long its zone report comes back.
                var zones = new byte[200];
                zones[0] = ZonesReportId;
                int length = Hid.GetFeature(handle, zones);

                if (length is ZonesReportLength or ZonesReportLength + 1)
                {
                    Array.Resize(ref zones, ZonesReportLength);
                    return new MysticLightDevice(path, product, handle, c.FeatureReportLength, MysticWire.Zones185,
                                                 zones, 0);
                }

                if (c.FeatureReportLength >= HeaderReportLength)
                {
                    // Reading the setup report first is what makes the board answer at all.
                    var setup = new byte[SetupReportLength];
                    setup[0] = SetupReportId;
                    Hid.GetFeature(handle, setup);

                    return new MysticLightDevice(path, product, handle, c.FeatureReportLength, MysticWire.Headers761,
                                                 new byte[HeaderReportLength], Math.Clamp(ledsPerChannel, 0, HeaderLeds));
                }

                handle.Dispose();
                Log.Info($"mystic light: {path} speaks a layout from before 2020 ({length} bytes)");
            }
            catch (Exception ex)
            {
                handle?.Dispose();
                Log.Info($"mystic light: {path} did not answer — {ex.Message}");
            }
        }

        return null;
    }

    public string Technology => "Mystic Light";

    public string Describe() => Wire == MysticWire.Zones185
        ? $"MSI Mystic Light 0x{Product:X4}: every zone in one colour"
        : $"MSI Mystic Light 0x{Product:X4}: JAF and 3 addressable headers × {_ledCount} LEDs";

    // The 185-byte boards need nothing: every frame carries the whole state. The newer ones are told
    // which zones follow the addressable reports, as SignalRGB and OpenRGB tell them.
    public void TakeOver()
    {
        if (Wire == MysticWire.Headers761)
            Hid.SetFeature(_handle, SetupReport(), SetupReportLength, _featureLength);
    }

    public void Show(Rgb color)
    {
        if (Wire == MysticWire.Zones185)
        {
            ZonesFrame(_packet, color);
            Hid.SetFeature(_handle, _packet, ZonesReportLength, _featureLength);
            return;
        }

        foreach ((byte kind, byte index) in Headers)
        {
            HeaderFrame(_packet, kind, index, _ledCount, color);
            Hid.SetFeature(_handle, _packet, HeaderReportLength, _featureLength);
        }
    }

    // Every zone static in this colour at full brightness, over what the board had, never saved.
    internal static void ZonesFrame(byte[] packet, Rgb color)
    {
        packet[0] = ZonesReportId;

        foreach (int at in ZoneOffsets)
        {
            packet[at] = ModeStatic;
            packet[at + 1] = color.R;
            packet[at + 2] = color.G;
            packet[at + 3] = color.B;
            packet[at + 4] = BrightnessFull;
            packet[at + 8] |= CustomColor;
            packet[at + 9] = 0;
        }

        // A static light has the pipes follow nothing but their own zones.
        packet[OnboardZone + 8] &= unchecked((byte)~SyncPipes);
        packet[SaveFlag] = 0;
    }

    internal static void HeaderFrame(byte[] packet, byte kind, byte index, int count, Rgb color)
    {
        Array.Clear(packet);
        packet[0] = HeaderReportId;
        packet[1] = 0x09;
        packet[2] = kind;
        packet[3] = index;
        packet[6] = HeaderLeds;

        for (int i = 0; i < count; i++)
        {
            packet[7 + 3 * i] = color.R;
            packet[8 + 3 * i] = color.G;
            packet[9 + 3 * i] = color.B;
        }
    }

    // Eighteen rows of sixteen: the four addressable headers on, the pipes, the 12 V headers and the
    // board LEDs off, and the last row, which seems to select them all, as it is.
    internal static byte[] SetupReport()
    {
        byte[] headers = [0x09, 0xFF, 0, 0, 0, 0xFF, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF, 0x03, 0x15, 0x78];
        byte[] off = new byte[16];
        off[15] = 0x1E;
        byte[] all = [0x09, 0xFF, 0, 0, 0, 0xFF, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF, 0x03, 0x95, 0x1E];

        var report = new byte[SetupReportLength];
        report[0] = SetupReportId;
        for (int row = 0; row < 18; row++)
        {
            byte[] bytes = row < 4 ? headers : row < 17 ? off : all;
            bytes.CopyTo(report, 1 + 16 * row);
        }

        return report;
    }

    public void Dispose() => _handle.Dispose();
}
