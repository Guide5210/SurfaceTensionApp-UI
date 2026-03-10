namespace SurfaceTensionApp.Services;

/// <summary>
/// Modified Z-score outlier rejection using Median Absolute Deviation (MAD).
/// Reference: Iglewicz &amp; Hoaglin (1993) — industry standard for robust outlier detection.
/// </summary>
public static class OutlierRejection
{
    /// <summary>
    /// Industry-standard MAD-based Z-score threshold (Iglewicz &amp; Hoaglin, 1993).
    /// Points with |modifiedZ| > 3.5 are considered outliers.
    /// Lowering this value increases sensitivity; raising it reduces it.
    /// </summary>
    public const double DefaultThreshold = 3.5;

    public static (List<double> clean, List<int> outlierIdx, List<double> outlierVals)
        RejectOutliers(List<double> peaks, double threshold = DefaultThreshold)
    {
        if (peaks.Count < 4)
            return (new List<double>(peaks), new(), new());

        double[] arr = peaks.ToArray();
        double median = Median(arr);
        double[] absDevs = arr.Select(x => Math.Abs(x - median)).ToArray();
        double mad = Median(absDevs);

        // MAD floor: 2% of |median| to prevent over-rejection on very tight data
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

    public static double Median(double[] values)
    {
        if (values.Length == 0) return 0;
        var sorted = values.OrderBy(x => x).ToArray();
        int n = sorted.Length;
        return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
    }

    /// <summary>
    /// Sample standard deviation — divides by (n-1) per Bessel's correction.
    /// Use this for measurement data where the full population is not sampled.
    /// </summary>
    public static double SampleStdDev(IReadOnlyList<double> values)
    {
        if (values.Count < 2) return 0;
        double avg = values.Average();
        double sumSq = values.Sum(v => (v - avg) * (v - avg));
        return Math.Sqrt(sumSq / (values.Count - 1));
    }
}

/// <summary>
/// Spike filter using Rate of Change (derivative) detection.
/// Flags and linearly interpolates points that jump unnaturally far from their
/// neighbors, while preserving real peaks that rise gradually over time.
/// When a user-specified maxForce threshold is provided, that absolute threshold
/// is used instead (backwards-compatible with original behaviour).
/// </summary>
public static class SpikeFilter
{
    /// <summary>
    /// A delta must exceed this multiple of the median absolute delta to be
    /// classified as a spike boundary. Based on empirical tuning for Du Noüy
    /// ring force profiles; increase to tolerate faster force changes.
    /// </summary>
    public const double RateOfChangeMultiplier = 8.0;

    /// <summary>
    /// Replace spike points with linearly interpolated values.
    /// </summary>
    /// <param name="times">Time values (same length as forces).</param>
    /// <param name="forces">Force values.</param>
    /// <param name="maxForce">Absolute upper threshold. Points above this are spikes.
    ///   If null, uses the automatic Rate of Change method.</param>
    /// <returns>Same-length arrays with spikes replaced, plus spike count.</returns>
    public static (double[] times, double[] forces, int spikeCount) Apply(
        double[] times, double[] forces, double? maxForce = null)
    {
        int n = forces.Length;
        if (n < 3) return (times, forces, 0);

        bool[] isSpike = new bool[n];
        int spikeCount = 0;

        if (maxForce.HasValue)
        {
            // User-specified absolute threshold (unchanged behaviour)
            for (int i = 0; i < n; i++)
            {
                if (forces[i] > maxForce.Value)
                {
                    isSpike[i] = true;
                    spikeCount++;
                }
            }
        }
        else
        {
            // Auto-detect using Rate of Change (derivative filter).
            double[] deltas = new double[n - 1];
            for (int i = 0; i < n - 1; i++)
                deltas[i] = Math.Abs(forces[i + 1] - forces[i]);

            double[] sortedDeltas = (double[])deltas.Clone();
            Array.Sort(sortedDeltas);
            double medianDelta = sortedDeltas.Length % 2 == 1
                ? sortedDeltas[sortedDeltas.Length / 2]
                : (sortedDeltas[sortedDeltas.Length / 2 - 1] + sortedDeltas[sortedDeltas.Length / 2]) / 2.0;

            // Threshold: jump must exceed RateOfChangeMultiplier × median delta.
            // Floor prevents false positives when all data is extremely smooth.
            double deltaThreshold = Math.Max(medianDelta * RateOfChangeMultiplier, 1e-6);

            for (int i = 0; i < n; i++)
            {
                double dLeft  = i > 0     ? Math.Abs(forces[i] - forces[i - 1]) : 0;
                double dRight = i < n - 1 ? Math.Abs(forces[i + 1] - forces[i]) : 0;

                if (i > 0 && i < n - 1)
                {
                    // Interior: spike only if BOTH neighbors show sharp jumps
                    if (dLeft > deltaThreshold && dRight > deltaThreshold)
                    { isSpike[i] = true; spikeCount++; }
                }
                else if (i == 0 && dRight > deltaThreshold * 2)
                { isSpike[i] = true; spikeCount++; }
                else if (i == n - 1 && dLeft > deltaThreshold * 2)
                { isSpike[i] = true; spikeCount++; }
            }
        }

        if (spikeCount == 0) return (times, forces, 0);

        // Replace spikes with linear interpolation between clean neighbours
        double[] filtered = (double[])forces.Clone();

        int idx = 0;
        while (idx < n)
        {
            if (!isSpike[idx]) { idx++; continue; }

            int regionStart = idx;
            while (idx < n && isSpike[idx]) idx++;
            int regionEnd = idx - 1;

            int left  = regionStart - 1;
            int right = regionEnd + 1;

            if (left < 0 && right >= n) continue; // entire signal is spiked

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

            double dt = times[right] - times[left];
            for (int j = regionStart; j <= regionEnd; j++)
            {
                double t = dt > 1e-12 ? (times[j] - times[left]) / dt : 0.5;
                filtered[j] = forces[left] + t * (forces[right] - forces[left]);
            }
        }

        return (times, filtered, spikeCount);
    }
}
