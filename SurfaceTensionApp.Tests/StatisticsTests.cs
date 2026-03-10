using Xunit;
using SurfaceTensionApp.Services;

namespace SurfaceTensionApp.Tests;

/// <summary>
/// Unit tests for OutlierRejection, SpikeFilter, and sample StdDev.
/// Scientific software must have verified statistical algorithms — errors
/// here propagate directly into published surface tension values.
/// </summary>
public class StatisticsTests
{
    // ══════════════════════════════════════════════════════
    // OutlierRejection
    // ══════════════════════════════════════════════════════

    [Fact]
    public void RejectOutliers_LessThan4Values_ReturnsAllClean()
    {
        var peaks = new List<double> { 1.0, 2.0, 3.0 };
        var (clean, outlierIdx, _) = OutlierRejection.RejectOutliers(peaks);
        Assert.Equal(peaks, clean);
        Assert.Empty(outlierIdx);
    }

    [Fact]
    public void RejectOutliers_UniformData_ReturnsAllClean()
    {
        // All identical values — no outliers
        var peaks = Enumerable.Repeat(1.5, 10).ToList();
        var (clean, outlierIdx, _) = OutlierRejection.RejectOutliers(peaks);
        Assert.Equal(peaks.Count, clean.Count);
        Assert.Empty(outlierIdx);
    }

    [Fact]
    public void RejectOutliers_SingleSpikeInTightCluster_DetectsOutlier()
    {
        // 9 values clustered around 1.0, one extreme outlier at 10.0
        var peaks = new List<double> { 1.0, 1.01, 0.99, 1.02, 0.98, 1.03, 0.97, 1.01, 1.00, 10.0 };
        var (clean, outlierIdx, outlierVals) = OutlierRejection.RejectOutliers(peaks);
        Assert.Single(outlierIdx);
        Assert.Equal(9, outlierIdx[0]);
        Assert.Equal(10.0, outlierVals[0], precision: 6);
        Assert.DoesNotContain(10.0, clean);
    }

    [Fact]
    public void RejectOutliers_CustomThreshold_AffectsSensitivity()
    {
        // With very low threshold every moderate deviation becomes an outlier
        var peaks = new List<double> { 1.0, 1.0, 1.0, 1.0, 2.0 };
        var (_, idx_strict, _) = OutlierRejection.RejectOutliers(peaks, threshold: 0.1);
        var (_, idx_loose,  _) = OutlierRejection.RejectOutliers(peaks, threshold: 10.0);
        Assert.True(idx_strict.Count >= idx_loose.Count);
    }

    [Fact]
    public void Median_OddCount_ReturnsMiddleValue()
    {
        double[] vals = { 3.0, 1.0, 4.0, 1.0, 5.0 };
        Assert.Equal(3.0, OutlierRejection.Median(vals), precision: 10);
    }

    [Fact]
    public void Median_EvenCount_ReturnsAverageOfMiddleTwo()
    {
        double[] vals = { 1.0, 2.0, 3.0, 4.0 };
        Assert.Equal(2.5, OutlierRejection.Median(vals), precision: 10);
    }

    [Fact]
    public void Median_EmptyArray_ReturnsZero()
    {
        Assert.Equal(0.0, OutlierRejection.Median(Array.Empty<double>()));
    }

    // ══════════════════════════════════════════════════════
    // SampleStdDev
    // ══════════════════════════════════════════════════════

    [Fact]
    public void SampleStdDev_SingleValue_ReturnsZero()
    {
        Assert.Equal(0.0, OutlierRejection.SampleStdDev(new[] { 5.0 }));
    }

    [Fact]
    public void SampleStdDev_TwoIdenticalValues_ReturnsZero()
    {
        Assert.Equal(0.0, OutlierRejection.SampleStdDev(new[] { 3.0, 3.0 }));
    }

    [Fact]
    public void SampleStdDev_KnownDataset_MatchesBessel()
    {
        // Population: {2, 4, 4, 4, 5, 5, 7, 9}  mean=5  pop-σ=2  sample-σ≈2.138
        var data = new[] { 2.0, 4.0, 4.0, 4.0, 5.0, 5.0, 7.0, 9.0 };
        double expected = Math.Sqrt(data.Select(x => (x - 5.0) * (x - 5.0)).Sum() / (data.Length - 1));
        double actual   = OutlierRejection.SampleStdDev(data);
        Assert.Equal(expected, actual, precision: 10);
    }

    [Fact]
    public void SampleStdDev_DividesBy_N_Minus_1_NotN()
    {
        // Verify Bessel's correction: result must equal sqrt(Σ(x-mean)²/(n-1))
        var data = new[] { 1.0, 2.0, 3.0 };
        double mean     = data.Average();
        double expected = Math.Sqrt(data.Sum(x => (x - mean) * (x - mean)) / (data.Length - 1));
        double actual   = OutlierRejection.SampleStdDev(data);
        Assert.Equal(expected, actual, precision: 12);
        // Ensure it is NOT the population StdDev (which would divide by n)
        double populationStdDev = Math.Sqrt(data.Sum(x => (x - mean) * (x - mean)) / data.Length);
        Assert.NotEqual(populationStdDev, actual);
    }

    // ══════════════════════════════════════════════════════
    // SpikeFilter
    // ══════════════════════════════════════════════════════

    [Fact]
    public void SpikeFilter_NoSpike_ReturnsSameData()
    {
        double[] times  = { 0, 1, 2, 3, 4 };
        double[] forces = { 1.0, 1.1, 1.2, 1.1, 1.0 };
        var (_, filtered, count) = SpikeFilter.Apply(times, forces);
        Assert.Equal(0, count);
        Assert.Equal(forces, filtered);
    }

    [Fact]
    public void SpikeFilter_AbsoluteThreshold_RemovesSpike()
    {
        double[] times  = { 0, 1, 2, 3, 4 };
        double[] forces = { 1.0, 1.0, 100.0, 1.0, 1.0 };  // spike at index 2
        var (_, filtered, count) = SpikeFilter.Apply(times, forces, maxForce: 5.0);
        Assert.Equal(1, count);
        // Interpolated value should be between neighbours (~1.0)
        Assert.True(filtered[2] < 5.0, "Spike should have been interpolated to a low value");
    }

    [Fact]
    public void SpikeFilter_AutoDetect_InteriorSpikeReplaced()
    {
        // Smooth baseline with one instant spike
        double[] times  = Enumerable.Range(0, 10).Select(i => (double)i).ToArray();
        double[] forces = Enumerable.Repeat(1.0, 10).ToArray();
        forces[5] = 1000.0; // extreme spike at index 5

        var (_, filtered, count) = SpikeFilter.Apply(times, forces);
        Assert.Equal(1, count);
        Assert.True(filtered[5] < 100.0, "Spike should be interpolated away");
        // Baseline points should be unchanged
        for (int i = 0; i < 10; i++)
            if (i != 5)
                Assert.Equal(1.0, filtered[i], precision: 10);
    }

    [Fact]
    public void SpikeFilter_TooShortSignal_ReturnsOriginal()
    {
        double[] times  = { 0, 1 };
        double[] forces = { 1.0, 1.0 };
        var (_, filtered, count) = SpikeFilter.Apply(times, forces);
        Assert.Equal(0, count);
        Assert.Equal(forces, filtered);
    }

    [Fact]
    public void SpikeFilter_GradualRealPeak_PreservedIntact()
    {
        // Simulate a real Du Noüy ring peak: slow rise then fall — must NOT be flagged
        double[] times  = Enumerable.Range(0, 20).Select(i => (double)i).ToArray();
        // Linear rise to 5, then linear fall
        double[] forces = times.Select(t => t < 10 ? t * 0.5 : (20 - t) * 0.5).ToArray();

        var (_, filtered, count) = SpikeFilter.Apply(times, forces);
        Assert.Equal(0, count);
        // All values must be unchanged
        for (int i = 0; i < forces.Length; i++)
            Assert.Equal(forces[i], filtered[i], precision: 10);
    }

    [Fact]
    public void SpikeFilter_RateOfChangeMultiplier_IsEight()
    {
        // Constant value must be documented and testable
        Assert.Equal(8.0, SpikeFilter.RateOfChangeMultiplier);
    }
}
