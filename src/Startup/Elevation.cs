using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;

namespace SystemSpinnerX64.Startup;

/// <summary>
/// Administrator rights are required: without them the kernel driver will not load and the ETW
/// session will not start, so there would be no temperatures, no fan speeds and no FPS.
///
/// The manifest with requireAdministrator normally takes care of this; this class is the backstop
/// for when it did not (started via `dotnet SystemSpinnerX64.dll`, an unusual launcher).
/// </summary>
internal static class Elevation
{
    /// <summary>Whether the current process has administrator rights.</summary>
    public static bool IsElevated { get; } = CheckElevated();

    private static bool CheckElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex)
        {
            // Could not tell — assume no rights: an extra UAC prompt beats a panel of dashes.
            Debug.WriteLine($"the rights check failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Restarts the app elevated, passing the same arguments.</summary>
    /// <param name="problem">What to show the user when the restart did not happen.</param>
    /// <returns>true means the restart has begun and this copy should close.</returns>
    public static bool TryRelaunchElevated(out string? problem)
    {
        problem = null;

        // For single-file this is the path to the exe itself, not to the temporary unpack.
        string? exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            problem = "Could not determine the path to the overlay's own exe for a restart.\n\n" +
                      "Start SystemSpinnerX64.exe as administrator manually.";
            return false;
        }

        var start = new ProcessStartInfo(exe)
        {
            // Without ShellExecute the runas verb does nothing: the shell elevates, not the process.
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = AppContext.BaseDirectory
        };

        foreach (string arg in Environment.GetCommandLineArgs().Skip(1))
            start.ArgumentList.Add(arg);

        try
        {
            Process.Start(start);
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            problem = "Without administrator rights the overlay has nothing to show: " +
                      "temperatures, power, fan speeds and the frame counter are only readable " +
                      "with them.\n\nStart it again and confirm the UAC prompt.";
            return false;
        }
        catch (Exception ex)
        {
            problem = $"Could not restart with administrator rights: {ex.Message}\n\n" +
                      "Start SystemSpinnerX64.exe as administrator manually.";
            return false;
        }
    }

    /// <summary>ERROR_CANCELLED — the user dismissed the UAC prompt.</summary>
    private const int ErrorCancelled = 1223;
}
