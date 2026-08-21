using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using SystemSpinnerX64.Diagnostics;
using SystemSpinnerX64.Platform;

namespace SystemSpinnerX64.Devices;

// Intercepting the volume keys and the brightness combinations.
internal sealed class MediaKeyMonitor : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int HcAction = 0;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const int WmHotKey = 0x0312;

    private const int VkVolumeMute = 0xAD;
    private const int VkVolumeDown = 0xAE;
    private const int VkVolumeUp = 0xAF;

    private const int BrightnessUpId = 0xB01;
    private const int BrightnessDownId = 0xB02;

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookExW(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // Who decides what to do with a key.
    public Func<MediaKey, MediaKeyResult>? Handler { get; set; }

    // The delegate has to outlive the hook: the garbage collector does not know Windows refers to
    // it, and without this field the hook would one day call freed code.
    private readonly HookProc _callback;

    private IntPtr _hook;
    private HwndSource? _messages;
    private bool _hooked;

    public MediaKeyMonitor()
    {
        _callback = OnKey;
    }

    // Installs the volume key hook. false means volume stays with the system.
    public bool StartVolumeKeys()
    {
        if (_hook != IntPtr.Zero) return true;

        // WH_KEYBOARD_LL needs no hMod: the hook is global but the code runs in our process, and
        // Windows only requires a live message queue.
        _hook = SetWindowsHookExW(WhKeyboardLl, _callback, IntPtr.Zero, 0);

        if (_hook == IntPtr.Zero)
        {
            Log.Error($"the volume keys were not hooked, error {Marshal.GetLastWin32Error()} — " +
                      "Windows will show its own volume OSD");
            return false;
        }

        Log.Info("volume keys hooked");
        return true;
    }

    // Registers the brightness combinations.
    public string? StartBrightnessKeys(HotKey? up, HotKey? down)
    {
        if (up is null && down is null) return null;

        // The window is only an address for WM_HOTKEY messages; there is no reason to show it.
        // HWND_MESSAGE (-3) — a window with no screen presence, alive for its message queue.
        _messages ??= new HwndSource(new HwndSourceParameters(AppParameters.Identity.HotKeyWindow)
        {
            ParentWindow = new IntPtr(-3)
        });

        // Once: called twice, the same hook would run the handler twice for every press.
        if (!_hooked)
        {
            _messages.AddHook(OnWindowMessage);
            _hooked = true;
        }

        string? problem = null;
        if (up is not null &&
            !RegisterHotKey(_messages.Handle, BrightnessUpId, (uint)up.Modifiers, (uint)up.VirtualKey))
        {
            problem = Combine(problem, $"{up} is taken by another program");
        }

        if (down is not null &&
            !RegisterHotKey(_messages.Handle, BrightnessDownId, (uint)down.Modifiers, (uint)down.VirtualKey))
        {
            problem = Combine(problem, $"{down} is taken by another program");
        }

        if (problem is null)
            Log.Info($"brightness keys: {up?.ToString() ?? "off"} / {down?.ToString() ?? "off"}");

        return problem;
    }

    private static string Combine(string? first, string second) =>
        first is null ? second : first + "; " + second;

    private IntPtr OnKey(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code != HcAction) return CallNextHookEx(_hook, code, wParam, lParam);

        int message = (int)wParam;
        if (message is not (WmKeyDown or WmSysKeyDown)) return CallNextHookEx(_hook, code, wParam, lParam);

        var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);

        MediaKey key;
        switch (data.vkCode)
        {
            case VkVolumeUp: key = MediaKey.VolumeUp; break;
            case VkVolumeDown: key = MediaKey.VolumeDown; break;
            case VkVolumeMute: key = MediaKey.Mute; break;
            default: return CallNextHookEx(_hook, code, wParam, lParam);
        }

        // The hook runs on the thread pumping the message queue — ours. The handling happens right
        // here: Windows waits for the return and there is no going to another thread.
        MediaKeyResult result;
        try
        {
            result = Handler?.Invoke(key) ?? MediaKeyResult.PassThrough;
        }
        catch (Exception ex)
        {
            // An exception from here would kill the hook and the keys would stop working entirely.
            Log.Error($"handling the {key} key failed", ex);
            result = MediaKeyResult.PassThrough;
        }

        // A non-zero answer is what "do not pass it on" means — without it the system panel appears.
        return result == MediaKeyResult.Consumed
            ? new IntPtr(1)
            : CallNextHookEx(_hook, code, wParam, lParam);
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotKey) return IntPtr.Zero;

        MediaKey? key = (int)wParam switch
        {
            BrightnessUpId => MediaKey.BrightnessUp,
            BrightnessDownId => MediaKey.BrightnessDown,
            _ => null
        };

        if (key is null) return IntPtr.Zero;

        try { Handler?.Invoke(key.Value); }
        catch (Exception ex) { Log.Error($"handling the {key} key failed", ex); }

        handled = true;
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        if (_messages is not null)
        {
            UnregisterHotKey(_messages.Handle, BrightnessUpId);
            UnregisterHotKey(_messages.Handle, BrightnessDownId);
            _messages.RemoveHook(OnWindowMessage);
            _hooked = false;
            _messages.Dispose();
            _messages = null;
        }
    }
}
