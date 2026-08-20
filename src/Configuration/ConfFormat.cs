using System.Linq;
using SystemSpinnerX64.Diagnostics;
using SystemSpinnerX64.Localization;
using SystemSpinnerX64.Spinner;

namespace SystemSpinnerX64.Configuration;

/// <summary>
/// Settings to text and back. The parameter descriptions live here rather than in a separate
/// document: the file is edited by hand and the note has to sit next to the value. The same code
/// produces <c>sample.conf</c> in the project root, so the sample cannot drift away from what
/// the program actually reads.
///
/// A missing parameter always means "keep the default". So the file can be cut down to a couple
/// of lines, and new parameters do not break old files.
///
/// The sections follow the program rather than its two faces: [General] is what the whole app
/// does, [Hardware] is everything read from the machine and the thresholds for it,
/// [AppearanceFullScreen] is the overlay, [Spinner] is the tray icon. Settings that were tuned
/// once and never needed changing are not in the file at all — they stay as defaults in the
/// configuration classes.
/// </summary>
internal static class ConfFormat
{
    private const string General = "General";
    private const string Hardware = "Hardware";
    private const string AppearanceSection = "AppearanceFullScreen";
    private const string SpinnerSection = "Spinner";

    /// <summary>Milliseconds in a second: the file speaks seconds, the timers milliseconds.</summary>
    private const double Second = 1000.0;

    public static AppConfig Read(string text)
    {
        ConfFile file = ConfFile.Parse(text);
        var cfg = new AppConfig();

        cfg.Language = file.Choice<Language>(General, nameof(cfg.Language)) ?? cfg.Language;

        if (file.Number(General, "UpdateInterval") is double seconds)
            cfg.UpdateIntervalMs = (int)(seconds * Second);

        cfg.ShowOverlayInGames = file.Flag(General, nameof(cfg.ShowOverlayInGames)) ?? cfg.ShowOverlayInGames;
        cfg.SpinOnDesktop = file.Flag(General, nameof(cfg.SpinOnDesktop)) ?? cfg.SpinOnDesktop;

        // A missing log level differs from a configured one: the rule "log the first run in full,
        // write Warn to the file" rests on that.
        cfg.LogLevel = file.Choice<LogLevel>(General, nameof(cfg.LogLevel));


        OsdConfig osd = cfg.Osd;
        osd.AlwaysUseCustomOsd = file.Flag(General, nameof(osd.AlwaysUseCustomOsd)) ?? osd.AlwaysUseCustomOsd;
        osd.AdjustmentSteps = file.Whole(General, "AdjustmentStepsOsd") ?? osd.AdjustmentSteps;
        osd.BrightnessUpKey = file.Text(General, nameof(osd.BrightnessUpKey)) ?? osd.BrightnessUpKey;
        osd.BrightnessDownKey = file.Text(General, nameof(osd.BrightnessDownKey)) ?? osd.BrightnessDownKey;

        StatsConfig stats = cfg.Stats;
        stats.ShowExternalAddress = file.Flag(General, nameof(stats.ShowExternalAddress)) ?? stats.ShowExternalAddress;
        stats.HistoryPoints = file.Whole(General, "DetailHistoryPoints") ?? stats.HistoryPoints;
        stats.TopProcesses = file.Whole(General, "DetailTopProcesses") ?? stats.TopProcesses;

        cfg.GpuIndex = file.Whole(Hardware, nameof(cfg.GpuIndex)) ?? cfg.GpuIndex;

        SensorNamesConfig s = cfg.Sensors;
        s.CpuLoad = file.List(Hardware, nameof(s.CpuLoad)) ?? s.CpuLoad;
        s.CpuTemp = file.List(Hardware, nameof(s.CpuTemp)) ?? s.CpuTemp;
        s.CpuPower = file.List(Hardware, nameof(s.CpuPower)) ?? s.CpuPower;
        s.CpuClockCores = file.Text(Hardware, nameof(s.CpuClockCores)) ?? s.CpuClockCores;
        s.MemoryUsed = file.List(Hardware, nameof(s.MemoryUsed)) ?? s.MemoryUsed;
        s.MemoryAvailable = file.List(Hardware, nameof(s.MemoryAvailable)) ?? s.MemoryAvailable;
        s.GpuLoad = file.List(Hardware, nameof(s.GpuLoad)) ?? s.GpuLoad;
        s.GpuTemp = file.List(Hardware, nameof(s.GpuTemp)) ?? s.GpuTemp;
        s.GpuPower = file.List(Hardware, nameof(s.GpuPower)) ?? s.GpuPower;
        s.GpuClock = file.List(Hardware, nameof(s.GpuClock)) ?? s.GpuClock;
        s.GpuMemory = file.List(Hardware, nameof(s.GpuMemory)) ?? s.GpuMemory;
        s.GpuMemoryTotal = file.List(Hardware, nameof(s.GpuMemoryTotal)) ?? s.GpuMemoryTotal;

        FanConfig f = cfg.Fans;
        f.Cpu = file.List(Hardware, "CpuFan") ?? f.Cpu;
        f.Aio = file.List(Hardware, "AioFan") ?? f.Aio;
        f.Gpu = file.List(Hardware, "GpuFan") ?? f.Gpu;
        f.Extra = file.List(Hardware, "ExtraFan") ?? f.Extra;
        f.AverageCpu = file.Flag(Hardware, "AverageCpuFan") ?? f.AverageCpu;
        f.AverageAio = file.Flag(Hardware, "AverageAioFan") ?? f.AverageAio;
        f.AverageGpu = file.Flag(Hardware, "AverageGpuFan") ?? f.AverageGpu;

        WarnConfig n = cfg.Warn;
        n.Color = file.Text(Hardware, "WarnColor") ?? n.Color;
        n.CpuTemp = file.Number(Hardware, "WarnCpuTemp") ?? n.CpuTemp;
        n.GpuTemp = file.Number(Hardware, "WarnGpuTemp") ?? n.GpuTemp;
        n.SysMem = file.Percent(Hardware, "WarnSysMem") ?? n.SysMem;
        n.GpuMem = file.Percent(Hardware, "WarnGpuMem") ?? n.GpuMem;
        n.Swap = file.Percent(Hardware, "WarnSwap") ?? n.Swap;

        AppearanceConfig a = cfg.Appearance;
        a.FontFamily = file.Text(AppearanceSection, nameof(a.FontFamily)) ?? a.FontFamily;
        a.FontScalePercent = file.Number(AppearanceSection, nameof(a.FontScalePercent)) ?? a.FontScalePercent;
        a.UnitSizePercent = file.Number(AppearanceSection, nameof(a.UnitSizePercent)) ?? a.UnitSizePercent;
        a.Margin = file.Number(AppearanceSection, nameof(a.Margin)) ?? a.Margin;
        a.TextColor = file.Text(AppearanceSection, nameof(a.TextColor)) ?? a.TextColor;
        a.TextOpacity = file.Number(AppearanceSection, nameof(a.TextOpacity)) ?? a.TextOpacity;
        a.ShowPanel = file.Flag(AppearanceSection, nameof(a.ShowPanel)) ?? a.ShowPanel;
        a.PanelColor = file.Text(AppearanceSection, nameof(a.PanelColor)) ?? a.PanelColor;
        a.PanelOpacity = file.Number(AppearanceSection, nameof(a.PanelOpacity)) ?? a.PanelOpacity;

        SpinnerConfig sp = cfg.Spinner;
        sp.Style = file.Text(SpinnerSection, nameof(sp.Style)) ?? sp.Style;
        sp.Effect = file.Choice<SpinnerEffect>(SpinnerSection, nameof(sp.Effect)) ?? sp.Effect;
        sp.InvertRotation = file.Flag(SpinnerSection, nameof(sp.InvertRotation)) ?? sp.InvertRotation;

        return cfg;
    }

    public static string Write(AppConfig cfg)
    {
        var w = new ConfFile.Writer();

        w.Note("System Spinner x64 settings.",
               "",
               "Lines starting with a hash are comments. Removing a parameter is safe: the",
               "default then applies, shown in parentheses.",
               "",
               "Lists are comma-separated and tried in order — the first one present in the",
               "system wins.");

        OsdConfig o = cfg.Osd;
        StatsConfig st = cfg.Stats;
        w.Section(General);

        w.Note("Language of the application: Auto, En, Ru, Ar, Zh, Fr, De, It or Ja (Auto).",
               "Auto follows the system language and falls back to English.",
               "This file and the log are always in English.")
         .Value(nameof(cfg.Language), cfg.Language.ToString()).Blank();

        w.Note("Update interval for the data and the sensors, seconds (1, 1.5, 2, 3).",
               "Below one second it is not accepted: polling wakes the driver, and the app must",
               "not eat what it measures.")
         .Value("UpdateInterval", cfg.UpdateIntervalMs / Second).Blank();

        w.Note("Show the overlay while the active window covers the whole screen (true).",
               "false leaves only the tray icon — that is, the spinner half of the app alone.",
               "The full-screen test is loose: a video player counts as well.")
         .Value(nameof(cfg.ShowOverlayInGames), cfg.ShowOverlayInGames).Blank();

        w.Note("Keep the tray icon spinning outside full-screen applications (true). false",
               "leaves the first frame standing still until a game starts: the animation is",
               "cheap, but on a laptop every wake-up costs battery.")
         .Value(nameof(cfg.SpinOnDesktop), cfg.SpinOnDesktop).Blank();

        w.Note("What to write to SystemSpinnerX64.log next to this file: Info, Warn or Error",
               "(Warn). Reasons for a refused startup are always written, whatever this is set",
               "to. With the parameter absent the whole run is logged as Info — as on the first",
               "start.")
         .Value(nameof(cfg.LogLevel), (cfg.LogLevel ?? LogLevel.Warn).ToString()).Blank();

        w.Note("Always show the custom OSD when brightness or volume changes (false). Otherwise,",
               "when there is nothing to control, the key is handed back to Windows and it draws",
               "its own panel.")
         .Value(nameof(o.AlwaysUseCustomOsd), o.AlwaysUseCustomOsd).Blank();

        w.Note("How many steps the OSD scale is divided into (8, 16, 24, 32). One key press is",
               "one step, and the ticks under the bar show exactly this number.")
         .Value("AdjustmentStepsOsd", o.AdjustmentSteps).Blank();

        w.Note("Key combinations for brightness. Windows has no brightness keys of its own: on",
               "laptops the firmware handles them and they never reach an application, so an",
               "ordinary combination is used instead. The defaults mirror the Mac layout —",
               "F1 down, F2 up. Write off to give brightness back to the system.",
               "Format: Ctrl+Alt+F2, Win+Shift+Up, Ctrl+PageUp.")
         .Value(nameof(o.BrightnessUpKey), o.BrightnessUpKey)
         .Value(nameof(o.BrightnessDownKey), o.BrightnessDownKey).Blank();

        w.Note("Look up the external address through checkip.dyndns.org (true). This is the only",
               "request the app makes to the network; turning it off leaves the local address.")
         .Value(nameof(st.ShowExternalAddress), st.ShowExternalAddress).Blank();

        w.Note("How much history the charts keep and how many processes the list shows.",
               "900 points at a one-second interval is a quarter of an hour.")
         .Value("DetailHistoryPoints", st.HistoryPoints)
         .Value("DetailTopProcesses", st.TopProcesses);

        SensorNamesConfig s = cfg.Sensors;
        FanConfig f = cfg.Fans;
        WarnConfig n = cfg.Warn;
        w.Section(Hardware);

        w.Note("Names of the sensors the readings come from. You need to change them when some",
               "values show a dash: the naming depends on the hardware and the library version.",
               "The app writes the full list visible on your machine to the log at",
               "LogLevel = Info, and to sensors-found.txt next to this file.",
               "",
               "The CPU names are the Intel ones: this build targets Intel processors, and startup",
               "stops with an explanation on anything else. The GPU names are shared — Intel,",
               "NVIDIA and AMD all report load, temperature and clock the same way.").Blank();

        w.Note("Which GPU to show when there are several (0). The discrete one comes first.")
         .Value(nameof(cfg.GpuIndex), cfg.GpuIndex).Blank();

        w.Note("CPU load. CPU Total is the average over all threads: on a 24-thread CPU a game",
               "using eight threads shows about 15 %. CPU Core Max is the busiest core, closer",
               "to what in-game overlays report. This value also drives the spinner speed.")
         .Value(nameof(s.CpuLoad), s.CpuLoad).Blank();

        w.Note("CPU temperature and power.")
         .Value(nameof(s.CpuTemp), s.CpuTemp)
         .Value(nameof(s.CpuPower), s.CpuPower).Blank();

        w.Note("CPU clock — averaged over the cores whose sensor name holds this word. Efficient",
               "cores are left out: they run at their own, lower clock and would drag it down.")
         .Value(nameof(s.CpuClockCores), s.CpuClockCores).Blank();

        w.Note("Memory. Used goes on the overlay; the free part only feeds the status window,",
               "where the two together make the scale: how much of the installed memory is taken.")
         .Value(nameof(s.MemoryUsed), s.MemoryUsed)
         .Value(nameof(s.MemoryAvailable), s.MemoryAvailable).Blank();

        w.Note("GPU: load, temperature, power, clock.")
         .Value(nameof(s.GpuLoad), s.GpuLoad)
         .Value(nameof(s.GpuTemp), s.GpuTemp)
         .Value(nameof(s.GpuPower), s.GpuPower)
         .Value(nameof(s.GpuClock), s.GpuClock).Blank();

        w.Note("Video memory. GPU Memory Used is what the whole card holds; put",
               "D3D Dedicated Memory Used first if you only want what the game itself takes.",
               "The total is only used by the status window: without it the megabytes have",
               "nothing to be compared against, and a scale without a ceiling is meaningless.")
         .Value(nameof(s.GpuMemory), s.GpuMemory)
         .Value(nameof(s.GpuMemoryTotal), s.GpuMemoryTotal).Blank();

        w.Note("Names of the fan sensors. They fill in by themselves on the first run: every",
               "board names its headers differently, so they cannot be guessed in advance. While",
               "all three lists are empty the app rescans the hardware; once the file has",
               "something, it uses that. To force a rescan, clear the three lists below and",
               "start the app again.").Blank();

        w.Note("The CPU cooler, followed by case fans as fallbacks.")
         .Value("CpuFan", f.Cpu).Blank();

        w.Note("The AIO pump. Empty means the cell is not shown at all: a dash would say «the",
               "sensor is silent», while an air cooler simply has no pump. Auto-detection never",
               "fills this in, but nothing stops you from typing a name yourself.")
         .Value("AioFan", f.Aio).Blank();

        w.Note("GPU fans.")
         .Value("GpuFan", f.Gpu).Blank();

        w.Note("Your own fans — one extra cell per name, at the end of the CPU row. This is where",
               "case fans, the PSU fan or a second pump header go. Auto-detection never touches",
               "this list, and a rescan does not overwrite it.")
         .Value("ExtraFan", f.Extra).Blank();

        w.Note("Show the average over every name in the list instead of the first one found.",
               "Without the flag the list is an order of preference; with it, a group to merge.",
               "Enabled for the GPU: it has two or three fans but only one cell.")
         .Value("AverageCpuFan", f.AverageCpu)
         .Value("AverageAioFan", f.AverageAio)
         .Value("AverageGpuFan", f.AverageGpu).Blank();

        w.Note("Highlighting for values past a threshold. Zero disables a threshold, so you can",
               "keep temperatures only. One colour serves them all: the point of the highlight is",
               "to catch the eye, and one difference from the rest is enough.")
         .Value("WarnColor", n.Color).Blank();

        w.Note("Temperatures, °C (85 and 83). They colour the values on the overlay and the",
               "temperature scales in the status window.")
         .Value("WarnCpuTemp", n.CpuTemp)
         .Value("WarnGpuTemp", n.GpuTemp).Blank();

        w.Note("Memory, per cent of the total (90 % each). These colour the three memory scales in",
               "the status window: the installed memory, the video memory and the page file.")
         .Value("WarnSysMem", n.SysMem, "%")
         .Value("WarnGpuMem", n.GpuMem, "%")
         .Value("WarnSwap", n.Swap, "%");

        AppearanceConfig a = cfg.Appearance;
        w.Section(AppearanceSection);

        w.Note("How the overlay looks. Nothing here touches the tray icon or the status window:",
               "those follow the system theme, and a game has no theme to follow.").Blank();

        w.Note("Panel font. Several comma-separated names — the first one present in the system",
               "is used. Impact is chosen for density: a long row of values fits over the game.")
         .Value(nameof(a.FontFamily), a.FontFamily).Blank();

        w.Note("Font size as a percentage of the computed one (100), from 50 to 300. The size",
               "does not depend on the resolution: Windows scaling is already accounted for.")
         .Value(nameof(a.FontScalePercent), a.FontScalePercent).Blank();

        w.Note("Size of the unit labels (%, °C, RPM) as a percentage of the values (55).")
         .Value(nameof(a.UnitSizePercent), a.UnitSizePercent).Blank();

        w.Note("Panel offset from the top-left corner of the screen (10).")
         .Value(nameof(a.Margin), a.Margin).Blank();

        w.Note("Colour and opacity of the text. The colour is #RRGGBB or a name such as White,",
               "Gold, LightGreen. Values, labels and row tags share one colour and differ only in",
               "opacity: a multicoloured panel over a game reads worse than one colour at",
               "several densities.")
         .Value(nameof(a.TextColor), a.TextColor)
         .Value(nameof(a.TextOpacity), a.TextOpacity).Blank();

        w.Note("Dark backdrop behind the panel (off). Over a game it reads as a rectangle across",
               "the picture, so it is off by default. Worth enabling on a busy background.")
         .Value(nameof(a.ShowPanel), a.ShowPanel)
         .Value(nameof(a.PanelColor), a.PanelColor)
         .Value(nameof(a.PanelOpacity), a.PanelOpacity);

        SpinnerConfig sp = cfg.Spinner;
        w.Section(SpinnerSection);

        w.Note("The tray icon outside full-screen applications. Its speed is the whole way this",
               "app shows CPU load: the busier the processor, the faster the animation runs.",
               "Every set is also in the tray menu, and picking one there writes it here.").Blank();

        w.Note("Name of the frame set (Loader). Available sets:",
               "  " + string.Join(", ", SpinnerCatalog.All.Select(x => x.Name)))
         .Value(nameof(sp.Style), sp.Style).Blank();

        w.Note("Colouring: Original, White, Black or Auto (Original). Auto follows the taskbar",
               "theme — black on a light taskbar, white on a dark one. Sets that live by their",
               "own colours ignore this: a silhouette would turn them into a blob.")
         .Value(nameof(sp.Effect), sp.Effect.ToString()).Blank();

        w.Note("Spin the frames backwards (false).",
               "",
               "The exact number is in the tooltip under the pointer: the speed of the animation",
               "shows the load at a glance, and hovering shows the digits.")
         .Value(nameof(sp.InvertRotation), sp.InvertRotation);

        return w.ToString();
    }
}
