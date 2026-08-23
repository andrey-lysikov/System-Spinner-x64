//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using SystemSpinnerX64.Configuration;
using SystemSpinnerX64.Diagnostics;
using SystemSpinnerX64.Localization;
using SystemSpinnerX64.Modes;
using SystemSpinnerX64.Startup;

namespace SystemSpinnerX64;

// Startup. The app shows no windows at all, neither on refusal nor on error: the panel hangs over a
// game, and a window popping up mid-fight is worse than any unexplained trouble.
public partial class App : Application
{
    private Mutex? _instanceLock;
    private ModeSupervisor? _supervisor;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // The log first of all: before it, no reason to refuse would go anywhere. The level is not
        // known yet, so everything is written; the configured one is applied in Preflight.
        Log.Start(AppConfig.ResolveDirectory(), AppConfig.FallbackDirectory);
        Log.Info($"arguments: {string.Join(" ", e.Args)}");

        CatchEverything();

        // Administrator rights before anything else, ahead of the mutex: the restarted copy must
        // not hit it while this one is still closing.
        if (!Elevation.IsElevated)
        {
            Log.Info("no administrator rights — restarting elevated");

            if (Elevation.TryRelaunchElevated(out string? elevationProblem))
            {
                Stop("restarting with administrator rights");
                return;
            }

            Log.Error(elevationProblem ?? "administrator rights are required");
            Stop("no administrator rights", Text.ReasonNoRights);
            return;
        }

        // Two copies cannot raise one ETW session or share the key hook — only the first starts.
        _instanceLock = new Mutex(initiallyOwned: true, AppParameters.Identity.SingleInstanceMutex, out bool first);
        if (!first)
        {
            Log.Error(AppParameters.Identity.Name +
                      " is already running — a second copy is not needed, use the tray icon");
            Stop("already running", Text.ReasonAlreadyRunning);
            return;
        }

        // Every check and everything worth reporting happens here, before the tray icon appears.
        PreflightResult preflight = Preflight.Run();
        if (!preflight.CanStart)
        {
            Log.Error(preflight.Problem ?? "startup failed");
            Stop("startup checks did not pass", Text.ReasonChecksFailed);
            return;
        }

        _supervisor = new ModeSupervisor(preflight.Config!, preflight.Hardware!);
        _supervisor.Start();

        // --list-sensors is handled in Preflight: there it works even when the start did not happen.
    }

    // Catches what nobody else caught.
    private void CatchEverything()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Crash("the UI thread", args.Exception);

            // Marked handled and closed deliberately: carrying on after an unknown failure means
            // showing something unknown. This way the tray still gets to remove the icon.
            args.Handled = true;
            Stop("unhandled exception", Text.ReasonCrashed);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            // The process cannot be saved here — the point is to write the line in time.
            if (args.ExceptionObject is Exception ex) Log.Crash("a background thread", ex);
            else Log.Error($"UNHANDLED in a background thread: {args.ExceptionObject}");

            // The Windows volume panel is kept out of sight by changing the shell's own window.
            // Nobody else will put it back, and the process is not going to reach OnExit.
            Devices.ShellFlyout.Stop();
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Crash("a task nobody awaited", args.Exception);
            args.SetObserved();
        };
    }

    // notice is what the user is told in the action centre; without it the app just closes.
    private void Stop(string reason, string? notice = null)
    {
        Log.Finish(reason);

        if (notice is not null) StartupNotice.Show(notice);
        else Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _supervisor?.Dispose();
        _instanceLock?.Dispose();
        base.OnExit(e);
    }
}
