using System.Globalization;

namespace SystemSpinnerX64.ViewModels;

/// <summary>One value on the panel: the number, its unit and the width of its column.</summary>
public sealed class Metric : Observable
{
    private string _value = "—";
    private bool _visible = true;
    private bool _warning;
    private double _valueWidth;
    private double _cellWidth;

    /// <param name="valueSlots">
    /// Digits in the longest expected value: 3 for load, 4 for a clock. The column width is
    /// computed from this — otherwise a percentage cell would be as wide as an rpm one.
    /// </param>
    public Metric(string unit, int valueSlots)
    {
        Unit = unit;
        ValueSlots = valueSlots;
    }

    public string Unit { get; }

    public int ValueSlots { get; }

    public string Value
    {
        get => _value;
        private set => Set(ref _value, value);
    }

    // One per column, so the values of every row line up.
    public double ValueWidth
    {
        get => _valueWidth;
        internal set => Set(ref _valueWidth, value);
    }

    // Including the unit — it decides where the next column starts.
    public double CellWidth
    {
        get => _cellWidth;
        internal set => Set(ref _cellWidth, value);
    }

    // A dash where a pump does not exist would say "no data" instead of "no such hardware",
    // so that cell is dropped altogether.
    public bool Visible
    {
        get => _visible;
        private set => Set(ref _visible, value);
    }

    public bool Warning
    {
        get => _warning;
        private set => Set(ref _warning, value);
    }

    public void Update(double? raw, int decimals = 0) =>
        Value = raw is null
            ? "—"
            : raw.Value.ToString("F" + decimals, CultureInfo.InvariantCulture);

    /// <summary>Highlights a value that reached the threshold. A threshold of 0 is off.</summary>
    public void Update(double? raw, double threshold, int decimals = 0)
    {
        Update(raw, decimals);
        Warning = threshold > 0 && raw is not null && raw.Value >= threshold;
    }

    /// <summary>Drops the cell when there is no value.</summary>
    public void UpdateOrHide(double? raw, int decimals = 0)
    {
        Update(raw, decimals);
        Visible = raw is not null;
    }
}
