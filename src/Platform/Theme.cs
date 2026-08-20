using System;
using Microsoft.Win32;

namespace SystemSpinnerX64.Platform;

/// <summary>
/// Whether the taskbar is light or dark. The tray icon needs this: an automatic silhouette is
/// drawn in whichever colour shows up on the taskbar, and the taskbar can be light while the
/// windows are dark — Windows has two separate switches for that.
/// </summary>
internal static class Theme
{
    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>Whether the taskbar is light. Unknown counts as dark: that is the default.</summary>
    public static bool IsTaskbarLight()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);

            // SystemUsesLightTheme specifically: AppsUseLightTheme covers windows, not the taskbar.
            return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"the taskbar theme was not read: {ex.Message}");
            return false;
        }
    }

    /// <summary>Whether windows are dark — the switch the custom OSD is painted by.</summary>
    public static bool AreWindowsDark()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is not int value || value == 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"the app theme was not read: {ex.Message}");
            return true;
        }
    }
}
