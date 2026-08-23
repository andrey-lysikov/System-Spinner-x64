//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Linq;
using System.Management;
using SystemSpinnerX64.Diagnostics;

namespace SystemSpinnerX64.Devices;

// Brightness of the built-in laptop panel.
internal static class InternalBrightness
{
    private const string Scope = @"root\WMI";

    // Seconds Windows gives the panel to fade.
    private const uint Instant = 0;

    // Current brightness in percent, or null when there is no built-in panel.
    public static double? Get()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(Scope, "SELECT * FROM WmiMonitorBrightness");
            using ManagementObjectCollection results = searcher.Get();

            foreach (ManagementBaseObject item in results)
            {
                using (item)
                {
                    // CurrentBrightness is already a percentage — the same number the slider shows.
                    if (item["CurrentBrightness"] is byte value) return value;
                }
            }
        }
        catch (ManagementException ex)
        {
            // The class is missing on a desktop: an ordinary case, not a failure.
            System.Diagnostics.Debug.WriteLine($"WmiMonitorBrightness is unavailable: {ex.Message}");
        }
        catch (Exception ex)
        {
            Log.Error("the built-in panel brightness was not read", ex);
        }

        return null;
    }

    // Sets the built-in panel brightness.
    public static bool Set(double percent)
    {
        byte value = (byte)Math.Clamp(Math.Round(percent), 0, 100);

        try
        {
            using var searcher = new ManagementObjectSearcher(Scope, "SELECT * FROM WmiMonitorBrightnessMethods");
            using ManagementObjectCollection results = searcher.Get();

            bool applied = false;
            foreach (ManagementBaseObject item in results)
            {
                if (item is not ManagementObject method) continue;

                using (method)
                {
                    method.InvokeMethod("WmiSetBrightness", new object[] { Instant, value });
                    applied = true;
                }
            }

            return applied;
        }
        catch (ManagementException ex)
        {
            System.Diagnostics.Debug.WriteLine($"WmiSetBrightness is unavailable: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Log.Error("the built-in panel brightness was not set", ex);
            return false;
        }
    }

    // Whether there is a built-in panel that can be driven at all.
    public static bool IsAvailable => Get() is not null;
}
