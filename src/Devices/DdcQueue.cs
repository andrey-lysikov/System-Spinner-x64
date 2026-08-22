using System;
using System.Collections.Concurrent;
using System.Threading;
using SystemSpinnerX64.Diagnostics;

namespace SystemSpinnerX64.Devices;

// One queue for every monitor command: DDC is serial and each exchange takes tens of milliseconds
// — too long for the key hook to wait. A newer command replaces an older one waiting under the
// same name: a held key steps faster than a monitor answers, and the values in between are waste.
internal static class DdcQueue
{
    private static readonly ConcurrentDictionary<string, Action> Pending = new();
    private static readonly BlockingCollection<string> Waiting = new();
    private static readonly Lazy<Thread> Worker = new(StartWorker, LazyThreadSafetyMode.ExecutionAndPublication);

    // The name is what makes two commands the same thing. Reads and writes carry different ones,
    // or a read would swallow the write it was meant to follow.
    public static void Run(string name, Action command)
    {
        _ = Worker.Value;

        Pending[name] = command;

        try { Waiting.Add(name); }
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
        foreach (string name in Waiting.GetConsumingEnumerable())
        {
            // Nothing under that name: the stale ticket of a command that was overtaken.
            if (!Pending.TryRemove(name, out Action? command)) continue;

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
        try { Waiting.CompleteAdding(); }
        catch (ObjectDisposedException) { /* already closed */ }
    }
}
