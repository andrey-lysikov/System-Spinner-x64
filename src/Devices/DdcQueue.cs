using System;
using System.Collections.Concurrent;
using System.Threading;
using SystemSpinnerX64.Diagnostics;

namespace SystemSpinnerX64.Devices;

/// <summary>
/// One queue for every monitor command, for two reasons.
///
/// DDC is a serial bus inside the cable and will not take two commands at once: the monitor
/// answers with rubbish or stops answering at all. So, strictly one after another.
///
/// And a command takes tens of milliseconds while the caller is the key hook, which Windows
/// gives a fraction of a second to return. Miss that and the hook is switched off, taking the
/// volume keys with it.
/// </summary>
internal static class DdcQueue
{
    private static readonly BlockingCollection<Action> Work = new();
    private static readonly Lazy<Thread> Worker = new(StartWorker, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Queues a command and returns immediately.</summary>
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

    /// <summary>Closes the queue: what is already in it still runs, nothing new is accepted.</summary>
    public static void Stop()
    {
        try { Work.CompleteAdding(); }
        catch (ObjectDisposedException) { /* already closed */ }
    }
}
