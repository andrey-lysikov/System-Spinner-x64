//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using SystemSpinnerX64.Diagnostics;

namespace SystemSpinnerX64.Lighting;

// How a Nollie model takes its colours. The 16 and 32 channel ones are high-speed USB and take a
// whole channel in one 1025-byte report; the rest are full-speed and take 21 LEDs per 65-byte
// report, shown only when the frame is committed.
internal enum NollieWire { HighSpeed, FullSpeed }

// One Nollie model: where it sits on USB and what its channels hold. Channels are the numbers the
// controller knows them by, in the order they are sent.
internal sealed record NollieModel(
    string Name, ushort Vendor, ushort Product, int? Interface,
    NollieWire Wire, int[] Channels, int MaxLeds, bool Grb, bool ReportsLedCount = false)
{
    public int ReportLength => Wire == NollieWire.HighSpeed ? NollieDevice.HighSpeedReport : NollieDevice.FullSpeedReport;
}

// The Nollie ARGB controllers, spoken to the way OpenRGB does. They answer nothing, so a model is
// known by its USB ids alone, and every channel is sent the same count of LEDs in one colour.
internal sealed class NollieDevice : ILightDevice
{
    internal const int HighSpeedReport = 1025;
    internal const int FullSpeedReport = 65;

    // A full-speed report has room for 21 LEDs after the report id and the packet number.
    internal const int LedsPerPacket = 21;

    // The full-speed commit: what was sent since the last one goes out to the LEDs.
    private const byte CmdCommit = 0xFF;

    // The Nollie 1 is told how long its strip is before it takes colours.
    private const byte CmdLedCount = 0xFE;

    // A high-speed controller shows each half of its channels once the half's flagged channel
    // arrives, and needs a moment after it before the next report.
    private const int FlagChannel1 = 15;
    private const int FlagChannel2 = 31;
    private static readonly TimeSpan FlagPause = TimeSpan.FromMilliseconds(8);

    private static readonly int[] All32 = Enumerable.Range(0, 32).ToArray();
    private static readonly int[] Low16 = Enumerable.Range(0, 16).ToArray();
    private static readonly int[] High16 = Enumerable.Range(16, 16).ToArray();
    private static readonly int[] Eight = Enumerable.Range(0, 8).ToArray();
    private static readonly int[] One = [0];

    // What OpenRGB knows. The same model comes with the vendor ids of each firmware generation;
    // the OS2.1 ones are composite devices with the lighting on one interface.
    internal static readonly NollieModel[] Models =
    [
        new("Nollie 32CH", 0x3061, 0x4714, null, NollieWire.HighSpeed, All32, 256, Grb: true),
        new("Nollie 16CH", 0x3061, 0x4716, null, NollieWire.HighSpeed, High16, 256, Grb: true),
        new("Nollie 8CH", 0x16D2, 0x1F01, null, NollieWire.FullSpeed, Eight, 126, Grb: true),
        new("Nollie 1CH", 0x16D2, 0x1F11, null, NollieWire.FullSpeed, One, 630, Grb: true, ReportsLedCount: true),
        new("Nollie 28/12", 0x16D2, 0x1616, null, NollieWire.FullSpeed, One, 42, Grb: false),
        new("Nollie 28 L1", 0x16D2, 0x1617, null, NollieWire.FullSpeed, Eight, 525, Grb: false),
        new("Nollie 28 L2", 0x16D2, 0x1618, null, NollieWire.FullSpeed, Eight, 525, Grb: false),

        new("Nollie 32CH OS2", 0x16D5, 0x4714, null, NollieWire.HighSpeed, All32, 256, Grb: true),
        new("Nollie 16CH OS2", 0x16D5, 0x4716, null, NollieWire.HighSpeed, Low16, 256, Grb: true),
        new("Nollie 8CH OS2", 0x16D5, 0x1F01, null, NollieWire.FullSpeed, Eight, 126, Grb: true),
        new("Nollie 1CH OS2", 0x16D5, 0x1F11, null, NollieWire.FullSpeed, One, 630, Grb: true),

        new("Nollie 32CH OS2.1", 0x16D5, 0x2A32, 0, NollieWire.HighSpeed, All32, 256, Grb: true),
        new("Nollie 16CH OS2.1", 0x16D5, 0x2A16, 0, NollieWire.HighSpeed, Low16, 256, Grb: true),
        new("Nollie 8CH OS2.1", 0x16D5, 0x2A08, 2, NollieWire.FullSpeed, Eight, 126, Grb: true),
        new("Prism 8CH OS2.1", 0x16D5, 0x2C08, 2, NollieWire.FullSpeed, Eight, 126, Grb: true),
        new("Nollie 1CH OS2.1", 0x16D5, 0x2A01, 2, NollieWire.FullSpeed, One, 630, Grb: true),
    ];

    private readonly FileStream _stream;
    private readonly byte[] _packet;

    public NollieModel Model { get; }
    public string Path { get; }
    public int LedCount { get; }

    private NollieDevice(NollieModel model, string path, FileStream stream, int ledCount)
    {
        Model = model;
        Path = path;
        _stream = stream;
        LedCount = ledCount;
        _packet = new byte[model.ReportLength];
    }

    // Every Nollie on the machine: people chain two when one runs out of channels. ledsPerChannel
    // is what each channel is driven with, up to what the model addresses; none when it is zero.
    public static List<ILightDevice> OpenAll(int ledsPerChannel)
    {
        var found = new List<ILightDevice>();
        if (ledsPerChannel <= 0) return found;

        foreach (string path in Hid.Paths())
        {
            NollieModel? model = Models.FirstOrDefault(m => Hid.Is(path, m.Vendor, m.Product, m.Interface));
            if (model is null) continue;

            // The lighting interface is the one whose reports are the length the protocol writes;
            // a model may show other collections beside it.
            if (Hid.Caps(path)?.OutputReportLength != model.ReportLength) continue;

            try
            {
                FileStream stream = Hid.Open(path);
                found.Add(new NollieDevice(model, path, stream, Math.Min(ledsPerChannel, model.MaxLeds)));
            }
            catch (Exception ex)
            {
                Log.Info($"nollie: {path} did not open — {ex.Message}");
            }
        }

        return found;
    }

    public string Technology => "Nollie";

    public string Describe() => $"{Model.Name}: {Model.Channels.Length} × {LedCount} LEDs";

    // Nothing to switch over: a Nollie shows whatever it is sent. The Nollie 1 only has to know how
    // long its strip is. Nothing is saved on the controller either way.
    public void TakeOver()
    {
        if (!Model.ReportsLedCount) return;

        Array.Clear(_packet);
        _packet[1] = CmdLedCount;
        _packet[2] = 0x03;
        for (int i = 0; i < Model.Channels.Length; i++)
        {
            _packet[3 + 2 * i] = (byte)(LedCount & 0xFF);
            _packet[4 + 2 * i] = (byte)(LedCount >> 8);
        }

        _stream.Write(_packet, 0, _packet.Length);
    }

    public void Show(Rgb color)
    {
        if (Model.Wire == NollieWire.HighSpeed)
        {
            foreach (int channel in Model.Channels)
            {
                HighSpeedPacket(_packet, channel, LedCount, color);
                _stream.Write(_packet, 0, _packet.Length);

                if (channel is FlagChannel1 or FlagChannel2) Thread.Sleep(FlagPause);
            }
        }
        else
        {
            int packets = (LedCount + LedsPerPacket - 1) / LedsPerPacket;

            foreach (int channel in Model.Channels)
                for (int p = 0; p < packets; p++)
                {
                    int n = Math.Min(LedsPerPacket, LedCount - p * LedsPerPacket);
                    FullSpeedPacket(_packet, Model, channel, p, n, color);
                    _stream.Write(_packet, 0, _packet.Length);
                }

            Array.Clear(_packet);
            _packet[1] = CmdCommit;
            _stream.Write(_packet, 0, _packet.Length);
        }
    }

    // One high-speed channel: its number, the half flag, the LED count big-endian, then G R B.
    internal static void HighSpeedPacket(byte[] packet, int channel, int count, Rgb color)
    {
        Array.Clear(packet);
        packet[1] = (byte)channel;
        packet[2] = channel switch { FlagChannel1 => 1, FlagChannel2 => 2, _ => 0 };
        packet[3] = (byte)(count >> 8);
        packet[4] = (byte)count;

        for (int i = 0; i < count; i++)
        {
            packet[5 + 3 * i] = color.G;
            packet[6 + 3 * i] = color.R;
            packet[7 + 3 * i] = color.B;
        }
    }

    // One full-speed packet: the numbers run on through the channels, each channel owning as many
    // as its longest strip needs, so packet p of channel c is c * that + p.
    internal static void FullSpeedPacket(byte[] packet, NollieModel model, int channel, int index, int count, Rgb color)
    {
        int perChannel = model.MaxLeds / LedsPerPacket;

        Array.Clear(packet);
        packet[1] = (byte)(index + channel * perChannel);

        (byte a, byte b) = model.Grb ? (color.G, color.R) : (color.R, color.G);
        for (int i = 0; i < count; i++)
        {
            packet[2 + 3 * i] = a;
            packet[3 + 3 * i] = b;
            packet[4 + 3 * i] = color.B;
        }
    }

    public void Dispose() => _stream.Dispose();
}
