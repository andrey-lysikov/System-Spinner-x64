//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Management;
using SystemSpinnerX64.Diagnostics;

namespace SystemSpinnerX64.Monitoring;

// The page file — what the macOS version calls swap.
internal static class SwapMonitor
{
    private const double Megabyte = 1024.0;

    // Used and total page file in gigabytes, or null when there is no page file.
    public static (double UsedGb, double TotalGb)? Read()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT AllocatedBaseSize, CurrentUsage FROM Win32_PageFileUsage");
            using ManagementObjectCollection results = searcher.Get();

            double used = 0;
            double total = 0;

            // A machine can have several page files, one per drive: they add up into one number,
            // exactly as Task Manager shows them.
            foreach (ManagementBaseObject item in results)
            {
                using (item)
                {
                    if (item["AllocatedBaseSize"] is uint allocated) total += allocated / Megabyte;
                    if (item["CurrentUsage"] is uint current) used += current / Megabyte;
                }
            }

            return total > 0 ? (used, total) : null;
        }
        catch (ManagementException ex)
        {
            // The page file can be switched off entirely — an ordinary case, not a failure.
            System.Diagnostics.Debug.WriteLine($"Win32_PageFileUsage is unavailable: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            Log.Error("the page file usage was not read", ex);
            return null;
        }
    }
}
