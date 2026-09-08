//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Interop;
using SystemSpinnerX64.Diagnostics;
using SystemSpinnerX64.Platform;

namespace SystemSpinnerX64.Devices;

// The pair of keys that stands in for the brightness keys a keyboard has not got, written the way
// a person writes it: "Ctrl+F1/F2" — dimmer first, brighter second.
internal readonly record struct HotKeySpec(uint Modifiers, int DownKey, int UpKey)
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    // Windows repeats a held hotkey by itself; without this one press would arrive many times.
    public const uint ModNoRepeat = 0x4000;

    private static readonly Dictionary<string, uint> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = ModControl, ["control"] = ModControl,
        ["alt"] = ModAlt,
        ["shift"] = ModShift,
        ["win"] = ModWin, ["windows"] = ModWin
    };

    // Never throws: a wrong line in the config leaves the keys unregistered and says why.
    public static HotKeySpec? Parse(string? text, out string? problem)
    {
        problem = null;

        text = text?.Trim() ?? "";
        if (text.Length == 0 || text.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;

        string[] parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            problem = $"\"{text}\" names no keys";
            return null;
        }

        uint modifiers = 0;

        foreach (string part in parts[..^1])
        {
            if (!Names.TryGetValue(part, out uint modifier))
            {
                problem = $"\"{part}\" is not Ctrl, Alt, Shift or Win";
                return null;
            }

            modifiers |= modifier;
        }

        // The keys themselves: "F1/F2", dimmer before brighter.
        string[] keys = parts[^1].Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (keys.Length != 2)
        {
            problem = $"\"{parts[^1]}\" is not a pair of keys like F1/F2";
            return null;
        }

        int? down = Key(keys[0]), up = Key(keys[1]);
        if (down is null || up is null)
        {
            problem = $"\"{parts[^1]}\" names a key that is not F1 to F24";
            return null;
        }

        if (down == up)
        {
            problem = $"\"{parts[^1]}\" is the same key twice";
            return null;
        }

        // A bare function key would be taken from every other application at once — F1 is help
        // and F2 renames things. Demanding a modifier costs nothing and saves the explanation.
        if (modifiers == 0)
        {
            problem = $"\"{text}\" has no Ctrl, Alt, Shift or Win, and a bare key would be taken " +
                      "from every application";
            return null;
        }

        return new HotKeySpec(modifiers, down.Value, up.Value);
    }

    // VK_F1 is 0x70 and the rest follow it in order, up to F24.
    private static int? Key(string name)
    {
        if (name.Length < 2 || (name[0] != 'F' && name[0] != 'f')) return null;

        return int.TryParse(name[1..], NumberStyles.None, CultureInfo.InvariantCulture, out int number)
               && number is >= 1 and <= 24
            ? 0x70 + number - 1
            : null;
    }

    // How it reads in the log: the same shape it has in the config.
    public string Describe =>
        string.Join("+", Named().Append($"F{DownKey - 0x70 + 1}/F{UpKey - 0x70 + 1}"));

    private IEnumerable<string> Named()
    {
        if ((Modifiers & ModControl) != 0) yield return "Ctrl";
        if ((Modifiers & ModAlt) != 0) yield return "Alt";
        if ((Modifiers & ModShift) != 0) yield return "Shift";
        if ((Modifiers & ModWin) != 0) yield return "Win";
    }
}

// Which key was pressed. The macOS set minus the keyboard backlight.
public enum MediaKey
{
    VolumeUp,
    VolumeDown,
    Mute,
    BrightnessUp,
    BrightnessDown
}

// What to do with the press. PassThrough means there was nothing to control and the key goes back
// to Windows, which then shows its own panel.
public enum MediaKeyResult
{
    PassThrough,
    Consumed
}

// Who answers a media key: the app, or Windows. The decision alone — nothing here touches a screen
// or the mixer, so the rules sit in one place and can be checked without hardware.
internal static class MediaKeyRules
{
    // Without a monitor driven over DDC, Windows does the job itself and shows its own panel.
    public static bool Takes(bool drivesOverDdc, bool alwaysCustomOsd) => drivesOverDdc || alwaysCustomOsd;

    // Nothing to move means the key goes back to Windows — unless our own panel was asked for in
    // every case, and then it comes up on the value as it stands.
    public static MediaKeyResult Brightness(bool drivesOverDdc, bool alwaysCustomOsd, bool targetFound)
    {
        if (!Takes(drivesOverDdc, alwaysCustomOsd)) return MediaKeyResult.PassThrough;

        if (!targetFound) return alwaysCustomOsd ? MediaKeyResult.Consumed : MediaKeyResult.PassThrough;

        return MediaKeyResult.Consumed;
    }

    public static MediaKeyResult Volume(bool moved, bool alwaysCustomOsd) =>
        moved || alwaysCustomOsd ? MediaKeyResult.Consumed : MediaKeyResult.PassThrough;
}

// Keys as HID sees them, before Windows turns them into virtual keys — which for the brightness
// ones it never does. The keyboard hook sees nothing of those; this is where they can be read.
internal static class RawInput
{
    // Brightness up and down on the consumer page. These are what the whole class is here for.
    public const ushort BrightnessIncrement = 0x006F;
    public const ushort BrightnessDecrement = 0x0070;

    // One report as it arrived. The bytes are for the trace, when a usage cannot be read.
    internal sealed class Press
    {
        public required IReadOnlyList<ushort> Usages { get; init; }
        public required string Report { get; init; }

        public override string ToString()
        {
            var named = new List<string>();
            foreach (ushort usage in Usages) named.Add($"usage 0x{usage:X4} ({Name(usage)})");

            return $"{string.Join(", ", named)}, report {Report}";
        }
    }

    // Consumer Control: the media and brightness keys of a keyboard live on this page.
    private const ushort ConsumerPage = 0x0C;
    private const ushort ConsumerControl = 0x01;

    // Deliver the input even when no window of ours is in the foreground — a tray app never is.
    private const uint InputSink = 0x00000100;

    // Takes the registration away again; the target must be nothing at all for this one.
    private const uint RemoveDevice = 0x00000001;

    private const uint RidInput = 0x10000003;
    private const uint RidiPreparsedData = 0x20000005;

    private const uint TypeHid = 2;

    // HidP_GetUsages answers with this when it worked.
    private const int HidpStatusSuccess = 0x00110000;

    // Input report.
    private const int HidpInput = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr wParam;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices([In] RawInputDevice[] devices, uint count, uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetRawInputData(IntPtr rawInput, uint command, IntPtr data,
        ref uint size, uint headerSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetRawInputDeviceInfoW(IntPtr device, uint command, IntPtr data, ref uint size);

    [DllImport("hid.dll")]
    private static extern int HidP_GetUsages(int reportType, ushort usagePage, ushort linkCollection,
        [Out] ushort[] usages, ref uint usageLength, IntPtr preparsedData, IntPtr report, uint reportLength);

    [DllImport("hid.dll")]
    private static extern int HidP_MaxUsageListLength(int reportType, ushort usagePage, IntPtr preparsedData);

    // Asks Windows to send the consumer keys of every keyboard to this window.
    public static bool Listen(IntPtr window)
    {
        var devices = new[]
        {
            new RawInputDevice
            {
                UsagePage = ConsumerPage,
                Usage = ConsumerControl,
                Flags = InputSink,
                Target = window
            }
        };

        if (RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawInputDevice>()))
        {
            Log.Info("raw input: the consumer keys of the keyboard are being watched");
            return true;
        }

        Log.Warn($"raw input was not registered, error {Marshal.GetLastWin32Error()} — " +
                 "the brightness keys cannot be seen");
        return false;
    }

    // Gives the registration back before the window it points at goes.
    public static void Stop()
    {
        var devices = new[]
        {
            new RawInputDevice
            {
                UsagePage = ConsumerPage,
                Usage = ConsumerControl,
                Flags = RemoveDevice,
                Target = IntPtr.Zero
            }
        };

        RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawInputDevice>());
    }

    // What arrived with a WM_INPUT. null: a report of another kind, or a key being released.
    public static Press? Read(IntPtr message)
    {
        IntPtr buffer = IntPtr.Zero;

        try
        {
            uint size = 0;
            uint headerSize = (uint)Marshal.SizeOf<RawInputHeader>();

            if (GetRawInputData(message, RidInput, IntPtr.Zero, ref size, headerSize) != 0 || size == 0)
                return null;

            buffer = Marshal.AllocHGlobal((int)size);

            if (GetRawInputData(message, RidInput, buffer, ref size, headerSize) != size) return null;

            var header = Marshal.PtrToStructure<RawInputHeader>(buffer);
            if (header.Type != TypeHid) return null;

            // RAWHID follows the header: the size of one report, how many of them, then the reports
            // themselves back to back.
            IntPtr hid = buffer + (int)headerSize;
            int reportSize = Marshal.ReadInt32(hid);
            int reportCount = Marshal.ReadInt32(hid, 4);
            IntPtr reports = hid + 8;

            if (reportSize <= 0 || reportCount <= 0) return null;

            var held = new List<ushort>();

            for (int i = 0; i < reportCount; i++)
            {
                // A report with nothing listed in it is the release of what was held.
                held.AddRange(Usages(header.Device, reports + i * reportSize, (uint)reportSize));
            }

            if (held.Count == 0) return null;

            return new Press { Usages = held, Report = Hex(reports, reportSize) };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"the raw input was not read: {ex.Message}");
            return null;
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
        }
    }

    // The consumer usages held down in this report.
    private static IReadOnlyList<ushort> Usages(IntPtr device, IntPtr report, uint reportSize)
    {
        IntPtr preparsed = IntPtr.Zero;

        try
        {
            uint size = 0;
            if (GetRawInputDeviceInfoW(device, RidiPreparsedData, IntPtr.Zero, ref size) != 0 || size == 0)
                return Array.Empty<ushort>();

            preparsed = Marshal.AllocHGlobal((int)size);
            if (GetRawInputDeviceInfoW(device, RidiPreparsedData, preparsed, ref size) <= 0)
                return Array.Empty<ushort>();

            int max = HidP_MaxUsageListLength(HidpInput, ConsumerPage, preparsed);
            if (max <= 0) return Array.Empty<ushort>();

            var usages = new ushort[max];
            uint length = (uint)max;

            if (HidP_GetUsages(HidpInput, ConsumerPage, 0, usages, ref length, preparsed, report, reportSize)
                != HidpStatusSuccess)
            {
                return Array.Empty<ushort>();
            }

            var held = new List<ushort>((int)length);
            for (int i = 0; i < length; i++) held.Add(usages[i]);

            return held;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"the HID usages were not read: {ex.Message}");
            return Array.Empty<ushort>();
        }
        finally
        {
            if (preparsed != IntPtr.Zero) Marshal.FreeHGlobal(preparsed);
        }
    }

    // The usages of the consumer page worth naming.
    private static string Name(ushort usage) => usage switch
    {
        0x006F => "Brightness Increment",
        0x0070 => "Brightness Decrement",
        0x0079 => "Keyboard Backlight Up",
        0x007A => "Keyboard Backlight Down",
        0x00B5 => "Media Next",
        0x00B6 => "Media Previous",
        0x00B7 => "Media Stop",
        0x00CD => "Media Play/Pause",
        0x00E2 => "Mute",
        0x00E9 => "Volume Up",
        0x00EA => "Volume Down",
        _ => "unnamed"
    };

    // The report byte for byte: all there is to go on when the usages cannot be read.
    private static string Hex(IntPtr report, int size)
    {
        var text = new StringBuilder(size * 3);

        for (int i = 0; i < size; i++)
        {
            if (i > 0) text.Append(' ');
            text.Append(Marshal.ReadByte(report, i).ToString("X2"));
        }

        return text.ToString();
    }
}

// The panel Windows puts up for volume and brightness, caught through a window event hook and put
// away: ours already shows the same thing, and for brightness its number means nothing.
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

    // Hiding alone loses a frame to every repeat of a held key. An empty region and no opacity
    // survive being shown again, and unlike DWM cloaking may be set on another process's window.
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

        // A thread of its own: an out-of-context hook is delivered through the queue of the thread
        // that set it, and on the interface thread it would wait behind the animation.
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
            if (!Win32.TryGetWindowRect(window, out Win32.RECT rect)) return;

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
                if (!Win32.TryGetWindowRect(window, out Win32.RECT rect)) return true;

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
            Win32.GetWindowThreadProcessId(window, out uint id);
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

        Log.Event("volume keys hooked");
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

        // Only the brightness keys are taken from the raw input: volume arrives both as a usage
        // and as the virtual key the hook handles, and acting on both moves it two steps.
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
