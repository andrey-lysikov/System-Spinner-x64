//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SystemSpinnerX64.Lighting;

// What a HID interface says about itself before it is opened for real.
internal readonly record struct HidCaps(ushort UsagePage, ushort Usage, int OutputReportLength, int FeatureReportLength);

// The HID plumbing the lighting drivers share: finding the interfaces, reading what they are,
// opening one for reports.
internal static class Hid
{
    // Every HID interface present, by device path.
    public static List<string> Paths()
    {
        var paths = new List<string>();
        HidD_GetHidGuid(out Guid hid);

        IntPtr set = SetupDiGetClassDevs(ref hid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (set == new IntPtr(-1)) return paths;

        try
        {
            for (int index = 0; ; index++)
            {
                var data = new InterfaceData { Size = Marshal.SizeOf<InterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hid, index, ref data)) break;

                SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out int required, IntPtr.Zero);
                if (required <= 0) continue;

                IntPtr detail = Marshal.AllocHGlobal(required);
                try
                {
                    // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W on x64: the DWORD and one WCHAR, padded.
                    Marshal.WriteInt32(detail, 8);
                    if (SetupDiGetDeviceInterfaceDetail(set, ref data, detail, required, out _, IntPtr.Zero))
                        paths.Add(Marshal.PtrToStringUni(detail + 4) ?? "");
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }

        return paths;
    }

    // Whether the path belongs to this vendor and product, and to this interface of a composite
    // device when one is named. Windows spells them into the path: "vid_16d2&pid_1f01&mi_02".
    public static bool Is(string path, ushort vendor, ushort? product = null, int? iface = null)
    {
        if (!path.Contains($"vid_{vendor:x4}", StringComparison.OrdinalIgnoreCase)) return false;
        if (product is { } pid && !path.Contains($"pid_{pid:x4}", StringComparison.OrdinalIgnoreCase)) return false;
        if (iface is { } mi && !path.Contains($"mi_{mi:x2}", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    // Asked with no access at all: that works on interfaces somebody else holds open exclusively.
    // Null when the interface would not say.
    public static HidCaps? Caps(string path)
    {
        using SafeFileHandle handle = CreateFile(path, 0, ShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid) return null;

        if (!HidD_GetPreparsedData(handle, out IntPtr preparsed)) return null;
        try
        {
            HidP_GetCaps(preparsed, out RawCaps caps);
            return new HidCaps(caps.UsagePage, caps.Usage, caps.OutputReportByteLength, caps.FeatureReportByteLength);
        }
        finally
        {
            HidD_FreePreparsedData(preparsed);
        }
    }

    // Overlapped, so a reply that never comes can be waited for with a timeout.
    public static FileStream Open(string path)
    {
        SafeFileHandle handle = CreateFile(path, GenericRead | GenericWrite, ShareReadWrite, IntPtr.Zero,
                                           OpenExisting, FlagOverlapped, IntPtr.Zero);
        if (handle.IsInvalid)
            throw new IOException($"the device would not open (error {Marshal.GetLastWin32Error()})");

        return new FileStream(handle, FileAccess.ReadWrite, bufferSize: 0, isAsync: true);
    }

    // For controllers spoken to through feature reports alone: those are synchronous anyway.
    public static SafeFileHandle OpenForFeatures(string path)
    {
        SafeFileHandle handle = CreateFile(path, GenericRead | GenericWrite, ShareReadWrite, IntPtr.Zero,
                                           OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
            throw new IOException($"the device would not open (error {Marshal.GetLastWin32Error()})");

        return handle;
    }

    // Asks for the feature report whose id is in buffer[0]. The count of bytes that came back, the
    // id included, tells which layout a controller speaks; zero when it would not answer.
    public static int GetFeature(SafeFileHandle handle, byte[] buffer)
    {
        if (!DeviceIoControl(handle, IoctlGetFeature, buffer, buffer.Length, buffer, buffer.Length, out int returned, IntPtr.Zero))
            return 0;
        return returned;
    }

    // Sends a feature report, padded to the longest one the interface declares, as Windows wants it.
    // Throws when the controller went away.
    public static void SetFeature(SafeFileHandle handle, byte[] report, int length, int featureLength)
    {
        byte[] buffer = report;
        if (featureLength > length)
        {
            buffer = new byte[featureLength];
            Array.Copy(report, buffer, length);
            length = featureLength;
        }

        if (!DeviceIoControl(handle, IoctlSetFeature, buffer, length, null, 0, out _, IntPtr.Zero))
            throw new IOException($"the feature report was refused (error {Marshal.GetLastWin32Error()})");
    }

    [DllImport("hid.dll")]
    private static extern void HidD_GetHidGuid(out Guid guid);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr preparsed);

    [DllImport("hid.dll")]
    private static extern bool HidD_FreePreparsedData(IntPtr preparsed);

    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(IntPtr preparsed, out RawCaps caps);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr parent, int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid classGuid,
                                                           int index, ref InterfaceData data);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref InterfaceData data, IntPtr detail,
                                                               int size, out int required, IntPtr info);

    [DllImport("setupapi.dll")]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security,
                                                    uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[]? input, int inputSize,
                                               byte[]? output, int outputSize, out int returned, IntPtr overlapped);

    // HID_IN_CTL_CODE(100) and HID_OUT_CTL_CODE(100) of hidclass.h.
    private const uint IoctlSetFeature = 0x000B0191;
    private const uint IoctlGetFeature = 0x000B0192;

    private const int DigcfPresent = 0x02;
    private const int DigcfDeviceInterface = 0x10;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint ShareReadWrite = 0x03;
    private const uint OpenExisting = 3;
    private const uint FlagOverlapped = 0x40000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct InterfaceData
    {
        public int Size;
        public Guid ClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawCaps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 27)]
        public ushort[] Rest;
    }
}
