using System.Collections.Generic;
using System.Linq;

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

    // Manual outlier marks by user
    public HashSet<int> ManualOutlierIndices { get; } = new();

    public void ComputeOutliers()
    {
        // First: auto detect via MAD
        var (autoClean, autoIdx, autoVals) = OutlierRejection.RejectOutliers(PeakForces);

        // Merge manual + auto outlier indices
        var allOutlierIdx = new HashSet<int>(autoIdx);
        foreach (int mi in ManualOutlierIndices)
            allOutlierIdx.Add(mi);

        // Rebuild clean/outlier lists
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
    public double Std => CleanPeaks.Count > 1 ? StdDev(CleanPeaks) : 0;
    public double Rsd => Avg != 0 ? Std / Avg * 100 : 0;

    private static double StdDev(List<double> vals)
    {
        double avg = vals.Average();
        return Math.Sqrt(vals.Sum(v => (v - avg) * (v - avg)) / vals.Count);
    }
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

/// <summary>
/// Modified Z-score outlier rejection using MAD — exact port from Python.
/// </summary>
public static class OutlierRejection
{
    public static (List<double> clean, List<int> outlierIdx, List<double> outlierVals)
        RejectOutliers(List<double> peaks, double threshold = 3.5)
    {
        if (peaks.Count < 4)
            return (new List<double>(peaks), new(), new());

        double[] arr = peaks.ToArray();
        double median = Median(arr);
        double[] absDevs = arr.Select(x => Math.Abs(x - median)).ToArray();
        double mad = Median(absDevs);

        // MAD floor: 2% of |median| to prevent over-rejection on tight data
        double madFloor = Math.Abs(median) * 0.02;
        if (mad < madFloor) mad = madFloor;

        if (mad < 1e-10)
            return (new List<double>(peaks), new(), new());

        var clean = new List<double>();
        var outlierIdx = new List<int>();
        var outlierVals = new List<double>();

        for (int i = 0; i < arr.Length; i++)
        {
            double modZ = 0.6745 * (arr[i] - median) / mad;
            if (Math.Abs(modZ) > threshold)
            {
                outlierIdx.Add(i);
                outlierVals.Add(arr[i]);
            }
            else
            {
                clean.Add(arr[i]);
            }
        }

        return (clean, outlierIdx, outlierVals);
    }

    private static double Median(double[] values)
    {
        var sorted = values.OrderBy(x => x).ToArray();
        int n = sorted.Length;
        if (n == 0) return 0;
        return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
    }
}

/// <summary>
/// Filters spikes from time-series force data using local median + MAD.
/// Only replaces points that deviate significantly from local neighbors;
/// non-spike data passes through unchanged.
/// </summary>
public static class SpikeFilter
{
    public static double[] Apply(IList<double> forces, int windowSize = 7, double threshold = 3.5)
    {
        int n = forces.Count;
        if (n < windowSize)
            return forces.ToArray();

        int half = windowSize / 2;
        double[] result = new double[n];

        for (int i = 0; i < n; i++)
        {
            int start = Math.Max(0, i - half);
            int end = Math.Min(n - 1, i + half);
            int wLen = end - start + 1;

            // Build sorted local window for median
            double[] local = new double[wLen];
            for (int j = 0; j < wLen; j++)
                local[j] = forces[start + j];
            Array.Sort(local);

            double median = wLen % 2 == 1
                ? local[wLen / 2]
                : (local[wLen / 2 - 1] + local[wLen / 2]) / 2.0;

            // Compute local MAD (median absolute deviation)
            double[] absDevs = new double[wLen];
            for (int j = 0; j < wLen; j++)
                absDevs[j] = Math.Abs(local[j] - median);
            Array.Sort(absDevs);

            double mad = wLen % 2 == 1
                ? absDevs[wLen / 2]
                : (absDevs[wLen / 2 - 1] + absDevs[wLen / 2]) / 2.0;

            if (mad < 1e-10)
            {
                result[i] = forces[i];
                continue;
            }

            double deviation = Math.Abs(forces[i] - median);
            result[i] = deviation > threshold * mad ? median : forces[i];
        }

        return result;
    }
}
