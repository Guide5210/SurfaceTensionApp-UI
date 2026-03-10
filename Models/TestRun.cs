using System.Collections.Generic;
using System.Linq;
using SurfaceTensionApp.Services;

namespace SurfaceTensionApp.Models;

/// <summary>
/// Data for a single measurement run (approach + retract).
/// </summary>
public class TestRun
{
    public List<double> Times { get; } = new();
    public List<double> Forces { get; } = new();
    public List<double> Positions { get; } = new();
    public List<double> RelPositions { get; } = new();
    public double? ContactPosition { get; set; }
    public double? ValidatedPeak { get; set; }

    public double PeakForce => ValidatedPeak is > 0 ? ValidatedPeak.Value
                                : Forces.Count > 0 ? Forces.Max() : 0;
    public int PointCount => Times.Count;
}

/// <summary>
/// Collection of runs for one speed/batch combination.
/// Key format: "ULTRA_FAST_1" (speedName_batchNum).
/// </summary>
public class SpeedGroup
{
    public string Key { get; init; } = "";           // e.g. "ULTRA_FAST_1"
    public string BaseName { get; init; } = "";      // e.g. "ULTRA_FAST"
    public double SpeedMmS { get; init; }
    public int Batch { get; set; } = 1;
    public List<TestRun> Runs { get; } = new();
    public List<double> PeakForces { get; } = new();

    // Outlier rejection results (computed on demand)
    public List<double> CleanPeaks { get; set; } = new();
    public List<int> OutlierIndices { get; set; } = new();
    public List<double> OutlierValues { get; set; } = new();

    // Manual outlier marks by user — ObservableHashSet fires INotifyCollectionChanged
    // so any UI element bound to this collection refreshes automatically.
    public ObservableHashSet<int> ManualOutlierIndices { get; } = new();

    public void ComputeOutliers()
    {
        // Auto-detect via MAD-based modified Z-score
        var (_, autoIdx, _) = OutlierRejection.RejectOutliers(PeakForces);

        // Merge manual + auto outlier indices
        var allOutlierIdx = new HashSet<int>(autoIdx);
        foreach (int mi in ManualOutlierIndices)
            allOutlierIdx.Add(mi);

        CleanPeaks = new List<double>();
        OutlierIndices = new List<int>();
        OutlierValues = new List<double>();

        for (int i = 0; i < PeakForces.Count; i++)
        {
            if (allOutlierIdx.Contains(i))
            {
                OutlierIndices.Add(i);
                OutlierValues.Add(PeakForces[i]);
            }
            else
            {
                CleanPeaks.Add(PeakForces[i]);
            }
        }
    }

    public double Avg => CleanPeaks.Count > 0 ? CleanPeaks.Average() : 0;

    // Sample standard deviation (Bessel's correction: divide by n-1).
    // Population StdDev (÷n) would underestimate variability for small samples.
    public double Std => OutlierRejection.SampleStdDev(CleanPeaks);

    public double Rsd => Avg != 0 ? Std / Avg * 100 : 0;
}

/// <summary>
/// Row for the results DataGrid.
/// </summary>
public class RunResultRow : System.ComponentModel.INotifyPropertyChanged
{
    public string SpeedName { get; init; } = "";
    public int Batch { get; init; }
    public int RunNumber { get; init; }
    public double PeakForce { get; init; }
    public int Points { get; init; }

    private bool _isOutlier;
    public bool IsOutlier
    {
        get => _isOutlier;
        set { _isOutlier = value; OnPropertyChanged(nameof(IsOutlier)); OnPropertyChanged(nameof(Status)); }
    }
    public string Status => IsOutlier ? "OUTLIER" : "OK";

    private bool _isVisible = true;
    public bool IsVisible
    {
        get => _isVisible;
        set { _isVisible = value; OnPropertyChanged(nameof(IsVisible)); }
    }

    private string _colorHex = "#4A9EFF";
    public string ColorHex
    {
        get => _colorHex;
        set { _colorHex = value; OnPropertyChanged(nameof(ColorHex)); }
    }

    // Index into the _runEntries list (set by code-behind)
    public int PlotIndex { get; set; } = -1;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}

/// <summary>
/// Row for statistics DataGrid.
/// </summary>
public class StatsRow
{
    public string SpeedName { get; init; } = "";
    public int Batch { get; init; }
    public int TotalRuns { get; init; }
    public int ValidRuns { get; init; }
    public double Average { get; init; }
    public double StdDev { get; init; }
    public double RsdPercent { get; init; }
    public int Outliers { get; init; }
}
