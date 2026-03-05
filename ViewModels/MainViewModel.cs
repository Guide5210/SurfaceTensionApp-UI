using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SurfaceTensionApp.Models;
using SurfaceTensionApp.Services;

namespace SurfaceTensionApp.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    // ══════════════════════════════════════════════════════════════
    // Constants
    // ══════════════════════════════════════════════════════════════
    private const int NUM_SPEEDS = 12;
    private const int AUTO_REPEATS = 10;
    private const int AUTO_BATCHES = 2;
    private static readonly int TOTAL_AUTO_RUNS = AUTO_BATCHES * NUM_SPEEDS * AUTO_REPEATS; // 240

    // ══════════════════════════════════════════════════════════════
    // Services
    // ══════════════════════════════════════════════════════════════
    private readonly SerialService _serial = new();
    private readonly DatabaseService _db = new();
    private readonly Dispatcher _dispatcher;
    private long _currentSessionId;

    // ══════════════════════════════════════════════════════════════
    // Bindable properties — Session browser
    // ══════════════════════════════════════════════════════════════
    public ObservableCollection<SessionInfo> Sessions { get; } = new();
    [ObservableProperty] private SessionInfo? _selectedSession;

    // ══════════════════════════════════════════════════════════════
    // Bindable properties — Connection
    // ══════════════════════════════════════════════════════════════
    [ObservableProperty] private ObservableCollection<string> _availablePorts = new();
    [ObservableProperty] private string _selectedPort = "";
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _connectionStatus = "Disconnected";

    // ══════════════════════════════════════════════════════════════
    // Encoder mode
    // ══════════════════════════════════════════════════════════════
    [ObservableProperty] private bool _isEncoderMode;
    [ObservableProperty] private string _encoderHome = "—";
    [ObservableProperty] private string _encoderTarget = "—";

    // ══════════════════════════════════════════════════════════════
    // Auto sequence progress
    // ══════════════════════════════════════════════════════════════
    [ObservableProperty] private bool _isAutoRunning;
    [ObservableProperty] private int _autoProgress;
    [ObservableProperty] private int _autoTotal = TOTAL_AUTO_RUNS;
    [ObservableProperty] private string _autoStatusText = "Idle";
    [ObservableProperty] private string _currentBatchText = "";
    [ObservableProperty] private string _currentSpeedText = "";
    [ObservableProperty] private string _currentRunText = "";
    [ObservableProperty] private string _lastPeakText = "";

    // ══════════════════════════════════════════════════════════════
    // Live force display (removed — monitor only)
    // ══════════════════════════════════════════════════════════════
    [ObservableProperty] private string _liveForce = "0.00000 N";

    // ══════════════════════════════════════════════════════════════
    // Emergency Stop lock
    // ══════════════════════════════════════════════════════════════
    [ObservableProperty] private bool _isEmergencyLocked;

    // ══════════════════════════════════════════════════════════════
    // Custom Auto Repeat
    // ══════════════════════════════════════════════════════════════
    [ObservableProperty] private string _customRepeatCount = "10";
    [ObservableProperty] private string _selectedSpeedOption = "1: ULTRA_FAST (600 µm/s)";
    public ObservableCollection<string> SpeedOptions { get; } = new()
    {
        "1: ULTRA_FAST (600 µm/s)",
        "2: FAST_UP (450 µm/s)",
        "3: FAST_DN (150 µm/s)",
        "4: V8 (133.5 µm/s)",
        "5: V6 (100.125 µm/s)",
        "6: V4 (66.75 µm/s)",
        "7: V2 (33.3375 µm/s)",
        "8: MEASURE_F (18.75 µm/s)",
        "9: MEASURE_M (7.50 µm/s)",
        "B: MEASURE_X (1.875 µm/s)",
        "C: MEASURE_Z (0.75 µm/s)",
    };

    // ══════════════════════════════════════════════════════════════
    // Measurement Configuration
    // ══════════════════════════════════════════════════════════════
    public MeasurementConfig Config { get; } = new();

    public ObservableCollection<string> MethodOptions { get; } = new() { "Du Noüy Ring", "Wilhelmy Plate" };
    public ObservableCollection<string> LoadCellOptions { get; } = new() { "100g", "30g" };
    public ObservableCollection<string> UnitOptions { get; } = new() { "mN/m", "dyn/cm" };

    // ══════════════════════════════════════════════════════════════
    // Results & Statistics
    // ══════════════════════════════════════════════════════════════
    public ObservableCollection<RunResultRow> RunResults { get; } = new();
    public ObservableCollection<StatsRow> StatsRows { get; } = new();
    [ObservableProperty] private RunResultRow? _selectedRunResult;

    // ══════════════════════════════════════════════════════════════
    // Serial Log
    // ══════════════════════════════════════════════════════════════
    [ObservableProperty] private string _serialLog = "";
    [ObservableProperty] private bool _showSerialLog = true;
    private readonly object _logLock = new();

    // ══════════════════════════════════════════════════════════════
    // Spike filter toggle + threshold
    // ══════════════════════════════════════════════════════════════
    [ObservableProperty] private bool _isSpikeFilterEnabled;
    [ObservableProperty] private string _spikeThresholdText = "";

    /// <summary>
    /// Parsed threshold value (null = auto-detect using IQR).
    /// </summary>
    public double? SpikeThreshold =>
        double.TryParse(SpikeThresholdText, out double v) && v > 0 ? v : null;

    partial void OnIsSpikeFilterEnabledChanged(bool value)
    {
        SpikeFilterToggled?.Invoke();
        string thresholdInfo = SpikeThreshold.HasValue ? $", threshold={SpikeThreshold.Value} N" : ", auto-detect";
        AppendLog(value ? $"✓ Spike filter ON{thresholdInfo}" : "✓ Spike filter OFF");
    }

    partial void OnSpikeThresholdTextChanged(string value)
    {
        if (IsSpikeFilterEnabled)
            SpikeFilterToggled?.Invoke();
    }

    // ══════════════════════════════════════════════════════════════
    // Graph data (accessed by code-behind to update ScottPlot)
    // ══════════════════════════════════════════════════════════════
    public event Action? GraphDataUpdated;
    public event Action? GraphRunCompleted;
    public event Action? SpikeFilterToggled;

    // ══════════════════════════════════════════════════════════════
    // Internal state
    // ══════════════════════════════════════════════════════════════
    private readonly Dictionary<string, SpeedGroup> _allData = new();
    private TestRun? _currentRun;
    private string? _currentSpeed;
    private int _currentBatch = 1;
    private int _totalRuns;
    private bool _isSingleTest;

    // Live graph data for current run
    public List<double> LiveTimes { get; } = new();
    public List<double> LiveForces { get; } = new();

    // All completed runs (for overlay plot)
    public Dictionary<string, SpeedGroup> AllData => _allData;

    // ══════════════════════════════════════════════════════════════
    // Constructor
    // ══════════════════════════════════════════════════════════════
    public MainViewModel()
    {
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _serial.LineReceived += OnLineReceived;
        _serial.ConnectionChanged += c => _dispatcher.Invoke(() =>
        {
            IsConnected = c;
            ConnectionStatus = c ? $"Connected ({_serial.PortName})" : "Disconnected";
        });
        RefreshPorts();
        _currentSessionId = _db.CreateSession();
        RefreshSessions();
    }

    // ══════════════════════════════════════════════════════════════
    // Commands — Connection
    // ══════════════════════════════════════════════════════════════
    [RelayCommand]
    private void RefreshPorts()
    {
        AvailablePorts.Clear();
        foreach (var p in SerialService.GetAvailablePorts())
            AvailablePorts.Add(p);
        if (AvailablePorts.Count > 0 && string.IsNullOrEmpty(SelectedPort))
            SelectedPort = AvailablePorts[0];
    }

    [RelayCommand]
    private void ToggleConnect()
    {
        if (IsConnected)
        {
            _serial.Disconnect();
        }
        else if (!string.IsNullOrEmpty(SelectedPort))
        {
            if (_serial.Connect(SelectedPort))
            {
                AppendLog($"✓ Connected to {SelectedPort}");
                // Query system info from Arduino
                QuerySystemInfo();
            }
            else
                AppendLog($"✗ Failed to connect to {SelectedPort}");
        }
    }

    // ══════════════════════════════════════════════════════════════
    // Commands — Control panel
    // ══════════════════════════════════════════════════════════════
    [RelayCommand]
    private void RunSpeed(string speedNum)
    {
        if (!IsConnected || IsEmergencyLocked) return;
        // Set current speed name for single test data collection
        var profile = SpeedProfile.All.FirstOrDefault(s => s.SerialCmd == speedNum[0]);
        if (profile != null)
        {
            _currentSpeed = profile.Name;
            _currentBatch = 1;
            _isSingleTest = true;
        }
        _serial.Flush();
        _serial.Send(speedNum);
        AppendLog($"→ Speed test {speedNum}");
    }

    [RelayCommand]
    private void RunAutoSequence()
    {
        if (!IsConnected || IsEmergencyLocked) return;
        _allData.Clear();
        RunResults.Clear();
        StatsRows.Clear();
        _totalRuns = 0;
        _currentBatch = 1;
        _isSingleTest = false;
        IsAutoRunning = true;
        AutoProgress = 0;
        AutoStatusText = $"Auto: 0/{TOTAL_AUTO_RUNS}";
        _serial.Flush();
        _serial.Send('A');
        AppendLog($"→ Auto sequence started ({TOTAL_AUTO_RUNS} runs: {AUTO_BATCHES}x{NUM_SPEEDS}x{AUTO_REPEATS})");
    }

    [RelayCommand]
    private void Tare()
    {
        if (!IsConnected) return;
        _serial.Flush();
        _serial.Send('t');
        AppendLog("→ Tare");
    }

    [RelayCommand]
    private void SetHome()
    {
        if (!IsConnected) return;
        _serial.Flush();
        _serial.Send('0');
        AppendLog("→ Set Home");
    }

    [RelayCommand]
    private void GoHome()
    {
        if (!IsConnected) return;
        _serial.Flush();
        _serial.Send('h');
        AppendLog("→ Go Home");
    }

    [RelayCommand]
    private void Calibrate()
    {
        if (!IsConnected) return;
        _serial.Flush();
        _serial.Send('k');
        AppendLog("→ Calibrate");
    }

    /// <summary>
    /// Send the known weight value to the Arduino during calibration.
    /// Called after 'k' has already been sent to enter calibration mode.
    /// </summary>
    [RelayCommand]
    private void SendCalibrationWeight(string weightStr)
    {
        if (!IsConnected) return;
        _serial.Send(weightStr);
        AppendLog($"→ Calibration weight: {weightStr} g");
    }

    [RelayCommand]
    private void EmergencyStop()
    {
        if (!IsConnected) return;

        if (IsEmergencyLocked)
        {
            // Unlock — send 'r' to clear eStop state on Arduino
            _serial.Send('r');
            IsEmergencyLocked = false;
            AppendLog("🔓 Emergency unlocked — system ready");
            return;
        }

        // Lock — stop everything
        _serial.Send('q');
        IsEmergencyLocked = true;
        IsAutoRunning = false;
        _isSingleTest = false;
        _currentRun = null;
        _customAutoRemaining = 0;
        AutoStatusText = "EMERGENCY STOPPED";
        AppendLog("⚠ EMERGENCY STOP — all operations halted. Click again to unlock.");
    }

    [RelayCommand]
    private void GoTarget()
    {
        if (!IsConnected || IsEmergencyLocked) return;
        _serial.Flush();
        _serial.Send('g');  // 'g' = go to target position
        AppendLog("→ Go Target");
    }

    // ── Custom Auto Repeat ──
    private int _customAutoRemaining;
    private string? _customAutoSpeedCmd;

    [RelayCommand]
    private void RunCustomAuto()
    {
        if (!IsConnected || IsEmergencyLocked) return;
        if (!int.TryParse(CustomRepeatCount, out int count) || count < 1 || count > 999)
        {
            AppendLog("✗ Invalid repeat count (1-999)");
            return;
        }

        string speedCmd = SelectedSpeedOption.Split(':')[0].Trim(); // "1", "2", ... "9", "B", "C"
        var profile = SpeedProfile.All.FirstOrDefault(s => s.SerialCmd == speedCmd[0]);
        if (profile == null) return;

        _currentSpeed = profile.Name;
        _currentBatch = 1;
        _isSingleTest = true;
        _customAutoRemaining = count;
        _customAutoSpeedCmd = speedCmd;
        AutoTotal = count;
        AutoProgress = 0;
        IsAutoRunning = true;
        AutoStatusText = $"Custom: 0/{count} ({profile.Name})";

        _serial.Flush();
        _serial.Send(speedCmd);
        AppendLog($"→ Custom auto: {count}x {profile.Name}");
    }

    [RelayCommand]
    private void Monitor()
    {
        if (!IsConnected) return;
        _serial.Flush();
        _serial.Send('m');
        AppendLog("→ Monitor mode");
    }

    [RelayCommand]
    private void QuerySystemInfo()
    {
        if (!IsConnected) return;
        _serial.Flush();
        _serial.Send('I');
        AppendLog("→ System info query");
    }

    [RelayCommand]
    private void ToggleLoadCell()
    {
        if (!IsConnected) return;
        _serial.Flush();
        _serial.Send('L');
        AppendLog("→ Toggle load cell type");
    }

    // ══════════════════════════════════════════════════════════════
    // Commands — Encoder mode
    // ══════════════════════════════════════════════════════════════
    [RelayCommand]
    private void EnterEncoderMode()
    {
        if (!IsConnected) return;
        _serial.Flush();
        _serial.Send('E');
        IsEncoderMode = true;
        AppendLog("→ Encoder mode");
    }

    [RelayCommand]
    private void ExitEncoderMode()
    {
        _serial.Send('q');
        IsEncoderMode = false;
        AppendLog("→ Exit encoder mode");
    }

    [RelayCommand]
    private void EncoderSetHome()
    {
        _serial.Send('0');
        AppendLog("→ Encoder: Set Home");
    }

    [RelayCommand]
    private void EncoderSetTarget()
    {
        _serial.Send('p');
        AppendLog("→ Encoder: Set Target");
    }

    [RelayCommand]
    private void EncoderGoHome()
    {
        _serial.Send('h');
        AppendLog("→ Encoder: Go Home");
    }

    [RelayCommand]
    private void EncoderGoTarget()
    {
        _serial.Send('g');
        AppendLog("→ Encoder: Go Target");
    }

    [RelayCommand]
    private void EncoderTare()
    {
        _serial.Send('t');
        AppendLog("→ Encoder: Tare");
    }

    // ══════════════════════════════════════════════════════════════
    // Commands — Manual Outlier Toggle
    // ══════════════════════════════════════════════════════════════
    [RelayCommand]
    private void ToggleOutlier()
    {
        if (SelectedRunResult == null) return;
        var row = SelectedRunResult;
        string key = $"{row.SpeedName}_{row.Batch}";
        if (!_allData.TryGetValue(key, out var group)) return;

        int idx = row.RunNumber - 1;
        if (group.ManualOutlierIndices.Contains(idx))
        {
            group.ManualOutlierIndices.Remove(idx);
            AppendLog($"✓ Unmarked outlier: {row.SpeedName} B{row.Batch} #{row.RunNumber}");
        }
        else
        {
            group.ManualOutlierIndices.Add(idx);
            AppendLog($"✓ Marked as outlier: {row.SpeedName} B{row.Batch} #{row.RunNumber} ({row.PeakForce:F6} N)");
        }

        group.ComputeOutliers();
        UpdateStatsTable();
        RefreshOutlierFlags();
    }

    // ══════════════════════════════════════════════════════════════
    // Commands — Export
    // ══════════════════════════════════════════════════════════════
    [RelayCommand]
    private void ExportExcel()
    {
        if (_allData.Count == 0)
        {
            AppendLog("✗ No data to export");
            return;
        }
        try
        {
            var dlg = new SaveFileDialog
            {
                Title = "Export Excel",
                Filter = "Excel Files (*.xlsx)|*.xlsx",
                FileName = $"surface_tension_results_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
                InitialDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "SurfaceTensionApp"),
            };
            if (dlg.ShowDialog() != true) return;

            string dir = Path.GetDirectoryName(dlg.FileName)!;
            string path = ExcelExportService.Export(_allData, dir);
            AppendLog($"✓ Excel saved: {path}");
            MessageBox.Show($"Saved to:\n{path}", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppendLog($"✗ Export error: {ex.Message}");
            MessageBox.Show(ex.Message, "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void ClearGraph()
    {
        LiveTimes.Clear();
        LiveForces.Clear();
        LiveGraphCleared?.Invoke();
    }

    /// <summary>Raised when only the live (in-progress) plot should be cleared.</summary>
    public event Action? LiveGraphCleared;

    /// <summary>Raised when graph should be fully cleared (all completed runs too).</summary>
    public event Action? GraphCleared;

    [RelayCommand]
    private void ClearAll()
    {
        // Safety prompt to prevent accidental data loss
        if (_allData.Count > 0 || LiveTimes.Count > 0)
        {
            var result = MessageBox.Show("Are you sure you want to clear all current data and graphs?",
                                          "Confirm Clear", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result \!= MessageBoxResult.Yes) return;
        }

        _allData.Clear();
        _currentRun = null;
        _totalRuns = 0;
        RunResults.Clear();
        StatsRows.Clear();
        LiveTimes.Clear();
        LiveForces.Clear();
        IsAutoRunning = false;
        AutoProgress = 0;
        AutoStatusText = "Idle";
        // Use GraphCleared (not GraphDataUpdated) to fully remove all scatter plots
        GraphCleared?.Invoke();
        AppendLog("✓ All data cleared");
    }

    // ══════════════════════════════════════════════════════════════
    // Serial line handler — the heart of the state machine
    // ══════════════════════════════════════════════════════════════
    private void OnLineReceived(string line)
    {
        _dispatcher.Invoke(() => ProcessLine(line));
    }

    private void ProcessLine(string line)
    {
        // Append to log (throttle to avoid flooding)
        AppendLog(line, isRaw: true);

        // ── JSON data point ──
        if (line.StartsWith('{') && line.EndsWith('}'))
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                double t = root.TryGetProperty("t", out var tv) ? tv.GetDouble() : 0;
                double f = root.TryGetProperty("f", out var fv) ? fv.GetDouble() : 0;
                double p = root.TryGetProperty("p", out var pv) ? pv.GetDouble() : 0;
                double pr = root.TryGetProperty("pr", out var prv) ? prv.GetDouble() : p;

                // LiveForce update removed for performance — use Monitor mode instead

                if (_currentRun != null)
                {
                    _currentRun.Times.Add(t);
                    _currentRun.Forces.Add(f);
                    _currentRun.Positions.Add(p);
                    _currentRun.RelPositions.Add(pr);

                    LiveTimes.Add(t);
                    LiveForces.Add(f);

                    // Throttle graph updates (~20 Hz)
                    if (_currentRun.Times.Count % 5 == 0)
                        GraphDataUpdated?.Invoke();
                }
            }
            catch { }
            return;
        }

        // ── RUN_START ──
        if (line.StartsWith("RUN_START:"))
        {
            var parts = line.Split(':');
            if (parts.Length >= 3)
            {
                _currentSpeed = parts[1];
                string runInfo = parts[2];
                if (parts.Length >= 4 && int.TryParse(parts[3], out int b))
                    _currentBatch = b;

                _currentRun = new TestRun();
                LiveTimes.Clear();
                LiveForces.Clear();

                CurrentBatchText = $"Batch {_currentBatch}";
                CurrentSpeedText = _currentSpeed;
                CurrentRunText = $"Run {runInfo}";
                AutoStatusText = $"Auto: {_totalRuns}/{TOTAL_AUTO_RUNS}  |  {_currentSpeed} #{runInfo}";
            }
            return;
        }

        // ── START_STREAM (single test mode) ──
        if (line.Contains("START_STREAM"))
        {
            _currentRun = new TestRun();
            LiveTimes.Clear();
            LiveForces.Clear();
            AppendLog("▶ Stream started");
            return;
        }

        // ── END_STREAM ──
        if (line.Contains("END_STREAM"))
        {
            if (_currentRun != null)
                AppendLog($"■ Stream ended: {_currentRun.PointCount} points");
            return;
        }

        // ── READY / HOME_OK — save single test run ──
        if (line.Contains("READY") || line.Contains("HOME_OK") || line.Contains("HOME reached"))
        {
            AppendLog($"[DBG] READY/HOME: single={_isSingleTest} run={_currentRun != null} speed={_currentSpeed} pts={_currentRun?.PointCount ?? 0}");
            if (_isSingleTest && _currentRun != null && _currentSpeed != null && _currentRun.PointCount > 0)
            {
                double pk = _currentRun.ValidatedPeak ?? _currentRun.PeakForce;
                string dataKey = $"{_currentSpeed}_{_currentBatch}";

                if (!_allData.TryGetValue(dataKey, out var group))
                {
                    double spd = 0;
                    foreach (var sp in SpeedProfile.All)
                        if (sp.Name == _currentSpeed) { spd = sp.SpeedMmS; break; }

                    group = new SpeedGroup
                    {
                        Key = dataKey,
                        BaseName = _currentSpeed,
                        SpeedMmS = spd,
                        Batch = _currentBatch
                    };
                    _allData[dataKey] = group;
                }

                group.Runs.Add(_currentRun);
                group.PeakForces.Add(pk);
                _totalRuns++;

                LastPeakText = $"Peak: {pk:F5} N";

                RunResults.Add(new RunResultRow
                {
                    SpeedName = _currentSpeed,
                    Batch = _currentBatch,
                    RunNumber = group.Runs.Count,
                    PeakForce = pk,
                    Points = _currentRun.PointCount,
                });

                group.ComputeOutliers();
                UpdateStatsTable();
                RefreshOutlierFlags();

                // Save to database
                try { _db.SaveRun(_currentSessionId, _currentSpeed, group.SpeedMmS, _currentBatch, group.Runs.Count, _currentRun, false); }
                catch { }

                AppendLog($"✓ Single test saved: {_currentSpeed} Peak={pk:F5}N ({_currentRun.PointCount} pts)");
                _currentRun = null;
                _isSingleTest = false;
                GraphRunCompleted?.Invoke();

                // Custom auto: send next run if remaining
                if (_customAutoRemaining > 1 && _customAutoSpeedCmd != null && !IsEmergencyLocked)
                {
                    _customAutoRemaining--;
                    AutoProgress = AutoTotal - _customAutoRemaining;
                    AutoStatusText = $"Custom: {AutoProgress}/{AutoTotal} ({_currentSpeed})";
                    _isSingleTest = true;
                    _serial.Flush();
                    _serial.Send(_customAutoSpeedCmd);
                }
                else if (_customAutoRemaining > 0)
                {
                    _customAutoRemaining = 0;
                    IsAutoRunning = false;
                    AutoProgress = AutoTotal;
                    AutoStatusText = "Custom auto complete";
                    AppendLog($"✓ Custom auto complete: {AutoTotal} runs");
                }
            }
            return;
        }

        // ── CONTACT_AT ──
        if (line.Contains("CONTACT_AT:"))
        {
            if (_currentRun != null && double.TryParse(line.Split(':')[^1], out double cpos))
                _currentRun.ContactPosition = cpos;
            return;
        }

        // ── PEAK_VALIDATED ──
        if (line.Contains("PEAK_VALIDATED:"))
        {
            if (_currentRun != null && double.TryParse(line.Split(':')[^1], out double vpeak))
            {
                _currentRun.ValidatedPeak = vpeak;
                LastPeakText = $"Peak: {vpeak:F5} N";
            }
            return;
        }

        // ── RUN_END ──
        if (line.StartsWith("RUN_END:"))
        {
            var parts = line.Split(':');
            if (parts.Length >= 4 && _currentRun != null && _currentSpeed != null)
            {
                if (!double.TryParse(parts[3], out double pk))
                    pk = _currentRun.PeakForce;

                if (parts.Length >= 5 && int.TryParse(parts[4], out int b))
                    _currentBatch = b;

                string dataKey = $"{_currentSpeed}_{_currentBatch}";

                if (!_allData.TryGetValue(dataKey, out var group))
                {
                    double spd = 0;
                    foreach (var sp in SpeedProfile.All)
                        if (sp.Name == _currentSpeed) { spd = sp.SpeedMmS; break; }

                    group = new SpeedGroup
                    {
                        Key = dataKey,
                        BaseName = _currentSpeed,
                        SpeedMmS = spd,
                        Batch = _currentBatch
                    };
                    _allData[dataKey] = group;
                }

                group.Runs.Add(_currentRun);
                group.PeakForces.Add(pk);
                _totalRuns++;

                AutoProgress = _totalRuns;
                AutoStatusText = $"Auto: {_totalRuns}/{TOTAL_AUTO_RUNS}";
                LastPeakText = $"Peak: {pk:F5} N";

                // Add to results table
                RunResults.Add(new RunResultRow
                {
                    SpeedName = _currentSpeed,
                    Batch = _currentBatch,
                    RunNumber = group.Runs.Count,
                    PeakForce = pk,
                    Points = _currentRun.PointCount,
                });

                // Update stats
                group.ComputeOutliers();
                UpdateStatsTable();

                // Mark outliers in results table
                RefreshOutlierFlags();

                // Save to database
                try { _db.SaveRun(_currentSessionId, _currentSpeed, group.SpeedMmS, _currentBatch, group.Runs.Count, _currentRun, false); }
                catch { }

                _currentRun = null;
                GraphRunCompleted?.Invoke();
            }
            return;
        }

        // ── SPEED_STATS ──
        if (line.StartsWith("SPEED_STATS:"))
        {
            // Arduino per-batch stats — we compute our own, just log it
            return;
        }

        // ── BATCH_COMPLETE ──
        if (line.StartsWith("BATCH_COMPLETE:"))
        {
            if (int.TryParse(line.Split(':')[^1], out int bn))
                AppendLog($"✓ Batch {bn}/{AUTO_BATCHES} complete");
            return;
        }

        // ── ALL BATCHES COMPLETE ──
        if (line.Contains("ALL BATCHES COMPLETE"))
        {
            IsAutoRunning = false;
            AutoStatusText = $"Complete! {_totalRuns} runs";
            AppendLog($"✓ All batches complete: {_totalRuns} runs");
            return;
        }

        // ── Encoder mode messages ──
        if (line.Contains("ENC_HOME:"))
        {
            EncoderHome = line.Split(':')[^1].Trim() + " mm";
            return;
        }
        if (line.Contains("ENC_TARGET:"))
        {
            EncoderTarget = line.Split(':')[^1].Trim() + " mm";
            return;
        }
        if (line.Contains("ENC_EXIT"))
        {
            IsEncoderMode = false;
            return;
        }

        // ── OVERLOAD ──
        if (line.Contains("OVERLOAD"))
        {
            AppendLog("⚠ OVERLOAD detected!");
            return;
        }

        // ── System Info responses (from 'I' command) ──
        if (line.StartsWith("FIRMWARE:"))
        {
            FirmwareVersion = line[9..];
            return;
        }
        if (line.StartsWith("LOADCELL_TYPE:"))
        {
            LoadCellType = line[14..];
            return;
        }
        if (line.StartsWith("LOADCELL_CAP:"))
        {
            LoadCellCapacity = line[13..];
            return;
        }
        if (line.StartsWith("CAL_FACTOR:"))
        {
            CalFactor = line[11..];
            return;
        }
        if (line.StartsWith("OVERLOAD_LIM:"))
        {
            OverloadLimit = line[13..];
            return;
        }
        if (line == "END_INFO")
        {
            ConnectionStatus = $"Connected ({_serial.PortName}) — {LoadCellType} {LoadCellCapacity}";
            AppendLog($"✓ System info: {FirmwareVersion} | Load Cell: {LoadCellType} ({LoadCellCapacity}) | Cal: {CalFactor}");
            return;
        }

        // ── LOADCELL_CHANGED (from 'L' command) ──
        if (line.StartsWith("LOADCELL_CHANGED:"))
        {
            LoadCellType = line[17..]; // "100G" or "30G"
            AppendLog($"✓ Load cell changed to {LoadCellType}");
            // Re-query system info to update all fields
            QuerySystemInfo();
            MessageBox.Show(
                $"Load cell switched to {LoadCellType}.\n\nRecalibration is recommended!\nUse Tools > Calibration Wizard or press Calibrate.",
                "Load Cell Changed", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // ── TARE_OK ──
        if (line.Contains("TARE_OK"))
        {
            AppendLog("✓ Tare completed");
            return;
        }

        // ── Calibration responses ──
        if (line.StartsWith("CAL_OK") || line.StartsWith("CAL_DONE"))
        {
            AppendLog("✓ Calibration completed successfully");
            // Re-query system info to get updated calibration factor
            QuerySystemInfo();
            return;
        }
        if (line.StartsWith("CAL_FACTOR:") && !line.Contains("LOADCELL"))
        {
            // Standalone calibration factor update (outside INFO block)
            CalFactor = line[11..];
            AppendLog($"✓ New calibration factor: {CalFactor}");
            return;
        }
        if (line.StartsWith("CAL_ERR") || line.StartsWith("CAL_FAIL"))
        {
            AppendLog($"✗ Calibration error: {line}");
            return;
        }

        // ── Monitor force line ──
        if (line.Contains("Force:"))
        {
            // Extract force value for live display
            var idx = line.IndexOf("Force:");
            if (idx >= 0)
            {
                var sub = line[(idx + 6)..].Trim().Split(' ')[0];
                if (double.TryParse(sub, out double f))
                    LiveForce = $"{f:F5} N";
            }
        }
    }

    // ══════════════════════════════════════════════════════════════
    // Helpers
    // ══════════════════════════════════════════════════════════════
    private void UpdateStatsTable()
    {
        StatsRows.Clear();
        foreach (var (key, g) in _allData.OrderByDescending(x => x.Value.SpeedMmS).ThenBy(x => x.Value.Batch))
        {
            StatsRows.Add(new StatsRow
            {
                SpeedName = g.BaseName,
                Batch = g.Batch,
                TotalRuns = g.PeakForces.Count,
                ValidRuns = g.CleanPeaks.Count,
                Average = Math.Round(g.Avg, 6),
                StdDev = Math.Round(g.Std, 6),
                RsdPercent = Math.Round(g.Rsd, 2),
                Outliers = g.OutlierIndices.Count,
            });
        }
    }

    private void RefreshOutlierFlags()
    {
        foreach (var row in RunResults)
        {
            string key = $"{row.SpeedName}_{row.Batch}";
            if (_allData.TryGetValue(key, out var g))
                row.IsOutlier = g.OutlierIndices.Contains(row.RunNumber - 1);
        }
    }

    private int _logLineCount;
    private void AppendLog(string text, bool isRaw = false)
    {
        lock (_logLock)
        {
            _logLineCount++;
            // Keep last 500 lines
            if (_logLineCount > 500)
            {
                var idx = SerialLog.IndexOf('\n');
                if (idx > 0) SerialLog = SerialLog[(idx + 1)..];
                _logLineCount--;
            }
            SerialLog += (isRaw ? "" : "[APP] ") + text + "\n";
        }
    }

    // ══════════════════════════════════════════════════════════════
    // Commands — PDF Report
    // ══════════════════════════════════════════════════════════════
    /// <summary>Set by MainWindow to capture graph as PNG bytes.</summary>
    public Func<byte[]?>? CaptureGraphImage { get; set; }

    [RelayCommand]
    private void ExportPdf()
    {
        if (_allData.Count == 0)
        {
            AppendLog("✗ No data for PDF report");
            return;
        }
        try
        {
            var dlg = new SaveFileDialog
            {
                Title = "Export PDF Report",
                Filter = "PDF Files (*.pdf)|*.pdf",
                FileName = $"SurfaceTension_Report_{DateTime.Now:yyyyMMdd_HHmmss}.pdf",
                InitialDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "SurfaceTensionApp"),
            };
            if (dlg.ShowDialog() != true) return;

            byte[]? graphImage = CaptureGraphImage?.Invoke();
            string dir = Path.GetDirectoryName(dlg.FileName)!;
            string path = PdfReportService.GenerateReport(
                _allData, dir, graphImage,
                config: Config,
                spikeFilterEnabled: IsSpikeFilterEnabled,
                spikeThreshold: SpikeThreshold);
            AppendLog($"✓ PDF report saved: {path}");
            MessageBox.Show($"Report saved to:\n{path}", "PDF Report", MessageBoxButton.OK, MessageBoxImage.Information);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppendLog($"✗ PDF error: {ex.Message}");
            MessageBox.Show(ex.Message, "PDF Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ══════════════════════════════════════════════════════════════
    // Commands — Session management
    // ══════════════════════════════════════════════════════════════
    [RelayCommand]
    private void RefreshSessions()
    {
        Sessions.Clear();
        foreach (var s in _db.GetSessions())
            Sessions.Add(s);
    }

    [RelayCommand]
    private void LoadSession()
    {
        if (SelectedSession == null) return;
        try
        {
            _allData.Clear();
            RunResults.Clear();
            StatsRows.Clear();
            _totalRuns = 0;
            LiveTimes.Clear();
            LiveForces.Clear();

            var loaded = _db.LoadSession(SelectedSession.Id);
            foreach (var (key, group) in loaded)
            {
                _allData[key] = group;
                for (int i = 0; i < group.Runs.Count; i++)
                {
                    _totalRuns++;
                    RunResults.Add(new RunResultRow
                    {
                        SpeedName = group.BaseName,
                        Batch = group.Batch,
                        RunNumber = i + 1,
                        PeakForce = group.PeakForces[i],
                        Points = group.Runs[i].PointCount,
                    });
                }
            }

            UpdateStatsTable();
            RefreshOutlierFlags();
            GraphCleared?.Invoke();
            AppendLog($"✓ Loaded session: {SelectedSession.Name} ({_totalRuns} runs)");
        }
        catch (Exception ex)
        {
            AppendLog($"✗ Load error: {ex.Message}");
        }
    }

    [RelayCommand]
    private void DeleteSession()
    {
        if (SelectedSession == null) return;
        var result = MessageBox.Show(
            $"Delete session '{SelectedSession.Name}'?\nThis cannot be undone.",
            "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
        {
            _db.DeleteSession(SelectedSession.Id);
            RefreshSessions();
            AppendLog("✓ Session deleted");
        }
    }

    [RelayCommand]
    private void NewSession()
    {
        _currentSessionId = _db.CreateSession();
        _allData.Clear();
        RunResults.Clear();
        StatsRows.Clear();
        _totalRuns = 0;
        LiveTimes.Clear();
        LiveForces.Clear();
        GraphCleared?.Invoke();
        RefreshSessions();
        AppendLog("✓ New session started");
    }

    public void Dispose()
    {
        _serial.Dispose();
        _db.Dispose();
        GC.SuppressFinalize(this);
    }
}