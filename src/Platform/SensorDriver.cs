using System;
using Microsoft.Win32;

namespace SystemSpinnerX64.Platform;

/// <summary>
/// LibreHardwareMonitor 0.9.6 does not carry a driver inside it the way WinRing0 did: it expects
/// PawnIO — a separate signed driver with its own installer, into which the library merely loads
/// its modules.
///
/// Without it half the panel silently disappears: CPU temperature and power and every fan speed
/// except the one on the card. From outside that is indistinguishable from "there are no
/// sensors" — the library returns nothing either way, so the check is made here.
/// </summary>
internal static class SensorDriver
{
    /// <summary>The PawnIO installer leaves an entry in the list of installed programs.</summary>
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";

    public const string DownloadPage = "https://pawnio.eu";

    /// <summary>true means PawnIO is installed, null means it could not be established.</summary>
    public static bool? IsPawnIoInstalled()
    {
        try
        {
            // The key is written to the 64-bit view of the registry — opened explicitly, or
            // a 32-bit process would not find it.
            using RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using RegistryKey? key = root.OpenSubKey(UninstallKey);
            return key is not null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Explanation for the log, or null when the driver is in place. A panel where half the
    /// values are dashes misleads more than no panel at all, so this stops the startup.
    /// </summary>
    public static string? DescribeIfMissing()
    {
        if (IsPawnIoInstalled() != false) return null;

        return "THE PawnIO DRIVER IS NOT INSTALLED — the app will not start without it.\n" +
               $"Installer: {DownloadPage}\n" +
               "LibreHardwareMonitor uses this driver to read the CPU MSRs and the motherboard " +
               "monitoring chip. Without it there is no CPU temperature or power, and no fan " +
               "speeds for the CPU cooler, the pump or the case fans — half of both the overlay " +
               "and the statistics window would be dashes.\n" +
               "PawnIO is signed and works with Memory Integrity enabled, so there is no need " +
               "to turn it off. Start the app again once installed.";
    }
}
