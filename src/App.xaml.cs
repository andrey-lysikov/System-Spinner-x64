using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using SystemSpinnerX64.Configuration;
using SystemSpinnerX64.Diagnostics;
using SystemSpinnerX64.Modes;
using SystemSpinnerX64.Startup;

namespace SystemSpinnerX64;

/// <summary>
/// Startup. The app shows no windows at all, neither on refusal nor on error: the panel hangs
/// over a game, and a window popping up mid-fight is worse than any unexplained trouble.
/// Everything goes to SystemSpinnerX64.log next to config.conf. Hence a failed start looks like
/// "nothing happened", and the reason is always in the log and always at ERROR, so it is visible
/// whatever LogLevel says.
///
/// The app closes only through the Quit item: it has no main window, and the tray icon does not
/// count — hence <c>ShutdownMode</c> of <c>OnExplicitShutdown</c>.
/// </summary>
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
            Stop("no administrator rights");
            return;
        }

        // Two copies cannot raise one ETW session or share the key hook — only the first starts.
        _instanceLock = new Mutex(initiallyOwned: true, "SystemSpinnerX64.SingleInstance", out bool first);
        if (!first)
        {
            Log.Error("System Spinner x64 is already running — a second copy is not needed, " +
                      "use the tray icon");
            Stop("already running");
            return;
        }

        // Every check and everything worth reporting happens here, before the tray icon appears.
        PreflightResult preflight = Preflight.Run();
        if (!preflight.CanStart)
        {
            Log.Error(preflight.Problem ?? "startup failed");
            Stop("startup checks did not pass");
            return;
        }

        _supervisor = new ModeSupervisor(preflight.Config!, preflight.Hardware!);
        _supervisor.Start();

        // --list-sensors is handled in Preflight: there it works even when the start did not happen.
    }

    /// <summary>
    /// Catches what nobody else caught. Without this a crash looks like the app simply vanishing:
    /// no windows, the tray icon gone and silence in the log, because the last line was written
    /// long before the failure.
    ///
    /// Three sources, and all three are needed: the UI thread has its own channel, other threads
    /// have another, and in a task the exception stays quiet until garbage collection.
    /// </summary>
    private void CatchEverything()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Crash("the UI thread", args.Exception);

            // Marked handled and closed deliberately: carrying on after an unknown failure means
            // showing something unknown. This way the tray still gets to remove the icon.
            args.Handled = true;
            Stop("unhandled exception");
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            // The process cannot be saved here — the point is to write the line in time.
            if (args.ExceptionObject is Exception ex) Log.Crash("a background thread", ex);
            else Log.Error($"UNHANDLED in a background thread: {args.ExceptionObject}");
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Crash("a task nobody awaited", args.Exception);
            args.SetObserved();
        };
    }

    private void Stop(string reason)
    {
        Log.Finish(reason);
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _supervisor?.Dispose();
        _instanceLock?.Dispose();
        base.OnExit(e);
    }
}
