using System;
using System.Collections.Concurrent;
using System.Threading;
using SystemSpinnerX64.Diagnostics;

namespace SystemSpinnerX64.Devices;

// One queue for every monitor command: DDC is serial and will not take two at once, and each
// exchange takes tens of milliseconds — too long for the key hook to wait.
internal static class DdcQueue
{
    private static readonly BlockingCollection<Action> Work = new();
    private static readonly Lazy<Thread> Worker = new(StartWorker, LazyThreadSafetyMode.ExecutionAndPublication);

    // Queues a command and returns immediately.
    public static void Run(Action command)
    {
        _ = Worker.Value;

        try { Work.Add(command); }
        catch (InvalidOperationException) { /* queue closed: the app is exiting */ }
    }

    private static Thread StartWorker()
    {
        var thread = new Thread(Pump)
        {
            IsBackground = true, // exiting must not wait for a monitor
            Name = "DDC"
        };
        thread.Start();
        return thread;
    }

    private static void Pump()
    {
        foreach (Action command in Work.GetConsumingEnumerable())
        {
            try
            {
                command();
            }
            catch (Exception ex)
            {
                // One stubborn monitor must not stall the queue for the rest.
                Log.Error("a monitor command failed", ex);
            }
        }
    }

    // Closes the queue: what is already in it still runs, nothing new is accepted.
    public static void Stop()
    {
        try { Work.CompleteAdding(); }
        catch (ObjectDisposedException) { /* already closed */ }
    }
}
