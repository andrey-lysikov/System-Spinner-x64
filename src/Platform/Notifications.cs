using System;
using Microsoft.Win32;
using SystemSpinnerX64.Diagnostics;

namespace SystemSpinnerX64.Platform;

// Whether Windows will show a notification at all. Windows turns tray balloons into toasts, and
// with notifications switched off in Settings — or Do not disturb on — a balloon simply never
// appears: no error, no window, nothing. What the user has to be told anyway then goes into
// a dialog instead.
internal static class Notifications
{
    private const string PushKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\PushNotifications";

    public static bool AreEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PushKey);

            // No value means nobody has switched them off.
            return key?.GetValue("ToastEnabled") is not int enabled || enabled != 0;
        }
        catch (Exception ex)
        {
            Log.Warn($"the notification setting was not read: {ex.Message}");
            return true;
        }
    }
}
