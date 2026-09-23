//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SystemSpinnerX64.Diagnostics;

namespace SystemSpinnerX64.Monitoring;

// Tells when a display adapter comes or goes — which is what a graphics driver being installed,
// updated or reset looks like from here. It arrives the moment the device restarts, well before
// Windows gets round to saying the screen configuration changed: on the machine this was written
// for, the driver came back 1.8 s before that message, and the process died in between.
internal sealed class GpuDriverWatch : NativeWindow, IDisposable
{
    // GUID_DEVINTERFACE_DISPLAY_ADAPTER
    private static readonly Guid DisplayAdapter = new("5B45201D-F2F2-4F3B-85BB-30FF1F953599");

    private const int WmDeviceChange = 0x0219;
    private const int DbtDeviceArrival = 0x8000;
    private const int DbtDeviceRemoveComplete = 0x8004;
    private const int DbtDevtypDeviceInterface = 5;
    private const int DeviceNotifyWindowHandle = 0;

    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsExToolWindow = 0x00000080;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevBroadcastDeviceInterface
    {
        public int Size;
        public int DeviceType;
        public int Reserved;
        public Guid ClassGuid;
        public char Name;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr RegisterDeviceNotification(IntPtr recipient, ref DevBroadcastDeviceInterface filter,
                                                            int flags);

    [DllImport("user32.dll")]
    private static extern bool UnregisterDeviceNotification(IntPtr handle);

    private IntPtr _notification;

    // What happened, for the log: "a display adapter arrived", say. Raised on the UI thread.
    public event Action<string>? AdapterChanged;

    public GpuDriverWatch()
    {
        // A top-level window, kept out of sight and off the taskbar: that is what device
        // notifications are sure to reach.
        CreateHandle(new CreateParams
        {
            Caption = string.Empty,
            X = AppParameters.Layout.OffScreen,
            Y = AppParameters.Layout.OffScreen,
            Width = 0,
            Height = 0,
            Style = WsPopup,
            ExStyle = WsExToolWindow
        });

        var filter = new DevBroadcastDeviceInterface
        {
            Size = Marshal.SizeOf<DevBroadcastDeviceInterface>(),
            DeviceType = DbtDevtypDeviceInterface,
            ClassGuid = DisplayAdapter
        };

        _notification = RegisterDeviceNotification(Handle, ref filter, DeviceNotifyWindowHandle);

        // Not fatal: the change of the screen configuration still comes, only later.
        if (_notification == IntPtr.Zero)
            Log.Warn($"display adapter notifications are unavailable (error {Marshal.GetLastWin32Error()}) — " +
                     "a graphics driver reload is noticed only by the screen configuration changing");
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmDeviceChange)
        {
            int kind = unchecked((int)(long)m.WParam);

            if (kind is DbtDeviceArrival or DbtDeviceRemoveComplete && IsAdapter(m.LParam))
                AdapterChanged?.Invoke(kind == DbtDeviceArrival ? "a display adapter arrived" : "a display adapter went away");
        }

        base.WndProc(ref m);
    }

    // The notification carries the class of the interface that changed; only adapters count.
    private static bool IsAdapter(IntPtr header)
    {
        if (header == IntPtr.Zero) return false;
        if (Marshal.ReadInt32(header, 4) != DbtDevtypDeviceInterface) return false;

        return Marshal.PtrToStructure<DevBroadcastDeviceInterface>(header).ClassGuid == DisplayAdapter;
    }

    public void Dispose()
    {
        if (_notification != IntPtr.Zero)
        {
            UnregisterDeviceNotification(_notification);
            _notification = IntPtr.Zero;
        }

        DestroyHandle();
    }
}
