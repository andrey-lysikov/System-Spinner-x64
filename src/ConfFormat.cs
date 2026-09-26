//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using SystemSpinnerX64.Diagnostics;
using SystemSpinnerX64.Lighting;
using SystemSpinnerX64.Localization;
using SystemSpinnerX64.Spinner;

namespace SystemSpinnerX64.Configuration;

// Sections in square brackets, "key = value" lines, comments from a hash.
internal sealed class ConfFile
{
    private readonly Dictionary<string, Dictionary<string, string>> _sections =
        new(StringComparer.OrdinalIgnoreCase);

    public static ConfFile Parse(string text)
    {
        var file = new ConfFile();
        Dictionary<string, string>? current = null;
        int number = 0;

        foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            number++;
            string line = raw.Trim();

            if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;

            if (line[0] == '[')
            {
                if (!line.EndsWith(']')) throw new FormatException($"line {number}: section without a closing bracket");

                string name = line[1..^1].Trim();
                if (name.Length == 0) throw new FormatException($"line {number}: section without a name");

                current = file._sections.TryGetValue(name, out var existing)
                    ? existing
                    : file._sections[name] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }

            int separator = line.IndexOf('=');
            if (separator < 0) throw new FormatException($"line {number}: neither a section nor \"key = value\"");
            if (current is null) throw new FormatException($"line {number}: value outside any section");

            string key = line[..separator].Trim();
            if (key.Length == 0) throw new FormatException($"line {number}: empty parameter name");

            current[key] = line[(separator + 1)..].Trim();
        }

        return file;
    }

    public bool HasSection(string section) => _sections.ContainsKey(section);

    private string? Raw(string section, string key) =>
        _sections.TryGetValue(section, out var values) && values.TryGetValue(key, out string? value) && value.Length > 0
            ? value
            : null;

    // The string, or null when the parameter is absent.
    public string? Text(string section, string key) => Raw(section, key);

    public bool? Flag(string section, string key) => Raw(section, key) switch
    {
        null => null,
        var v when v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1" => true,
        var v when v.Equals("false", StringComparison.OrdinalIgnoreCase) || v == "0" => false,
        var v => throw new FormatException($"[{section}] {key}: \"{v}\" — expected true or false")
    };

    public int? Whole(string section, string key)
    {
        string? value = Raw(section, key);
        if (value is null) return null;

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : throw new FormatException($"[{section}] {key}: \"{value}\" — expected a whole number");
    }

    public double? Number(string section, string key)
    {
        string? value = Raw(section, key);
        if (value is null) return null;

        // Both a dot and a comma: the file is edited by hand, and making a person remember the
        // English separator is one more way to get it wrong.
        return double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : throw new FormatException($"[{section}] {key}: \"{value}\" — expected a number");
    }

    // A number that may carry a trailing per cent sign: both "90" and "90 %" read as 90, because
    // a bare threshold next to a temperature is easy to misread.
    public double? Percent(string section, string key)
    {
        string? value = Raw(section, key);
        if (value is null) return null;

        string number = value.TrimEnd('%', ' ');
        return double.TryParse(number.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : throw new FormatException($"[{section}] {key}: \"{value}\" — expected a percentage");
    }

    // A comma-separated list. An empty string is an empty list, not "leave as is".
    public List<string>? List(string section, string key)
    {
        if (!_sections.TryGetValue(section, out var values) || !values.TryGetValue(key, out string? value)) return null;

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    public TEnum? Choice<TEnum>(string section, string key) where TEnum : struct, Enum
    {
        string? value = Raw(section, key);
        if (value is null) return null;

        return Enum.TryParse(value, ignoreCase: true, out TEnum parsed)
            ? parsed
            : throw new FormatException($"[{section}] {key}: \"{value}\" — allowed values are {string.Join(", ", Enum.GetNames<TEnum>())}");
    }

    // Building the file: sections, comments and values in the order they are written.
    internal sealed class Writer
    {
        private readonly StringBuilder _text = new();

        public Writer Section(string name)
        {
            if (_text.Length > 0) _text.AppendLine();
            _text.AppendLine($"[{name}]");
            return this;
        }

        // The note above a parameter — the very reason this format was chosen.
        public Writer Note(params string[] lines)
        {
            foreach (string line in lines) _text.AppendLine(line.Length == 0 ? "#" : $"# {line}");
            return this;
        }

        public Writer Value(string key, string value)
        {
            _text.AppendLine($"{key} = {value}");
            return this;
        }

        public Writer Value(string key, bool value) => Value(key, value ? "true" : "false");

        public Writer Value(string key, int value) => Value(key, value.ToString(CultureInfo.InvariantCulture));

        public Writer Value(string key, double value) =>
            Value(key, value.ToString("0.###", CultureInfo.InvariantCulture));

        // A number with a unit written after it, as in "90 %".
        public Writer Value(string key, double value, string unit) =>
            Value(key, value.ToString("0.###", CultureInfo.InvariantCulture) + " " + unit);

        public Writer Value(string key, IEnumerable<string> values) => Value(key, string.Join(", ", values));

        // Numbered keys of one kind: Row1, Row2, and so on.
        public Writer Values(string keyPrefix, IEnumerable<string> values)
        {
            int index = 1;
            foreach (string value in values) Value(keyPrefix + index++, value);
            return this;
        }

        public Writer Blank()
        {
            _text.AppendLine();
            return this;
        }

        public override string ToString() => _text.ToString();
    }
}

// Settings to text and back. The parameter descriptions live here rather than in a separate
// document: the file is edited by hand and the note has to sit next to the value.
internal static class ConfFormat
{
    private const string General = "General";
    private const string OverlaySection = "FullScreenOverlay";
    private const string EnableKey = "Enable";

    // Every section the file is meant to have; one that is missing is written back with defaults.
    public static readonly string[] Sections = { General, SensorsSection, OverlaySection, SpinnerSection, AuraSection };
    private const string SensorsSection = "Sensors";
    private const string SpinnerSection = "Spinner";
    private const string AuraSection = "Aura";

    // Milliseconds in a second: the file speaks seconds, the timers milliseconds.
    private const double Second = 1000.0;

    // Overlay rows: Row1, Row2, … Nine is more rows than the panel has values to fill.
    private const string RowKey = "Row";
    private const int MaxRows = 9;

    public static AppConfig Read(string text)
    {
        ConfFile file = ConfFile.Parse(text);
        var cfg = new AppConfig { MissingSections = Sections.Where(s => !file.HasSection(s)).ToList() };

        cfg.Language = file.Choice<Language>(General, nameof(cfg.Language)) ?? cfg.Language;

        if (file.Number(General, "UpdateInterval") is double seconds)
            cfg.UpdateIntervalMs = (int)(seconds * Second);

        cfg.SpinOnDesktop = file.Flag(General, nameof(cfg.SpinOnDesktop)) ?? cfg.SpinOnDesktop;

        // A missing switch differs from one set to false: the rule "log the first run in full,
        // then write it off into the file" rests on that.
        cfg.Debug = file.Flag(General, nameof(cfg.Debug));


        OsdConfig osd = cfg.Osd;
        osd.AlwaysUseCustomOsd = file.Flag(General, nameof(osd.AlwaysUseCustomOsd)) ?? osd.AlwaysUseCustomOsd;
        osd.AdjustmentSteps = file.Whole(General, "AdjustmentStepsOsd") ?? osd.AdjustmentSteps;
        osd.ControlExternalBrightness = file.Flag(General, nameof(osd.ControlExternalBrightness)) ?? osd.ControlExternalBrightness;
        osd.ControlExternalVolume = file.Flag(General, nameof(osd.ControlExternalVolume)) ?? osd.ControlExternalVolume;
        osd.BrightnessKeys = file.Text(General, nameof(osd.BrightnessKeys)) ?? osd.BrightnessKeys;

        StatsConfig stats = cfg.Stats;
        stats.ShowExternalAddress = file.Flag(General, nameof(stats.ShowExternalAddress)) ?? stats.ShowExternalAddress;
        stats.HistoryPoints = file.Whole(General, "DetailHistoryPoints") ?? stats.HistoryPoints;
        stats.TopProcesses = file.Whole(General, "DetailTopProcesses") ?? stats.TopProcesses;

        cfg.GpuIndex = file.Whole(General, nameof(cfg.GpuIndex)) ?? cfg.GpuIndex;

        List<string>? Sensor(string key) => file.List(SensorsSection, key);

        SensorNamesConfig s = cfg.Sensors;
        s.CpuLoad = Sensor(nameof(s.CpuLoad)) ?? s.CpuLoad;
        s.CpuTemp = Sensor(nameof(s.CpuTemp)) ?? s.CpuTemp;
        s.CpuPower = Sensor(nameof(s.CpuPower)) ?? s.CpuPower;
        s.CpuClockCores = file.Text(SensorsSection, nameof(s.CpuClockCores)) ?? s.CpuClockCores;
        s.RamUsed = Sensor(nameof(s.RamUsed)) ?? s.RamUsed;
        s.RamFree = Sensor(nameof(s.RamFree)) ?? s.RamFree;
        s.GpuLoad = Sensor(nameof(s.GpuLoad)) ?? s.GpuLoad;
        s.GpuTemp = Sensor(nameof(s.GpuTemp)) ?? s.GpuTemp;
        s.GpuPower = Sensor(nameof(s.GpuPower)) ?? s.GpuPower;
        s.GpuClock = Sensor(nameof(s.GpuClock)) ?? s.GpuClock;
        s.VramUsed = Sensor(nameof(s.VramUsed)) ?? s.VramUsed;
        s.VramTotal = Sensor(nameof(s.VramTotal)) ?? s.VramTotal;

        FanConfig f = cfg.Fans;
        f.Cpu = file.List(SensorsSection, "CpuFan") ?? f.Cpu;
        f.Aio = file.List(SensorsSection, "AioFan") ?? f.Aio;
        f.Gpu = file.List(SensorsSection, "GpuFan") ?? f.Gpu;
        f.Extra = file.List(SensorsSection, "ExtraFan") ?? f.Extra;
        f.AverageCpu = file.Flag(SensorsSection, "AverageCpuFan") ?? f.AverageCpu;
        f.AverageAio = file.Flag(SensorsSection, "AverageAioFan") ?? f.AverageAio;
        f.AverageGpu = file.Flag(SensorsSection, "AverageGpuFan") ?? f.AverageGpu;

        WarnConfig n = cfg.Warn;
        n.Color = file.Text(General, "WarnColor") ?? n.Color;
        n.WarnColorBy = file.Choice<WarnColorMode>(AuraSection, nameof(n.WarnColorBy)) ?? n.WarnColorBy;
        n.CpuTemp = file.Number(General, "WarnCpuTemp") ?? n.CpuTemp;
        n.GpuTemp = file.Number(General, "WarnGpuTemp") ?? n.GpuTemp;
        n.SysMem = file.Percent(General, "WarnSysMem") ?? n.SysMem;
        n.GpuMem = file.Percent(General, "WarnGpuMem") ?? n.GpuMem;
        n.SwapMem = file.Percent(General, "WarnSwapMem") ?? n.SwapMem;
        n.CpuUsage = file.Percent(General, "WarnCpuUsage") ?? n.CpuUsage;
        n.GpuUsage = file.Percent(General, "WarnGpuUsage") ?? n.GpuUsage;

        AppearanceConfig a = cfg.Appearance;
        cfg.ShowOverlayInGames = file.Flag(OverlaySection, EnableKey) ?? cfg.ShowOverlayInGames;

        a.FontFamily = file.Text(OverlaySection, nameof(a.FontFamily)) ?? a.FontFamily;
        a.FontScalePercent = file.Number(OverlaySection, nameof(a.FontScalePercent)) ?? a.FontScalePercent;
        a.UnitSizePercent = file.Number(OverlaySection, nameof(a.UnitSizePercent)) ?? a.UnitSizePercent;
        a.Margin = file.Number(OverlaySection, nameof(a.Margin)) ?? a.Margin;
        a.TextColor = file.Text(OverlaySection, nameof(a.TextColor)) ?? a.TextColor;
        a.TextOpacity = file.Number(OverlaySection, nameof(a.TextOpacity)) ?? a.TextOpacity;
        a.ShowPanel = file.Flag(OverlaySection, nameof(a.ShowPanel)) ?? a.ShowPanel;
        a.PanelColor = file.Text(OverlaySection, nameof(a.PanelColor)) ?? a.PanelColor;
        a.PanelOpacity = file.Number(OverlaySection, nameof(a.PanelOpacity)) ?? a.PanelOpacity;
        a.ShadowBlur = file.Number(OverlaySection, nameof(a.ShadowBlur)) ?? a.ShadowBlur;
        a.ShadowOpacity = file.Number(OverlaySection, nameof(a.ShadowOpacity)) ?? a.ShadowOpacity;
        a.Rows = Rows(file) ?? a.Rows;
        a.BlackListApplications = file.List(OverlaySection, nameof(a.BlackListApplications)) ?? a.BlackListApplications;

        SpinnerConfig sp = cfg.Spinner;
        sp.Style = file.Text(SpinnerSection, nameof(sp.Style)) ?? sp.Style;
        sp.Effect = file.Choice<SpinnerEffect>(SpinnerSection, nameof(sp.Effect)) ?? sp.Effect;
        sp.InvertRotation = file.Flag(SpinnerSection, nameof(sp.InvertRotation)) ?? sp.InvertRotation;
        sp.DimAbove = file.Number(SpinnerSection, nameof(sp.DimAbove)) ?? sp.DimAbove;
        sp.FullBelow = file.Number(SpinnerSection, nameof(sp.FullBelow)) ?? sp.FullBelow;
        sp.Sanitize();

        AuraConfig au = cfg.Aura;
        au.Enable = file.Flag(AuraSection, nameof(au.Enable)) ?? au.Enable;
        au.Color = file.Text(AuraSection, nameof(au.Color)) ?? au.Color;
        au.Effect = file.Choice<AuraEffect>(AuraSection, nameof(au.Effect)) ?? au.Effect;
        au.Speed = file.Whole(AuraSection, nameof(au.Speed)) ?? au.Speed;
        au.Brightness = file.Percent(AuraSection, nameof(au.Brightness)) ?? au.Brightness;
        au.VisibleFrom = file.Percent(AuraSection, nameof(au.VisibleFrom)) ?? au.VisibleFrom;
        au.WeatherCloud = file.Flag(AuraSection, nameof(au.WeatherCloud)) ?? au.WeatherCloud;
        au.LedsPerChannel = file.Whole(AuraSection, nameof(au.LedsPerChannel)) ?? au.LedsPerChannel;
        au.Sanitize();

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
            if (file.Text(OverlaySection, key) is not string line) continue;

            rows ??= new List<OverlayRow>();

            if (OverlayRow.Parse(line, out string? problem) is OverlayRow row)
            {
                rows.Add(row);
            }
            else if (problem is not null)
            {
                Log.Warn($"[{OverlaySection}] {key}: {problem}");
                spoilt = true;
            }
        }

        // Every row was a mistake — the panel would come up empty. Better the standard rows and
        // a line in the log than nothing at all over the game.
        if (spoilt && rows is { Count: 0 })
        {
            Log.Warn($"[{OverlaySection}]: no row was understood — the standard ones are used");
            return null;
        }

        return rows;
    }

    public static string Write(AppConfig cfg)
    {
        var w = new ConfFile.Writer();

        // By significance: what a person changes first at the top, the diagnostics at the bottom.
        OsdConfig o = cfg.Osd;
        StatsConfig st = cfg.Stats;
        SensorNamesConfig s = cfg.Sensors;
        FanConfig f = cfg.Fans;
        WarnConfig n = cfg.Warn;
        w.Section(General);

        w.Note("Language: Auto, En, Ru, Ar, Zh, Fr, De, It or Ja.")
         .Value(nameof(cfg.Language), cfg.Language.ToString()).Blank();

        w.Note("Spin the tray icon outside full-screen apps.")
         .Value(nameof(cfg.SpinOnDesktop), cfg.SpinOnDesktop).Blank();

        w.Note("Warning highlight: its colour and thresholds; 0 turns one off.")
         .Value("WarnColor", n.Color)
         .Value("WarnCpuTemp", n.CpuTemp)
         .Value("WarnGpuTemp", n.GpuTemp)
         .Value("WarnSysMem", n.SysMem, "%")
         .Value("WarnGpuMem", n.GpuMem, "%")
         .Value("WarnSwapMem", n.SwapMem, "%")
         .Value("WarnCpuUsage", n.CpuUsage, "%")
         .Value("WarnGpuUsage", n.GpuUsage, "%").Blank();

        w.Note("GPU to watch when there are several; 0 is the discrete one.")
         .Value(nameof(cfg.GpuIndex), cfg.GpuIndex).Blank();

        w.Note("Own OSD for the volume and brightness keys.")
         .Value(nameof(o.AlwaysUseCustomOsd), o.AlwaysUseCustomOsd).Blank();

        w.Note("Key presses from zero to full volume or brightness; with Alt a press moves 1 %.")
         .Value("AdjustmentStepsOsd", o.AdjustmentSteps).Blank();

        w.Note("Brightness keys for a keyboard that has none.")
         .Value(nameof(o.BrightnessKeys), o.BrightnessKeys).Blank();

        w.Note("Brightness and speakers of an external monitor over DDC/CI.")
         .Value(nameof(o.ControlExternalBrightness), o.ControlExternalBrightness)
         .Value(nameof(o.ControlExternalVolume), o.ControlExternalVolume).Blank();

        w.Note("External IP in the status window, asked from checkip.dyndns.org.")
         .Value(nameof(st.ShowExternalAddress), st.ShowExternalAddress).Blank();

        w.Note("Status window: chart points and process rows.")
         .Value("DetailHistoryPoints", st.HistoryPoints)
         .Value("DetailTopProcesses", st.TopProcesses).Blank();

        w.Note("Sensor poll interval, seconds, at least 1.")
         .Value("UpdateInterval", cfg.UpdateIntervalMs / Second).Blank();

        w.Note("Verbose log: every step, not only events and errors.")
         .Value(nameof(cfg.Debug), cfg.Debug ?? false);

        // Names of LibreHardwareMonitor sensors, not panel values: the log says which one each
        // key took. Grouped by device, the units said where they differ.
        w.Section(SensorsSection);

        w.Note("LibreHardwareMonitor sensor names, tried in order: exact match, then part of a name; the log shows which one each key took.")
         .Value(nameof(s.CpuLoad), s.CpuLoad)
         .Value(nameof(s.CpuTemp), s.CpuTemp)
         .Value(nameof(s.CpuPower), s.CpuPower).Blank();

        w.Note("CPU clock: average of the cores whose name has this word; none match — all but E-cores.")
         .Value(nameof(s.CpuClockCores), s.CpuClockCores).Blank();

        w.Note("RAM, GB: used and free; together they make the total.")
         .Value(nameof(s.RamUsed), s.RamUsed)
         .Value(nameof(s.RamFree), s.RamFree).Blank();

        w.Value(nameof(s.GpuLoad), s.GpuLoad)
         .Value(nameof(s.GpuTemp), s.GpuTemp)
         .Value(nameof(s.GpuPower), s.GpuPower)
         .Value(nameof(s.GpuClock), s.GpuClock).Blank();

        w.Note("Video memory, MB: used (the D3D counter matches Task Manager) and total.")
         .Value(nameof(s.VramUsed), s.VramUsed)
         .Value(nameof(s.VramTotal), s.VramTotal).Blank();

        w.Note("Fan sensors, found on the first run; clear all three to scan again.")
         .Value("CpuFan", f.Cpu)
         .Value("AioFan", f.Aio)
         .Value("GpuFan", f.Gpu).Blank();

        w.Note("Extra fans: one cell each where ExtraFans stands in a panel row.")
         .Value("ExtraFan", f.Extra).Blank();

        w.Note("Average every fan in the list instead of taking the first found.")
         .Value("AverageCpuFan", f.AverageCpu)
         .Value("AverageAioFan", f.AverageAio)
         .Value("AverageGpuFan", f.AverageGpu);

        AppearanceConfig a = cfg.Appearance;
        w.Section(OverlaySection);

        w.Note("The panel over full-screen games.")
         .Value(EnableKey, cfg.ShowOverlayInGames).Blank();

        w.Note("Font: the first installed one from the list.")
         .Value(nameof(a.FontFamily), a.FontFamily).Blank();

        w.Note("Font size, % of the automatic one, 50 to 300.")
         .Value(nameof(a.FontScalePercent), a.FontScalePercent).Blank();

        w.Note("Unit labels, % of the values.")
         .Value(nameof(a.UnitSizePercent), a.UnitSizePercent).Blank();

        w.Note("Offset from the top-left corner of the screen, pixels.")
         .Value(nameof(a.Margin), a.Margin).Blank();

        w.Note("Text colour, #RRGGBB or a name, and its opacity.")
         .Value(nameof(a.TextColor), a.TextColor)
         .Value(nameof(a.TextOpacity), a.TextOpacity).Blank();

        w.Note("Backdrop behind the panel: on or off, colour, opacity.")
         .Value(nameof(a.ShowPanel), a.ShowPanel)
         .Value(nameof(a.PanelColor), a.PanelColor)
         .Value(nameof(a.PanelOpacity), a.PanelOpacity).Blank();

        w.Note("Text shadow: blur 0 to 20, 0 is off, and opacity.")
         .Value(nameof(a.ShadowBlur), a.ShadowBlur)
         .Value(nameof(a.ShadowOpacity), a.ShadowOpacity).Blank();

        w.Note("Panel rows as \"Tag: values\", an empty one hidden; [Sensors] key names work too. Values: " +
               string.Join(", ", Enum.GetNames<OverlayMetric>()))
         .Values(RowKey, cfg.Appearance.Rows.Select(r => r.ToString())).Blank();

        w.Note("Full-screen apps the panel is never shown over.")
         .Value(nameof(a.BlackListApplications), a.BlackListApplications);

        // The first three keys speak for themselves; the sets and the effects are listed in the
        // menu. The sun keys do not, and they drive the lighting as well.
        SpinnerConfig sp = cfg.Spinner;
        w.Section(SpinnerSection);

        w.Value(nameof(sp.Style), sp.Style)
         .Value(nameof(sp.Effect), sp.Effect.ToString())
         .Value(nameof(sp.InvertRotation), sp.InvertRotation).Blank();

        w.Note("Sun elevation, degrees: the lighting comes up below DimAbove, full below FullBelow.")
         .Value(nameof(sp.DimAbove), sp.DimAbove)
         .Value(nameof(sp.FullBelow), sp.FullBelow);

        AuraConfig au = cfg.Aura;
        w.Section(AuraSection);

        w.Note("ARGB lighting that follows the sun: dark by day, up after sunset. Toggled in the menu.")
         .Value(nameof(au.Enable), au.Enable).Blank();

        w.Note("Colour of every LED, #RRGGBB or a name.")
         .Value(nameof(au.Color), au.Color).Blank();

        w.Note("Solid, Breathing or Rainbow; Speed of the last two, 1 to 10.")
         .Value(nameof(au.Effect), au.Effect.ToString())
         .Value(nameof(au.Speed), au.Speed).Blank();

        w.Note("Brightness after sunset.")
         .Value(nameof(au.Brightness), au.Brightness, "%").Blank();

        w.Note("Tint towards WarnColor: Off, Heat (temperatures), Load (usage) or Max (whichever is nearer).")
         .Value(nameof(n.WarnColorBy), n.WarnColorBy.ToString()).Blank();

        w.Note("Below this brightness the LEDs are off.")
         .Value(nameof(au.VisibleFrom), au.VisibleFrom, "%").Blank();

        w.Note("Go dark earlier on cloudy evenings, by Open-Meteo.")
         .Value(nameof(au.WeatherCloud), au.WeatherCloud).Blank();

        w.Note("LEDs per addressable header, capped by the controller; ASRock keeps its own count.")
         .Value(nameof(au.LedsPerChannel), au.LedsPerChannel);

        return w.ToString();
    }
}
