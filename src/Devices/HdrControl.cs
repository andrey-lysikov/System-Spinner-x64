//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SystemSpinnerX64.Diagnostics;

namespace SystemSpinnerX64.Devices;

// HDR on the attached screens. This does not go through DDC/CI: the switch lives in Windows itself,
// in the same display configuration the Settings app writes to.
internal static class HdrControl
{
    // One screen that can carry HDR, and whether it is on right now.
    internal sealed class HdrDisplay
    {
        public required string Name { get; init; }
        public required DisplayConfig.Luid AdapterId { get; init; }
        public required uint TargetId { get; init; }
        public required bool Enabled { get; init; }
    }

    // Windows 10 1709 and later: what advanced colour the screen carries and whether it is on.
    [StructLayout(LayoutKind.Sequential)]
    private struct AdvancedColorInfo
    {
        public DisplayConfig.DeviceInfoHeader Header;
        public uint Value;
        public uint ColorEncoding;
        public uint BitsPerColorChannel;

        public bool Supported => (Value & 0x1) != 0;
        public bool Enabled => (Value & 0x2) != 0;

        // The screen is capable but the system forbids the switch — a duplicated desktop, say.
        public bool ForceDisabled => (Value & 0x8) != 0;
    }

    // Windows 11 24H2 and later. There "advanced colour" also covers automatic colour management,
    // so HDR has bits of its own and must be read from them, not from advancedColorEnabled.
    [StructLayout(LayoutKind.Sequential)]
    private struct AdvancedColorInfo2
    {
        public DisplayConfig.DeviceInfoHeader Header;
        public uint Value;
        public uint ActiveColorMode;

        public bool LimitedByPolicy => (Value & 0x8) != 0;
        public bool HdrSupported => (Value & 0x10) != 0;
        public bool HdrEnabled => (Value & 0x20) != 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdvancedColorState
    {
        public DisplayConfig.DeviceInfoHeader Header;
        public uint Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HdrState
    {
        public DisplayConfig.DeviceInfoHeader Header;
        public uint Value;
    }

    private const uint GetAdvancedColorInfo = 9;
    private const uint SetAdvancedColorState = 10;
    private const uint GetAdvancedColorInfo2 = 17;
    private const uint SetHdrState = 18;

    private const int Success = DisplayConfig.Success;

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref AdvancedColorInfo request);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref AdvancedColorInfo2 request);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigSetDeviceInfo(ref AdvancedColorState request);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigSetDeviceInfo(ref HdrState request);

    // Every attached screen that can carry HDR, with the switch as it stands.
    public static IReadOnlyList<HdrDisplay> Capable()
    {
        var found = new List<HdrDisplay>();

        try
        {
            foreach (DisplayConfig.PathInfo path in DisplayConfig.ActivePaths())
            {
                DisplayConfig.Luid adapter = path.TargetInfo.AdapterId;
                uint target = path.TargetInfo.Id;
                uint technology = path.TargetInfo.OutputTechnology;

                string name = DisplayConfig.TargetName(adapter, target);
                if (name.Length == 0) name = "Display";

                (bool Supported, bool Enabled)? state = Read(adapter, target);

                // The whole decision on one line: what the screen hangs on, what it answered, and
                // what was made of it. Anything else and a menu entry that refuses to switch has
                // nothing behind it in the log.
                Log.Info($"HDR on \"{name}\": {DisplayConfig.TechnologyName(technology)}, " +
                         $"supported={Yes(state?.Supported)} on={Yes(state?.Enabled)}");

                // A screen with no output of the graphics card behind it is left out of the menu
                // rather than shown greyed out: the virtual display of a remote session claims
                // advanced colour and takes no switch, and an entry that never works is worse than
                // no entry at all — the "unavailable" line then says the true thing.
                if (DisplayConfig.IsVirtual(technology)) continue;

                if (state is not { Supported: true }) continue;

                found.Add(new HdrDisplay
                {
                    Name = name,
                    AdapterId = adapter,
                    TargetId = target,
                    Enabled = state.Value.Enabled
                });
            }
        }
        catch (Exception ex)
        {
            Log.Error("the HDR-capable screens were not enumerated", ex);
        }

        return found;
    }

    // Whether HDR is on for this screen. Asked afresh: two calls to user32, and the switch can be
    // thrown from our menu, the display settings or a game between one press and the next.
    public static bool IsOn(string gdiDevice)
    {
        if (gdiDevice.Length == 0) return false;

        try
        {
            foreach (DisplayConfig.PathInfo path in DisplayConfig.ActivePaths())
            {
                if (!DisplayConfig.SourceName(path.SourceInfo.AdapterId, path.SourceInfo.Id)
                                  .Equals(gdiDevice, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return Read(path.TargetInfo.AdapterId, path.TargetInfo.Id) is { Enabled: true };
            }
        }
        catch (Exception ex)
        {
            Log.Error($"HDR on \"{gdiDevice}\" was not read", ex);
        }

        return false;
    }

    // Turns HDR on or off. Windows refuses when the desktop is duplicated or the mode forbids it.
    public static bool Set(HdrDisplay display, bool on)
    {
        try
        {
            // 24H2 and later understand this one; older Windows answers it with an error.
            var hdr = new HdrState
            {
                Header = DisplayConfig.Header(SetHdrState, Marshal.SizeOf<HdrState>(),
                                              display.AdapterId, display.TargetId),
                Value = on ? 1u : 0u
            };

            if (DisplayConfigSetDeviceInfo(ref hdr) == Success) return true;

            var advanced = new AdvancedColorState
            {
                Header = DisplayConfig.Header(SetAdvancedColorState, Marshal.SizeOf<AdvancedColorState>(),
                                              display.AdapterId, display.TargetId),
                Value = on ? 1u : 0u
            };

            int result = DisplayConfigSetDeviceInfo(ref advanced);
            if (result == Success) return true;

            Log.Warn($"HDR on \"{display.Name}\" was not switched {(on ? "on" : "off")}: error {result}");
            return false;
        }
        catch (Exception ex)
        {
            Log.Error($"HDR on \"{display.Name}\" was not switched", ex);
            return false;
        }
    }

    // For the log: a screen that would not answer at all is not the same as one that answered no.
    private static string Yes(bool? value) => value is null ? "?" : value.Value ? "1" : "0";

    // Whether the screen carries HDR and whether it is on, or null when it cannot be asked.
    private static (bool Supported, bool Enabled)? Read(DisplayConfig.Luid adapter, uint target)
    {
        // The newer call first: from 24H2 on, "advanced colour is on" may mean colour management.
        var second = new AdvancedColorInfo2
        {
            Header = DisplayConfig.Header(GetAdvancedColorInfo2, Marshal.SizeOf<AdvancedColorInfo2>(),
                                          adapter, target)
        };

        if (DisplayConfigGetDeviceInfo(ref second) == Success)
            return (second.HdrSupported && !second.LimitedByPolicy, second.HdrEnabled);

        var first = new AdvancedColorInfo
        {
            Header = DisplayConfig.Header(GetAdvancedColorInfo, Marshal.SizeOf<AdvancedColorInfo>(),
                                          adapter, target)
        };

        if (DisplayConfigGetDeviceInfo(ref first) != Success) return null;

        return (first.Supported && !first.ForceDisabled, first.Enabled);
    }
}
