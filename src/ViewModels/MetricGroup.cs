using System.Collections.ObjectModel;

namespace SystemSpinnerX64.ViewModels;

/// <summary>A panel row: the group tag (CPU, GPU, FPS) and its values in order.</summary>
public sealed class MetricGroup
{
    public MetricGroup(string title, params Metric[] metrics)
    {
        Title = title;
        Metrics = new ObservableCollection<Metric>(metrics);
    }

    public string Title { get; }
    public ObservableCollection<Metric> Metrics { get; }
}
