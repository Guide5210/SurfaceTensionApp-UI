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
/// Spike filter using Rate of Change (derivative) detection.
/// A point is flagged as a spike when its force value jumps unnaturally
/// far from its neighbors — preserving the real peak that rises gradually.
/// When a user-specified maxForce threshold is provided, that absolute
/// threshold is used instead (backwards-compatible).
/// </summary>
public static class SpikeFilter
{
    /// <summary>
    /// Replace spike points with linearly interpolated values.
    /// </summary>
    /// <param name="times">Time values.</param>
    /// <param name="forces">Force values.</param>
    /// <param name="maxForce">Absolute force threshold — points with force &gt; maxForce are spikes.
    /// If null, auto-detects using Rate of Change (derivative) method.</param>
    /// <returns>Same-length arrays with spikes replaced by interpolation, plus spike count.</returns>
    public static (double[] times, double[] forces, int spikeCount) Apply(
        double[] times, double[] forces, double? maxForce = null)
    {
        int n = forces.Length;
        if (n < 3)
            return (times, forces, 0);

        // Mark spike points
        bool[] isSpike = new bool[n];
        int spikeCount = 0;

        if (maxForce.HasValue)
        {
            // User-specified absolute threshold (unchanged behavior)
            double upperThreshold = maxForce.Value;
            for (int i = 0; i < n; i++)
            {
                if (forces[i] > upperThreshold)
                {
                    isSpike[i] = true;
                    spikeCount++;
                }
            }
        }
        else
        {
            // Auto-detect using Rate of Change (derivative filter).
            // Compute point-to-point deltas and use a robust threshold
            // based on median absolute delta to find sudden jumps.
            double[] deltas = new double[n - 1];
            for (int i = 0; i < n - 1; i++)
                deltas[i] = Math.Abs(forces[i + 1] - forces[i]);

            // Median absolute delta
            double[] sortedDeltas = new double[deltas.Length];
            Array.Copy(deltas, sortedDeltas, deltas.Length);
            Array.Sort(sortedDeltas);
            double medianDelta = sortedDeltas.Length % 2 == 1
                ? sortedDeltas[sortedDeltas.Length / 2]
                : (sortedDeltas[sortedDeltas.Length / 2 - 1] + sortedDeltas[sortedDeltas.Length / 2]) / 2.0;

            // Threshold: a jump must exceed 8x the median delta to be a spike.
            // Floor prevents false positives when all data is very smooth.
            double deltaThreshold = Math.Max(medianDelta * 8.0, 1e-6);

            for (int i = 0; i < n; i++)
            {
                double deltaLeft = i > 0 ? Math.Abs(forces[i] - forces[i - 1]) : 0;
                double deltaRight = i < n - 1 ? Math.Abs(forces[i + 1] - forces[i]) : 0;

                // A spike jumps sharply both in AND out (or is at the boundary)
                // A real peak rises gradually from the left side
                if (i > 0 && i < n - 1)
                {
                    // Interior point: spike if BOTH left and right deltas exceed threshold
                    if (deltaLeft > deltaThreshold && deltaRight > deltaThreshold)
                    {
                        isSpike[i] = true;
                        spikeCount++;
                    }
                }
                else if (i == 0 && deltaRight > deltaThreshold * 2)
                {
                    isSpike[i] = true;
                    spikeCount++;
                }
                else if (i == n - 1 && deltaLeft > deltaThreshold * 2)
                {
                    isSpike[i] = true;
                    spikeCount++;
                }
            }
        }

        if (spikeCount == 0)
            return (times, forces, 0);

        // Replace spikes with linear interpolation
        double[] filtered = new double[n];
        Array.Copy(forces, filtered, n);

        int idx = 0;
        while (idx < n)
        {
            if (!isSpike[idx]) { idx++; continue; }

            // Find the extent of this spike region
            int regionStart = idx;
            while (idx < n && isSpike[idx]) idx++;
            int regionEnd = idx - 1;

            // Boundary clean points for interpolation
            int left = regionStart - 1;
            int right = regionEnd + 1;

            if (left < 0 && right >= n)
            {
                // Entire signal is above threshold — cannot interpolate
                continue;
            }

            if (left < 0)
            {
                for (int j = regionStart; j <= regionEnd; j++)
                    filtered[j] = forces[right];
                continue;
            }

            if (right >= n)
            {
                for (int j = regionStart; j <= regionEnd; j++)
                    filtered[j] = forces[left];
                continue;
            }

            // Linear interpolation between left and right boundary
            double tLeft = times[left];
            double fLeft = forces[left];
            double tRight = times[right];
            double fRight = forces[right];
            double dt = tRight - tLeft;

            for (int j = regionStart; j <= regionEnd; j++)
            {
                double t = dt > 1e-12 ? (times[j] - tLeft) / dt : 0.5;
                filtered[j] = fLeft + t * (fRight - fLeft);
            }
        }

        return (times, filtered, spikeCount);
    }
}