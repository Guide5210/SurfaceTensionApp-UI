using SurfaceTensionApp.Models;

namespace SurfaceTensionApp.Services;

/// <summary>
/// Owns the in-memory store of SpeedGroups and TestRuns for the active session.
/// Handles outlier computation and statistics aggregation.
/// MainViewModel subscribes to DataChanged to refresh its ObservableCollections.
/// </summary>
public class RunProcessingService
{
    private readonly Dictionary<string, SpeedGroup> _allData = new();

    public IReadOnlyDictionary<string, SpeedGroup> AllData => _allData;

    /// <summary>Total number of runs across all groups (auto + single).</summary>
    public int TotalRuns { get; private set; }

    // ── Lifetime ───────────────────────────────────────────

    public void Clear()
    {
        _allData.Clear();
        TotalRuns = 0;
    }

    public void LoadData(Dictionary<string, SpeedGroup> data)
    {
        _allData.Clear();
        TotalRuns = 0;
        foreach (var (key, group) in data)
        {
            _allData[key] = group;
            TotalRuns += group.Runs.Count;
        }
    }

    // ── Run management ─────────────────────────────────────

    public SpeedGroup GetOrCreateGroup(string speedName, double speedMms, int batch)
    {
        string key = Key(speedName, batch);
        if (!_allData.TryGetValue(key, out var group))
        {
            group = new SpeedGroup
            {
                Key      = key,
                BaseName = speedName,
                SpeedMmS = speedMms,
                Batch    = batch,
            };
            _allData[key] = group;
        }
        return group;
    }

    /// <summary>
    /// Append a completed run to the group and recompute outliers.
    /// </summary>
    public void AddRun(SpeedGroup group, TestRun run, double peakForce)
    {
        group.Runs.Add(run);
        group.PeakForces.Add(peakForce);
        TotalRuns++;
        group.ComputeOutliers();
    }

    // ── Manual outlier toggle ──────────────────────────────

    /// <summary>
    /// Toggle a manual outlier mark for the given run index.
    /// Returns true if the run is now marked as outlier, false if unmarked.
    /// Returns false if the group is not found.
    /// </summary>
    public bool ToggleManualOutlier(string speedName, int batch, int runIndex)
    {
        string key = Key(speedName, batch);
        if (!_allData.TryGetValue(key, out var group)) return false;

        bool nowOutlier;
        if (group.ManualOutlierIndices.Contains(runIndex))
        {
            group.ManualOutlierIndices.Remove(runIndex);
            nowOutlier = false;
        }
        else
        {
            group.ManualOutlierIndices.Add(runIndex);
            nowOutlier = true;
        }

        group.ComputeOutliers();
        return nowOutlier;
    }

    // ── Statistics ─────────────────────────────────────────

    public IEnumerable<StatsRow> BuildStatsRows() =>
        _allData
            .OrderByDescending(x => x.Value.SpeedMmS)
            .ThenBy(x => x.Value.Batch)
            .Select(kvp => new StatsRow
            {
                SpeedName  = kvp.Value.BaseName,
                Batch      = kvp.Value.Batch,
                TotalRuns  = kvp.Value.PeakForces.Count,
                ValidRuns  = kvp.Value.CleanPeaks.Count,
                Average    = Math.Round(kvp.Value.Avg, 6),
                StdDev     = Math.Round(kvp.Value.Std, 6),
                RsdPercent = Math.Round(kvp.Value.Rsd, 2),
                Outliers   = kvp.Value.OutlierIndices.Count,
            });

    public bool IsOutlier(string speedName, int batch, int runIndex)
    {
        return _allData.TryGetValue(Key(speedName, batch), out var g) &&
               g.OutlierIndices.Contains(runIndex);
    }

    private static string Key(string speedName, int batch) => $"{speedName}_{batch}";
}
