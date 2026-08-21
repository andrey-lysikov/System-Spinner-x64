using System.Globalization;

namespace SystemSpinnerX64.ViewModels;

// One value on the panel: the number, its unit and the width of its column.
public sealed class Metric : Observable
{
    private string _value = "—";
    private bool _visible = true;
    private bool _warning;
    private double _valueWidth;
    private double _cellWidth;

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

    // Highlights a value that reached the threshold.
    public void Update(double? raw, double threshold, int decimals = 0)
    {
        Update(raw, decimals);
        Warning = threshold > 0 && raw is not null && raw.Value >= threshold;
    }

    // Drops the cell when there is no value.
    public void UpdateOrHide(double? raw, int decimals = 0)
    {
        Update(raw, decimals);
        Visible = raw is not null;
    }
}
