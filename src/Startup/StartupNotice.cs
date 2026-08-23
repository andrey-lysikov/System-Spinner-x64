//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using SystemSpinnerX64.Diagnostics;
using SystemSpinnerX64.Localization;
using SystemSpinnerX64.Platform;

namespace SystemSpinnerX64.Startup;

// Says out loud that the app did not start. Everything else it has to say goes to the log, and
// a program that vanishes without a window looks broken rather than refused — so the reason goes
// to the Windows action centre as well, and a click on it opens the log.
internal static class StartupNotice
{
    // How long the icon stays alive for the notification to be raised and clicked.
    private static readonly TimeSpan Linger = TimeSpan.FromSeconds(30);

    // How long the tray icon is left to settle before the balloon is asked for.
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(700);

    public static void Show(string reason)
    {
        try
        {
            // The language of the system: the config may not have been read at all, and this is
            // the one moment the user is being told something without a window.
            Text.Use(Language.Auto);

            // Notifications are off in Windows: that is the user's decision, and a window forced
            // on them instead would be worse than silence. The reason stays in the log.
            if (!Notifications.AreEnabled())
            {
                Log.Warn("notifications are off in Windows — the reason for the refusal is in " +
                         "this log only");
                System.Windows.Application.Current?.Shutdown();
                return;
            }

            Icon picture = LoadIcon();

            var icon = new NotifyIcon
            {
                Icon = picture,
                Visible = true,
                Text = AppParameters.Identity.Name
            };

            string log = Log.Path ?? "";

            icon.BalloonTipTitle = AppParameters.Identity.Name;
            icon.BalloonTipText = Text.StartupFailed(reason);
            icon.BalloonTipIcon = ToolTipIcon.Warning;
            icon.BalloonTipClicked += (_, _) => Open(log);

            // Not straight away: the icon has just been handed to the tray, and a balloon asked
            // for in the same breath is dropped — the shell has not finished adding it yet.
            var delay = new System.Windows.Threading.DispatcherTimer { Interval = Settle };
            delay.Tick += (_, _) =>
            {
                delay.Stop();
                icon.ShowBalloonTip((int)Linger.TotalMilliseconds);
                Log.Warn($"startup notification shown: {reason}");
            };
            delay.Start();

            // The icon must outlive the message: hidden at once, and Windows drops the balloon
            // with it. A timer of its own, because there is no window loop to hang this on.
            var life = new System.Windows.Threading.DispatcherTimer { Interval = Linger };
            life.Tick += (_, _) =>
            {
                life.Stop();
                icon.Visible = false;
                icon.Dispose();

                // NotifyIcon does not free the icon itself, and the shared system one must not
                // be freed at all.
                if (!ReferenceEquals(picture, SystemIcons.Warning)) picture.Dispose();

                System.Windows.Application.Current?.Shutdown();
            };
            life.Start();
        }
        catch (Exception ex)
        {
            // The refusal itself is already in the log; failing to announce it changes nothing.
            Log.Warn($"the startup notification was not shown: {ex.Message}");
            System.Windows.Application.Current?.Shutdown();
        }
    }

    private static void Open(string log)
    {
        if (log.Length == 0 || !File.Exists(log)) return;

        try
        {
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{log}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"{log} did not open in the editor", ex);
        }
    }

    private static Icon LoadIcon()
    {
        try
        {
            using Stream? stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(AppParameters.Identity.IconResource);

            return stream is null ? SystemIcons.Warning : new Icon(stream, SystemInformation.SmallIconSize);
        }
        catch (Exception ex)
        {
            Log.Warn($"the notification icon did not load: {ex.Message}");
            return SystemIcons.Warning;
        }
    }
}
