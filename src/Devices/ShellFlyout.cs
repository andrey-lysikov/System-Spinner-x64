//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using SystemSpinnerX64.Diagnostics;

namespace SystemSpinnerX64.Devices;

// The panel Windows puts up for volume and brightness. The shell builds it at the moment of the
// press and takes it down again, so it is caught through a window event hook and put away: ours
// already shows the same thing, and for brightness the system one shows a number that means nothing.
internal static class ShellFlyout
{
    private const uint EventObjectShow = 0x8002;

    private const int ObjectIdWindow = 0;

    private const uint OutOfContext = 0x0000;

    // Our own panel appears at the same moment and must not be swept away with the system one.
    private const uint SkipOwnProcess = 0x0002;

    private const int Hide = 0;

    // A XAML island of the shell, above the taskbar. Found by watching what came up on a press.
    private const string PanelClass = "XamlExplorerHostIslandWindow";

    // Hiding alone loses a frame to every repeat of a held key. An empty region and no opacity are
    // properties of the window, so they survive being shown again — and unlike DWM cloaking, which
    // is refused for a window of another process, they may be set on someone else's window.
    private const int ExtendedStyle = -20;
    private const int Layered = 0x00080000;
    private const int AlphaOnly = 0x00000002;

    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr window,
                                       int objectId, int childId, uint thread, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint from, uint to, IntPtr module, WinEventProc callback,
                                                 uint process, uint thread, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr window);

    private delegate bool EnumProc(IntPtr window, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr window, [Out] char[] text, int size);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtrW(IntPtr window, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtrW(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(IntPtr window, uint key, byte alpha, uint flags);

    [DllImport("user32.dll")]
    private static extern int GetMessageW(out Message message, IntPtr window, uint first, uint last);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref Message message);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessageW(uint thread, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private const uint WmQuit = 0x0012;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Window;
        public uint Value;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint Time;
        public int X, Y;
    }

    // The hook holds a pointer to this: without a field of its own the collector would take it
    // away and the callback would one day be freed code.
    private static WinEventProc? _callback;

    private static IntPtr _hook;
    private static Thread? _thread;
    private static uint _threadId;

    // Every window put away: all of them have to be given back when the app quits.
    private static readonly List<IntPtr> Touched = new();

    // Starts listening. false means the panel stays as Windows draws it — the app works either way.
    public static bool Watch()
    {
        if (_thread is not null) return _hook != IntPtr.Zero;

        using var ready = new ManualResetEventSlim();

        // A thread of its own with its own queue. An out-of-context hook is delivered through the
        // message queue of the thread that set it, and on the interface thread it would wait
        // behind the animation and the readings — the very wait the flicker is made of.
        _thread = new Thread(() => Pump(ready))
        {
            IsBackground = true,
            Name = "shell-flyout"
        };

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        ready.Wait(TimeSpan.FromSeconds(5));

        // The panel may already exist — the shell keeps it between showings. Taken now, it never
        // gets the chance to flash before the first press is handled.
        Sweep();

        return _hook != IntPtr.Zero;
    }

    private static void Pump(ManualResetEventSlim ready)
    {
        _threadId = GetCurrentThreadId();
        _callback = OnShow;

        _hook = SetWinEventHook(EventObjectShow, EventObjectShow, IntPtr.Zero, _callback, 0, 0,
                                OutOfContext | SkipOwnProcess);

        if (_hook == IntPtr.Zero)
            Log.Warn("the window events were not hooked — the Windows panel will show alongside ours");
        else
            Log.Info("watching for the Windows volume and brightness panel");

        // The waiter gives up after a while and lets go of this: setting it then throws, and an
        // exception on a thread nobody watches takes the whole app down.
        try { ready.Set(); }
        catch (ObjectDisposedException) { }

        if (_hook == IntPtr.Zero) return;

        // Nothing is posted to this queue but the quit at the end; the hook is delivered through it.
        while (GetMessageW(out Message message, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessageW(ref message);
        }

        UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
    }

    private static void OnShow(IntPtr hook, uint eventType, IntPtr window,
                               int objectId, int childId, uint thread, uint time)
    {
        // Only whole windows. The shell raises this for every button and label inside them too.
        if (objectId != ObjectIdWindow || childId != 0 || window == IntPtr.Zero) return;

        try
        {
            if (!GetWindowRect(window, out Rect rect)) return;

            if (!IsFlyout(ProcessOf(window), ClassOf(window),
                          rect.Right - rect.Left, rect.Bottom - rect.Top))
            {
                return;
            }

            Take(window);
        }
        catch (Exception ex)
        {
            // An exception from a hook callback goes out through unmanaged code: it must not.
            System.Diagnostics.Debug.WriteLine($"a shown window was not looked at: {ex.Message}");
        }
    }

    // The panel as it stands right now. The shell keeps its window between showings, so one may
    // already be there — from before the app started, or from a press of a key it does not handle.
    private static void Sweep()
    {
        try
        {
            EnumWindows((window, _) =>
            {
                if (!GetWindowRect(window, out Rect rect)) return true;

                if (IsFlyout(ProcessOf(window), ClassOf(window),
                             rect.Right - rect.Left, rect.Bottom - rect.Top))
                {
                    Take(window);
                }

                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Log.Error("the windows were not walked for the system panel", ex);
        }
    }

    // Puts the panel away and remembers it for the giving back.
    private static void Take(IntPtr window)
    {
        lock (Touched)
            if (!Touched.Contains(window)) Touched.Add(window);

        Put(window);
    }

    // Puts one window out of sight and keeps it there. Hiding is the one that works at once, for
    // the frame before the region and the opacity take hold.
    private static void Put(IntPtr window)
    {
        // The system takes the region over when the call succeeds; when it does not, it is ours
        // to free, or every press leaks one.
        IntPtr region = CreateRectRgn(0, 0, 0, 0);
        if (SetWindowRgn(window, region, true) == 0) DeleteObject(region);

        IntPtr style = GetWindowLongPtrW(window, ExtendedStyle);
        SetWindowLongPtrW(window, ExtendedStyle, style | Layered);
        SetLayeredWindowAttributes(window, 0, 0, AlphaOnly);

        ShowWindow(window, Hide);
    }

    // Gives the window back the way it was found.
    private static void Restore(IntPtr window)
    {
        SetWindowRgn(window, IntPtr.Zero, true);

        IntPtr style = GetWindowLongPtrW(window, ExtendedStyle);
        SetWindowLongPtrW(window, ExtendedStyle, (IntPtr)(style.ToInt64() & ~(long)Layered));
    }

    private static bool IsFlyout(string process, string name, int width, int height)
    {
        // By class: a tooltip or a window preview is small and belongs to the shell too, and one
        // caught after a press would be left blank for good.
        if (name != PanelClass) return false;

        if (!process.Equals("explorer", StringComparison.OrdinalIgnoreCase) &&
            !process.Equals("ShellHost", StringComparison.OrdinalIgnoreCase) &&
            !process.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return width is > 1 and < 900 && height is > 1 and < 500;
    }

    private static string ClassOf(IntPtr window)
    {
        var text = new char[256];
        int length = GetClassNameW(window, text, text.Length);
        return length > 0 ? new string(text, 0, length) : "?";
    }

    private static string ProcessOf(IntPtr window)
    {
        try
        {
            GetWindowThreadProcessId(window, out uint id);
            return System.Diagnostics.Process.GetProcessById((int)id).ProcessName;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"the window's process was not named: {ex.Message}");
            return "?";
        }
    }

    public static void Stop()
    {
        // One left with an empty region stays blank for as long as the shell lives.
        lock (Touched)
        {
            foreach (IntPtr window in Touched)
                if (IsWindow(window)) Restore(window);

            Touched.Clear();
        }

        if (_threadId != 0) PostThreadMessageW(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);

        _thread?.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        _threadId = 0;
        _callback = null;
    }
}
