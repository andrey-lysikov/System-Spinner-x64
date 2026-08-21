using System;
using System.Collections.Generic;
using System.Linq;
using SystemSpinnerX64.Diagnostics;
using SystemSpinnerX64.Localization;
using SystemSpinnerX64.Spinner;

namespace SystemSpinnerX64.Configuration;

// Settings to text and back. The parameter descriptions live here rather than in a separate
// document: the file is edited by hand and the note has to sit next to the value.
internal static class ConfFormat
{
    private const string General = "General";
    private const string Hardware = "Hardware";
    private const string AppearanceSection = "AppearanceFullScreen";
    private const string SpinnerSection = "Spinner";

    // Milliseconds in a second: the file speaks seconds, the timers milliseconds.
    private const double Second = 1000.0;

    // Overlay rows: Row1, Row2, … Nine is more rows than the panel has values to fill.
    private const string RowKey = "Row";
    private const int MaxRows = 9;

    public static AppConfig Read(string text)
    {
        ConfFile file = ConfFile.Parse(text);
        var cfg = new AppConfig();

        cfg.Language = file.Choice<Language>(General, nameof(cfg.Language)) ?? cfg.Language;

        if (file.Number(General, "UpdateInterval") is double seconds)
            cfg.UpdateIntervalMs = (int)(seconds * Second);

        cfg.ShowOverlayInGames = file.Flag(General, nameof(cfg.ShowOverlayInGames)) ?? cfg.ShowOverlayInGames;
        cfg.SpinOnDesktop = file.Flag(General, nameof(cfg.SpinOnDesktop)) ?? cfg.SpinOnDesktop;

        // A missing switch differs from one set to false: the rule "log the first run in full,
        // then write it off into the file" rests on that.
        cfg.Debug = file.Flag(General, nameof(cfg.Debug));


        OsdConfig osd = cfg.Osd;
        osd.AlwaysUseCustomOsd = file.Flag(General, nameof(osd.AlwaysUseCustomOsd)) ?? osd.AlwaysUseCustomOsd;
        osd.AdjustmentSteps = file.Whole(General, "AdjustmentStepsOsd") ?? osd.AdjustmentSteps;
        osd.ControlExternalBrightness = file.Flag(General, nameof(osd.ControlExternalBrightness)) ?? osd.ControlExternalBrightness;
        osd.ControlExternalVolume = file.Flag(General, nameof(osd.ControlExternalVolume)) ?? osd.ControlExternalVolume;
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
        n.SwapMem = file.Percent(Hardware, "WarnSwapMem") ?? n.SwapMem;
        n.CpuUsage = file.Percent(Hardware, "WarnCpuUsage") ?? n.CpuUsage;
        n.GpuUsage = file.Percent(Hardware, "WarnGpuUsage") ?? n.GpuUsage;

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
        a.ShadowBlur = file.Number(AppearanceSection, nameof(a.ShadowBlur)) ?? a.ShadowBlur;
        a.ShadowOpacity = file.Number(AppearanceSection, nameof(a.ShadowOpacity)) ?? a.ShadowOpacity;
        a.Rows = Rows(file) ?? a.Rows;

        SpinnerConfig sp = cfg.Spinner;
        sp.Style = file.Text(SpinnerSection, nameof(sp.Style)) ?? sp.Style;
        sp.Effect = file.Choice<SpinnerEffect>(SpinnerSection, nameof(sp.Effect)) ?? sp.Effect;
        sp.InvertRotation = file.Flag(SpinnerSection, nameof(sp.InvertRotation)) ?? sp.InvertRotation;

        return cfg;
    }

    // Row1, Row2, … up to MaxRows. Absent altogether means the defaults stand; present but empty
    // means that row is not shown, which is how a row is removed.
    private static List<OverlayRow>? Rows(ConfFile file)
    {
        List<OverlayRow>? rows = null;

        bool spoilt = false;

        for (int index = 1; index <= MaxRows; index++)
        {
            string key = RowKey + index;
            if (file.Text(AppearanceSection, key) is not string line) continue;

            rows ??= new List<OverlayRow>();

            if (OverlayRow.Parse(line, out string? problem) is OverlayRow row)
            {
                rows.Add(row);
            }
            else if (problem is not null)
            {
                Log.Warn($"[{AppearanceSection}] {key}: {problem}");
                spoilt = true;
            }
        }

        // Every row was a mistake — the panel would come up empty. Better the standard rows and
        // a line in the log than nothing at all over the game.
        if (spoilt && rows is { Count: 0 })
        {
            Log.Warn($"[{AppearanceSection}]: no row was understood — the standard ones are used");
            return null;
        }

        return rows;
    }

    public static string Write(AppConfig cfg)
    {
        var w = new ConfFile.Writer();

        w.Note(AppParameters.Identity.Name + " settings. Read once, at startup — restart after editing.");

        OsdConfig o = cfg.Osd;
        StatsConfig st = cfg.Stats;
        w.Section(General);

        w.Note("Interface language: Auto, En, Ru, Ar, Zh, Fr, De, It or Ja (Auto).")
         .Value(nameof(cfg.Language), cfg.Language.ToString()).Blank();

        w.Note("Poll interval, seconds (1). Less than one is refused.")
         .Value("UpdateInterval", cfg.UpdateIntervalMs / Second).Blank();

        w.Note("Show the panel over full-screen applications (true).")
         .Value(nameof(cfg.ShowOverlayInGames), cfg.ShowOverlayInGames).Blank();

        w.Note("Spin the tray icon while no full-screen application is running (true).")
         .Value(nameof(cfg.SpinOnDesktop), cfg.SpinOnDesktop).Blank();

        w.Note("Log the whole course of work, not just problems (false).",
               "The first run — the one that creates this file — is always logged in full.")
         .Value(nameof(cfg.Debug), cfg.Debug ?? false).Blank();

        w.Note("Use the app's own OSD even when there is nothing to control (false).")
         .Value(nameof(o.AlwaysUseCustomOsd), o.AlwaysUseCustomOsd).Blank();

        w.Note("Steps from zero to full for volume and brightness (16).")
         .Value("AdjustmentStepsOsd", o.AdjustmentSteps).Blank();

        w.Note("Drive an external monitor over DDC/CI, brightness and its own speakers (both on).")
         .Value(nameof(o.ControlExternalBrightness), o.ControlExternalBrightness)
         .Value(nameof(o.ControlExternalVolume), o.ControlExternalVolume).Blank();

        w.Note("Brightness keys: Windows has none of its own. Write off to leave it to the system.")
         .Value(nameof(o.BrightnessUpKey), o.BrightnessUpKey)
         .Value(nameof(o.BrightnessDownKey), o.BrightnessDownKey).Blank();

        w.Note("Look up the external address through checkip.dyndns.org (true).")
         .Value(nameof(st.ShowExternalAddress), st.ShowExternalAddress).Blank();

        w.Note("Chart points and process rows in the status window (500 and 12).")
         .Value("DetailHistoryPoints", st.HistoryPoints)
         .Value("DetailTopProcesses", st.TopProcesses);

        SensorNamesConfig s = cfg.Sensors;
        FanConfig f = cfg.Fans;
        WarnConfig n = cfg.Warn;
        w.Section(Hardware);

        w.Note("Sensor names the readings come from; the Intel and the AMD ones stand together.",
               "Change a name when its value shows a dash: what your machine reports goes to",
               "sensors-found.txt next to this file.").Blank();

        w.Note("Which GPU when there are several (0). The discrete one comes first.")
         .Value(nameof(cfg.GpuIndex), cfg.GpuIndex).Blank();

        w.Note("CPU load.")
         .Value(nameof(s.CpuLoad), s.CpuLoad).Blank();

        w.Note("CPU temperature and power.")
         .Value(nameof(s.CpuTemp), s.CpuTemp)
         .Value(nameof(s.CpuPower), s.CpuPower).Blank();

        w.Note("The clock is averaged over the cores whose sensor name holds this word.")
         .Value(nameof(s.CpuClockCores), s.CpuClockCores).Blank();

        w.Note("Memory used and free — together they make the scale in the status window.")
         .Value(nameof(s.MemoryUsed), s.MemoryUsed)
         .Value(nameof(s.MemoryAvailable), s.MemoryAvailable).Blank();

        w.Note("GPU: load, temperature, power, clock.")
         .Value(nameof(s.GpuLoad), s.GpuLoad)
         .Value(nameof(s.GpuTemp), s.GpuTemp)
         .Value(nameof(s.GpuPower), s.GpuPower)
         .Value(nameof(s.GpuClock), s.GpuClock).Blank();

        w.Note("Video memory used and total.")
         .Value(nameof(s.GpuMemory), s.GpuMemory)
         .Value(nameof(s.GpuMemoryTotal), s.GpuMemoryTotal).Blank();

        w.Note("Fan sensors, filled in on the first run. Clear all three lists to scan again.")
         .Value("CpuFan", f.Cpu)
         .Value("AioFan", f.Aio)
         .Value("GpuFan", f.Gpu).Blank();

        w.Note("Your own fans — one cell each, wherever ExtraFans stands in a row below.")
         .Value("ExtraFan", f.Extra).Blank();

        w.Note("Average the whole list instead of taking the first name found.")
         .Value("AverageCpuFan", f.AverageCpu)
         .Value("AverageAioFan", f.AverageAio)
         .Value("AverageGpuFan", f.AverageGpu).Blank();

        w.Note("Highlighting past a threshold; zero switches one off.")
         .Value("WarnColor", n.Color)
         .Value("WarnCpuTemp", n.CpuTemp)
         .Value("WarnGpuTemp", n.GpuTemp)
         .Value("WarnSysMem", n.SysMem, "%")
         .Value("WarnGpuMem", n.GpuMem, "%")
         .Value("WarnSwapMem", n.SwapMem, "%")
         .Value("WarnCpuUsage", n.CpuUsage, "%")
         .Value("WarnGpuUsage", n.GpuUsage, "%");

        AppearanceConfig a = cfg.Appearance;
        w.Section(AppearanceSection);

        w.Note("The panel over a game; the tray icon and the status window follow the system theme.")
         .Blank();

        w.Note("Panel font — the first of these names present in the system.")
         .Value(nameof(a.FontFamily), a.FontFamily).Blank();

        w.Note("Font size, per cent of the computed one (100), from 50 to 300.")
         .Value(nameof(a.FontScalePercent), a.FontScalePercent).Blank();

        w.Note("Unit labels, per cent of the values (55).")
         .Value(nameof(a.UnitSizePercent), a.UnitSizePercent).Blank();

        w.Note("Offset from the top-left corner of the screen (10).")
         .Value(nameof(a.Margin), a.Margin).Blank();

        w.Note("Text colour — #RRGGBB or a name such as White — and its opacity.")
         .Value(nameof(a.TextColor), a.TextColor)
         .Value(nameof(a.TextOpacity), a.TextOpacity).Blank();

        w.Note("Dark backdrop behind the panel (off).")
         .Value(nameof(a.ShowPanel), a.ShowPanel)
         .Value(nameof(a.PanelColor), a.PanelColor)
         .Value(nameof(a.PanelOpacity), a.PanelOpacity).Blank();

        w.Note("Shadow under the text (3 and 0.9), from 0 to 20. Zero switches it off.")
         .Value(nameof(a.ShadowBlur), a.ShadowBlur)
         .Value(nameof(a.ShadowOpacity), a.ShadowOpacity).Blank();

        w.Note("Rows of the panel, in the order shown: the tag before the colon, the values after.",
               "An empty parameter removes a row; any value can stand in any row.",
               "  " + string.Join(", ", Enum.GetNames<OverlayMetric>().Take(8)),
               "  " + string.Join(", ", Enum.GetNames<OverlayMetric>().Skip(8)),
               "ExtraFans is one cell per name in ExtraFan above.")
         .Values(RowKey, cfg.Appearance.Rows.Select(r => r.ToString()));

        SpinnerConfig sp = cfg.Spinner;
        w.Section(SpinnerSection);

        w.Note("The tray icon outside full-screen applications; its speed follows the busier of",
               "the processor and the card.",
               "A set of one frame — App Icon, say — simply stands still.",
               "  " + string.Join(", ", SpinnerCatalog.All.Select(x => x.Name)))
         .Value(nameof(sp.Style), sp.Style).Blank();

        w.Note("Colouring: Original, White, Black or Auto (Original). Sets that live by their own",
               "colours ignore it.")
         .Value(nameof(sp.Effect), sp.Effect.ToString()).Blank();

        w.Note("Spin the frames backwards (false).")
         .Value(nameof(sp.InvertRotation), sp.InvertRotation);

        return w.ToString();
    }
}
