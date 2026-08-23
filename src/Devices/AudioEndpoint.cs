//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Runtime.InteropServices;
using SystemSpinnerX64.Diagnostics;

namespace SystemSpinnerX64.Devices;

// Volume and mute of the default output device through Core Audio.
internal static class AudioEndpoint
{
    private const int ERender = 0;
    private const int RoleMultimedia = 1;
    private const uint ClsCtxAll = 23;

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        int RegisterEndpointNotificationCallback(IntPtr client);
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams,
                     [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        int OpenPropertyStore(uint access, out IPropertyStore store);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetState(out uint state);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        int GetCount(out uint count);
        int GetAt(uint index, out PropertyKey key);
        int GetValue(ref PropertyKey key, out PropVariant value);
        int SetValue(ref PropertyKey key, ref PropVariant value);
        int Commit();
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr notify);
        int UnregisterControlChangeNotify(IntPtr notify);
        int GetChannelCount(out uint count);
        int SetMasterVolumeLevel(float level, ref Guid eventContext);
        int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        int GetMasterVolumeLevel(out float level);
        int GetMasterVolumeLevelScalar(out float level);
        int SetChannelVolumeLevel(uint channel, float level, ref Guid eventContext);
        int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
        int GetChannelVolumeLevel(uint channel, out float level);
        int GetChannelVolumeLevelScalar(uint channel, out float level);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        int GetVolumeStepInfo(out uint step, out uint stepCount);
        int VolumeStepUp(ref Guid eventContext);
        int VolumeStepDown(ref Guid eventContext);
        int QueryHardwareSupport(out uint mask);
        int GetVolumeRange(out float min, out float max, out float increment);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    // The full PROPVARIANT is not needed here: one property is read, a string with the name.
    // Its size matters more than its fields, so it is declared by size with the one field used.
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort ValueType;
        [FieldOffset(8)] public IntPtr Pointer;
    }

    // PKEY_Device_FriendlyName — "Speakers (Realtek)" or a monitor name over HDMI.
    private static PropertyKey FriendlyNameKey => new()
    {
        FormatId = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
        PropertyId = 14
    };

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);

    private static Guid _noEvent = Guid.Empty;

    private static IAudioEndpointVolume? Open(out string name)
    {
        name = "";

        object? enumerator = null;
        IMMDevice? device = null;

        try
        {
            enumerator = new MMDeviceEnumerator();
            if (((IMMDeviceEnumerator)enumerator).GetDefaultAudioEndpoint(
                    ERender, RoleMultimedia, out device) != 0)
                return null;

            name = ReadName(device);

            Guid iid = typeof(IAudioEndpointVolume).GUID;
            if (device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out object instance) != 0) return null;

            return instance as IAudioEndpointVolume;
        }
        catch (Exception ex)
        {
            Log.Error("the default audio output was not opened", ex);
            return null;
        }
        finally
        {
            // The enumerator and the device have done their part; the volume interface stands on
            // its own. They are released here rather than left to the garbage collector — a held
            // volume key opens the endpoint dozens of times a second.
            Release(device);
            Release(enumerator);
        }
    }

    // Lets go of a COM object at once instead of waiting for a collection.
    private static void Release(object? comObject)
    {
        if (comObject is null || !Marshal.IsComObject(comObject)) return;

        try { Marshal.FinalReleaseComObject(comObject); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"a COM object was not released: {ex.Message}"); }
    }

    // Opens the output, hands it over and releases it whatever happens.
    private static T With<T>(Func<IAudioEndpointVolume, T> use, T whenUnavailable)
    {
        IAudioEndpointVolume? volume = Open(out _);
        if (volume is null) return whenUnavailable;

        try { return use(volume); }
        finally { Release(volume); }
    }

    private static string ReadName(IMMDevice device)
    {
        IPropertyStore? store = null;

        try
        {
            // STGM_READ = 0
            if (device.OpenPropertyStore(0, out store) != 0) return "";

            PropertyKey key = FriendlyNameKey;
            if (store.GetValue(ref key, out PropVariant value) != 0) return "";

            // VT_LPWSTR = 31
            string name = value.ValueType == 31 && value.Pointer != IntPtr.Zero
                ? Marshal.PtrToStringUni(value.Pointer) ?? ""
                : "";

            PropVariantClear(ref value);
            return name;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"the audio device name was not read: {ex.Message}");
            return "";
        }
        finally
        {
            Release(store);
        }
    }

    // Name of the default output device.
    public static string DefaultDeviceName()
    {
        IAudioEndpointVolume? volume = Open(out string name);
        Release(volume);
        return name;
    }

    // Volume of the default output device in percent, or null when there is none.
    public static double? Volume() =>
        With<double?>(volume =>
            volume.GetMasterVolumeLevelScalar(out float level) == 0 ? level * 100.0 : null, null);

    // Sets the volume in percent. false means no device, or it refused.
    public static bool SetVolume(double percent) =>
        With(volume =>
        {
            float level = (float)(Math.Clamp(percent, 0, 100) / 100.0);
            if (volume.SetMasterVolumeLevelScalar(level, ref _noEvent) != 0) return false;

            // Zero and mute are different states but sound the same: they are tied together, or
            // after turning the volume down to nothing the icon would still show sound as on.
            volume.SetMute(level <= 0, ref _noEvent);
            return true;
        }, false);

    // Toggles mute. Returns the new state, or null on refusal.
    public static bool? ToggleMute() =>
        With<bool?>(volume =>
        {
            if (volume.GetMute(out bool muted) != 0) return null;
            return volume.SetMute(!muted, ref _noEvent) == 0 ? !muted : null;
        }, null);
}
