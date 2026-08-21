using System;
using System.Runtime.InteropServices;

namespace SystemSpinnerX64.Platform;

// The blurred backdrop of Windows 11. This is the local answer to the Liquid Glass of the macOS
// version: the window has neither a background of its own nor an image — whatever is behind it is
// blurred, and the volume panel stays readable on a light desktop and on a dark one.
internal static class Dwm
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;

    // DWMSBT_TRANSIENTWINDOW — acrylic, the backdrop of popup windows.
    private const int BackdropAcrylic = 3;

    // DWMWCP_ROUND — the large rounding the system uses for popup menus.
    private const int CornerRound = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    // Asks the system for an acrylic backdrop, rounded corners and the dark or light set of system
    // colours.
    public static bool ApplyAcrylic(IntPtr hwnd, bool dark)
    {
        if (hwnd == IntPtr.Zero) return false;

        int darkMode = dark ? 1 : 0;
        Set(hwnd, DwmwaUseImmersiveDarkMode, ref darkMode);

        int corner = CornerRound;
        Set(hwnd, DwmwaWindowCornerPreference, ref corner);

        int backdrop = BackdropAcrylic;
        return Set(hwnd, DwmwaSystemBackdropType, ref backdrop);
    }

    private static bool Set(IntPtr hwnd, int attribute, ref int value)
    {
        try
        {
            return DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int)) == 0;
        }
        catch (Exception ex)
        {
            // dwmapi ships with every Windows 11, but an older build may lack the attribute.
            System.Diagnostics.Debug.WriteLine($"DWM attribute {attribute} was refused: {ex.Message}");
            return false;
        }
    }
}
