using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SystemSpinnerX64.Configuration;

/// <summary>
/// Sections in square brackets, "key = value" lines, comments from a hash. Chosen over JSON
/// because the file is edited by hand: the explanation has to sit next to the parameter, and
/// a forgotten comma must break nothing. A hash starts a comment only at the beginning of a
/// line, or "TextColor = #FFFFFF" could not be written.
/// </summary>
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

    private string? Raw(string section, string key) =>
        _sections.TryGetValue(section, out var values) && values.TryGetValue(key, out string? value) && value.Length > 0
            ? value
            : null;

    /// <summary>The string, or null when the parameter is absent. Absent always means "leave as is".</summary>
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

    /// <summary>
    /// A number that may carry a trailing per cent sign: both "90" and "90 %" read as 90. The
    /// sign is accepted because a bare threshold next to a temperature is easy to misread, and
    /// the app writes it back for the same reason.
    /// </summary>
    public double? Percent(string section, string key)
    {
        string? value = Raw(section, key);
        if (value is null) return null;

        string number = value.TrimEnd('%', ' ');
        return double.TryParse(number.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : throw new FormatException($"[{section}] {key}: \"{value}\" — expected a percentage");
    }

    /// <summary>A comma-separated list. An empty string is an empty list, not "leave as is".</summary>
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

    /// <summary>Building the file: sections, comments and values in the order they are written.</summary>
    internal sealed class Writer
    {
        private readonly StringBuilder _text = new();

        public Writer Section(string name)
        {
            if (_text.Length > 0) _text.AppendLine();
            _text.AppendLine($"[{name}]");
            return this;
        }

        /// <summary>The note above a parameter — the very reason this format was chosen.</summary>
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

        /// <summary>A number with a unit written after it, as in "90 %".</summary>
        public Writer Value(string key, double value, string unit) =>
            Value(key, value.ToString("0.###", CultureInfo.InvariantCulture) + " " + unit);

        public Writer Value(string key, IEnumerable<string> values) => Value(key, string.Join(", ", values));

        public Writer Blank()
        {
            _text.AppendLine();
            return this;
        }

        public override string ToString() => _text.ToString();
    }
}
