//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using SystemSpinnerX64.Diagnostics;
using SystemSpinnerX64.Platform;

namespace SystemSpinnerX64.Devices;

// Intercepting the media keys: the volume ones as virtual keys through a keyboard hook, the
// brightness ones as raw HID input — Windows makes no virtual key of those at all.
internal sealed class MediaKeyMonitor : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int HcAction = 0;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyUp = 0x0105;
    private const int WmInput = 0x00FF;

    private const int VkVolumeMute = 0xAD;
    private const int VkVolumeDown = 0xAE;
    private const int VkVolumeUp = 0xAF;

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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetKeyNameTextW(int lParam, [Out] char[] text, int size);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    // Who decides what to do with a key.
    public Func<MediaKey, MediaKeyResult>? Handler { get; set; }

    // Writes every key the hook and the raw input see into the log: what arrived, under which
    // code, and whether a program put it there. A line per press, so off by default.
    public bool Trace { get; set; }

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

    // The window WM_INPUT is addressed to. It is only an address; there is no reason to show it.
    // HWND_MESSAGE (-3) — a window with no screen presence, alive for its message queue.
    private HwndSource Messages()
    {
        _messages ??= new HwndSource(new HwndSourceParameters(AppParameters.Identity.MessageWindow)
        {
            ParentWindow = new IntPtr(-3)
        });

        // Once: added twice, the same hook would run the handler twice for every message.
        if (!_hooked)
        {
            _messages.AddHook(OnWindowMessage);
            _hooked = true;
        }

        return _messages;
    }

    // The keys HID carries but Windows makes no virtual key of — the brightness ones. They never
    // reach the keyboard hook, so this is the only place they can be read at all.
    public void StartMediaUsages()
    {
        if (_raw) return;

        _raw = RawInput.Listen(Messages().Handle);
    }

    private bool _raw;

    // --- The stand-in for keys the keyboard has not got ---

    private const int WmHotKey = 0x0312;
    private const int HotKeyDown = 1;
    private const int HotKeyUp = 2;

    private bool _hotKeys;

    // Told once, when the keyboard turns out to have brightness keys of its own after all.
    public Action? NativeKeysSeen { get; set; }

    // Registers the pair from the config. Both keys or neither: half a pair would dim the screen
    // with no way back.
    public bool StartBrightnessKeys(HotKeySpec spec)
    {
        if (_hotKeys) return true;

        IntPtr window = Messages().Handle;
        uint modifiers = spec.Modifiers | HotKeySpec.ModNoRepeat;

        if (!RegisterHotKey(window, HotKeyDown, modifiers, (uint)spec.DownKey))
        {
            Log.Warn($"{spec.Describe} is taken by something else — the brightness keys are not registered");
            return false;
        }

        if (!RegisterHotKey(window, HotKeyUp, modifiers, (uint)spec.UpKey))
        {
            UnregisterHotKey(window, HotKeyDown);
            Log.Warn($"{spec.Describe} is taken by something else — the brightness keys are not registered");
            return false;
        }

        _hotKeys = true;
        return true;
    }

    // Given up as soon as the real keys show themselves: two ways to the same place, one of them
    // holding a combination other applications could use.
    public void StopBrightnessKeys()
    {
        if (!_hotKeys) return;

        IntPtr window = Messages().Handle;
        UnregisterHotKey(window, HotKeyDown);
        UnregisterHotKey(window, HotKeyUp);
        _hotKeys = false;
    }

    private IntPtr OnKey(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code != HcAction) return CallNextHookEx(_hook, code, wParam, lParam);

        int message = (int)wParam;

        // Before the filter below: the trace is there for the keys this class does not know.
        if (Trace) Describe(message, lParam);

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
        return result == MediaKeyResult.PassThrough
            ? CallNextHookEx(_hook, code, wParam, lParam)
            : new IntPtr(1);
    }

    // One line per key: what it was, under which code, and what was held with it.
    private void Describe(int message, IntPtr lParam)
    {
        if (message is not (WmKeyDown or WmSysKeyDown or WmKeyUp or WmSysKeyUp)) return;

        try
        {
            var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);

            bool down = message is WmKeyDown or WmSysKeyDown;
            bool extended = (data.flags & 0x01) != 0;
            bool injected = (data.flags & 0x10) != 0;

            var line = new StringBuilder();
            line.Append(down ? "down " : "up   ");
            line.Append($"vk 0x{data.vkCode:X2} ({KeyName(data)})");
            line.Append($", scan 0x{data.scanCode:X2}");
            if (extended) line.Append(", extended");
            if (injected) line.Append(", injected by a program");

            string held = Held();
            if (held.Length > 0) line.Append(", held: ").Append(held);

            Log.Key(line.ToString());
        }
        catch (Exception ex)
        {
            // The trace must never be what breaks the hook: without the hook the volume keys stop.
            System.Diagnostics.Debug.WriteLine($"the key was not described: {ex.Message}");
        }
    }

    // The name Windows itself puts on the key — "F2", "Volume Up" — in the keyboard layout in use.
    private static string KeyName(KbdLlHookStruct data)
    {
        try
        {
            // GetKeyNameText takes the scan code where WM_KEYDOWN keeps it: bits 16 to 23, with
            // the extended flag at bit 24.
            int lParam = (int)(data.scanCode << 16) | ((data.flags & 0x01) != 0 ? 1 << 24 : 0);

            var text = new char[64];
            int length = GetKeyNameTextW(lParam, text, text.Length);

            if (length > 0) return new string(text, 0, length);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"the key name was not read: {ex.Message}");
        }

        // Keys the layout has no name for — the media and brightness ones among them.
        return Known(data.vkCode);
    }

    // Windows names none of these, and a bare number says nothing.
    private static string Known(uint virtualKey) => virtualKey switch
    {
        0xAD => "Volume Mute",
        0xAE => "Volume Down",
        0xAF => "Volume Up",
        0xB0 => "Media Next",
        0xB1 => "Media Previous",
        0xB2 => "Media Stop",
        0xB3 => "Media Play/Pause",
        0xB4 => "Launch Mail",
        0xB5 => "Launch Media",
        0xB6 => "Launch App 1",
        0xB7 => "Launch App 2",
        0xA6 => "Browser Back",
        0xA7 => "Browser Forward",
        0xFF => "reserved by the driver — the key is handled without a code of its own",
        _ => "unnamed"
    };

    private static string Held()
    {
        var parts = new List<string>();
        if (Down(0x11)) parts.Add("Ctrl");
        if (Down(0x12)) parts.Add("Alt");
        if (Down(0x10)) parts.Add("Shift");
        if (Down(0x5B) || Down(0x5C)) parts.Add("Win");
        return string.Join("+", parts);
    }

    // The high bit says the key is down right now.
    private static bool Down(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    // A key read from the raw input. Nothing is taken away from anyone: raw input is a copy, and
    // the press goes its own way regardless.
    private void OnRawInput(IntPtr message)
    {
        RawInput.Press? press = RawInput.Read(message);
        if (press is null) return;

        if (Trace) Log.Key($"HID  {press}");

        // Only the brightness keys are taken from the raw input. Volume arrives twice — as a usage
        // and as the virtual key the hook already handles — and acting on both would move it two
        // steps per press.
        foreach (ushort usage in press.Usages)
        {
            MediaKey? key = usage switch
            {
                RawInput.BrightnessIncrement => MediaKey.BrightnessUp,
                RawInput.BrightnessDecrement => MediaKey.BrightnessDown,
                _ => null
            };

            if (key is null) continue;

            // The keyboard has the real thing, so the stand-in combination is handed back to
            // whoever else wants it.
            if (_hotKeys)
            {
                StopBrightnessKeys();
                NativeKeysSeen?.Invoke();
            }

            try { Handler?.Invoke(key.Value); }
            catch (Exception ex) { Log.Error($"handling the {key} key failed", ex); }
        }
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmInput) OnRawInput(lParam);

        if (msg == WmHotKey && (int)wParam is HotKeyDown or HotKeyUp)
        {
            MediaKey key = (int)wParam == HotKeyUp ? MediaKey.BrightnessUp : MediaKey.BrightnessDown;

            if (Trace) Log.Key($"HOT  {key}");

            try { Handler?.Invoke(key); }
            catch (Exception ex) { Log.Error($"handling the {key} key failed", ex); }
        }

        // Never marked as handled: raw input is a copy of the press, and stopping the message here
        // would only keep the window from doing what it does with the rest of them.
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        if (_raw)
        {
            RawInput.Stop();
            _raw = false;
        }

        // Before the window goes: a hotkey outlives the process that registered it only as far as
        // the window it was registered on.
        StopBrightnessKeys();

        if (_messages is not null)
        {
            _messages.RemoveHook(OnWindowMessage);
            _hooked = false;
            _messages.Dispose();
            _messages = null;
        }
    }
}
