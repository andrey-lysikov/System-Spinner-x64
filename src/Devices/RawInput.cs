using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using SystemSpinnerX64.Diagnostics;

namespace SystemSpinnerX64.Devices;

// Keys as HID sees them, before Windows turns them into virtual keys — which for the brightness
// ones it never does. The keyboard hook sees nothing of those; this is where they can be read.
internal static class RawInput
{
    // Brightness up and down on the consumer page. These are what the whole class is here for.
    public const ushort BrightnessIncrement = 0x006F;
    public const ushort BrightnessDecrement = 0x0070;

    // One report as it arrived. The bytes are for the trace, when a usage cannot be read.
    internal sealed class Press
    {
        public required IReadOnlyList<ushort> Usages { get; init; }
        public required string Report { get; init; }

        public override string ToString()
        {
            var named = new List<string>();
            foreach (ushort usage in Usages) named.Add($"usage 0x{usage:X4} ({Name(usage)})");

            return $"{string.Join(", ", named)}, report {Report}";
        }
    }

    // Consumer Control: the media and brightness keys of a keyboard live on this page.
    private const ushort ConsumerPage = 0x0C;
    private const ushort ConsumerControl = 0x01;

    // Deliver the input even when no window of ours is in the foreground — a tray app never is.
    private const uint InputSink = 0x00000100;

    // Takes the registration away again; the target must be nothing at all for this one.
    private const uint RemoveDevice = 0x00000001;

    private const uint RidInput = 0x10000003;
    private const uint RidiPreparsedData = 0x20000005;

    private const uint TypeHid = 2;

    // HidP_GetUsages answers with this when it worked.
    private const int HidpStatusSuccess = 0x00110000;

    // Input report.
    private const int HidpInput = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr wParam;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices([In] RawInputDevice[] devices, uint count, uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetRawInputData(IntPtr rawInput, uint command, IntPtr data,
        ref uint size, uint headerSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetRawInputDeviceInfoW(IntPtr device, uint command, IntPtr data, ref uint size);

    [DllImport("hid.dll")]
    private static extern int HidP_GetUsages(int reportType, ushort usagePage, ushort linkCollection,
        [Out] ushort[] usages, ref uint usageLength, IntPtr preparsedData, IntPtr report, uint reportLength);

    [DllImport("hid.dll")]
    private static extern int HidP_MaxUsageListLength(int reportType, ushort usagePage, IntPtr preparsedData);

    // Asks Windows to send the consumer keys of every keyboard to this window.
    public static bool Listen(IntPtr window)
    {
        var devices = new[]
        {
            new RawInputDevice
            {
                UsagePage = ConsumerPage,
                Usage = ConsumerControl,
                Flags = InputSink,
                Target = window
            }
        };

        if (RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawInputDevice>()))
        {
            Log.Info("raw input: the consumer keys of the keyboard are being watched");
            return true;
        }

        Log.Warn($"raw input was not registered, error {Marshal.GetLastWin32Error()} — " +
                 "the brightness keys cannot be seen");
        return false;
    }

    // Gives the registration back before the window it points at goes.
    public static void Stop()
    {
        var devices = new[]
        {
            new RawInputDevice
            {
                UsagePage = ConsumerPage,
                Usage = ConsumerControl,
                Flags = RemoveDevice,
                Target = IntPtr.Zero
            }
        };

        RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawInputDevice>());
    }

    // What arrived with a WM_INPUT. null: a report of another kind, or a key being released.
    public static Press? Read(IntPtr message)
    {
        IntPtr buffer = IntPtr.Zero;

        try
        {
            uint size = 0;
            uint headerSize = (uint)Marshal.SizeOf<RawInputHeader>();

            if (GetRawInputData(message, RidInput, IntPtr.Zero, ref size, headerSize) != 0 || size == 0)
                return null;

            buffer = Marshal.AllocHGlobal((int)size);

            if (GetRawInputData(message, RidInput, buffer, ref size, headerSize) != size) return null;

            var header = Marshal.PtrToStructure<RawInputHeader>(buffer);
            if (header.Type != TypeHid) return null;

            // RAWHID follows the header: the size of one report, how many of them, then the reports
            // themselves back to back.
            IntPtr hid = buffer + (int)headerSize;
            int reportSize = Marshal.ReadInt32(hid);
            int reportCount = Marshal.ReadInt32(hid, 4);
            IntPtr reports = hid + 8;

            if (reportSize <= 0 || reportCount <= 0) return null;

            var held = new List<ushort>();

            for (int i = 0; i < reportCount; i++)
            {
                // A report with nothing listed in it is the release of what was held.
                held.AddRange(Usages(header.Device, reports + i * reportSize, (uint)reportSize));
            }

            if (held.Count == 0) return null;

            return new Press { Usages = held, Report = Hex(reports, reportSize) };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"the raw input was not read: {ex.Message}");
            return null;
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
        }
    }

    // The consumer usages held down in this report.
    private static IReadOnlyList<ushort> Usages(IntPtr device, IntPtr report, uint reportSize)
    {
        IntPtr preparsed = IntPtr.Zero;

        try
        {
            uint size = 0;
            if (GetRawInputDeviceInfoW(device, RidiPreparsedData, IntPtr.Zero, ref size) != 0 || size == 0)
                return Array.Empty<ushort>();

            preparsed = Marshal.AllocHGlobal((int)size);
            if (GetRawInputDeviceInfoW(device, RidiPreparsedData, preparsed, ref size) <= 0)
                return Array.Empty<ushort>();

            int max = HidP_MaxUsageListLength(HidpInput, ConsumerPage, preparsed);
            if (max <= 0) return Array.Empty<ushort>();

            var usages = new ushort[max];
            uint length = (uint)max;

            if (HidP_GetUsages(HidpInput, ConsumerPage, 0, usages, ref length, preparsed, report, reportSize)
                != HidpStatusSuccess)
            {
                return Array.Empty<ushort>();
            }

            var held = new List<ushort>((int)length);
            for (int i = 0; i < length; i++) held.Add(usages[i]);

            return held;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"the HID usages were not read: {ex.Message}");
            return Array.Empty<ushort>();
        }
        finally
        {
            if (preparsed != IntPtr.Zero) Marshal.FreeHGlobal(preparsed);
        }
    }

    // The usages of the consumer page worth naming.
    private static string Name(ushort usage) => usage switch
    {
        0x006F => "Brightness Increment",
        0x0070 => "Brightness Decrement",
        0x0079 => "Keyboard Backlight Up",
        0x007A => "Keyboard Backlight Down",
        0x00B5 => "Media Next",
        0x00B6 => "Media Previous",
        0x00B7 => "Media Stop",
        0x00CD => "Media Play/Pause",
        0x00E2 => "Mute",
        0x00E9 => "Volume Up",
        0x00EA => "Volume Down",
        _ => "unnamed"
    };

    // The report byte for byte: all there is to go on when the usages cannot be read.
    private static string Hex(IntPtr report, int size)
    {
        var text = new StringBuilder(size * 3);

        for (int i = 0; i < size; i++)
        {
            if (i > 0) text.Append(' ');
            text.Append(Marshal.ReadByte(report, i).ToString("X2"));
        }

        return text.ToString();
    }
}
