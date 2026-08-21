using System;
using Microsoft.Win32;

namespace SystemSpinnerX64.Platform;

// LibreHardwareMonitor 0.9.6 does not carry a driver inside it the way WinRing0 did: it expects
// PawnIO — a separate signed driver with its own installer, into which the library merely loads its
// modules.
internal static class SensorDriver
{
    // The PawnIO installer leaves an entry in the list of installed programs.
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";

    // true means PawnIO is installed, null means it could not be established.
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

    // Explanation for the log, or null when the driver is in place.
    public static string? DescribeIfMissing()
    {
        if (IsPawnIoInstalled() != false) return null;

        return "THE PawnIO DRIVER IS NOT INSTALLED — the app will not start without it.\n" +
               $"Installer: {AppParameters.Links.SensorDriver}\n" +
               "LibreHardwareMonitor uses this driver to read the CPU MSRs and the motherboard " +
               "monitoring chip. Without it there is no CPU temperature or power, and no fan " +
               "speeds for the CPU cooler, the pump or the case fans — half of both the overlay " +
               "and the statistics window would be dashes.\n" +
               "PawnIO is signed and works with Memory Integrity enabled, so there is no need " +
               "to turn it off. Start the app again once installed.";
    }
}
