using System.Collections.ObjectModel;
using System.IO;
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
    private const int NUM_SPEEDS   = 12;
    private const int AUTO_REPEATS = 10;
    private const int AUTO_BATCHES = 2;
    private static readonly int TOTAL_AUTO_RUNS = AUTO_BATCHES * NUM_SPEEDS * AUTO_REPEATS; // 240

    // ══════════════════════════════════════════════════════════════
    // Services
    // ══════════════════════════════════════════════════════════════
    private readonly SerialService         _serial   = new();
    private readonly DatabaseService       _db       = new();
    private readonly SerialProtocolHandler _protocol = new();
    private readonly RunProcessingService  _runs     = new();
    private readonly Dispatcher            _dispatcher;
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
    [ObservableProperty] private bool   _isConnected;
    [ObservableProperty] private string _connectionStatus = "Disconnected";

    // ══════════════════════════════════════════════════════════════
    // Encoder mode
    // ══════════════════════════════════════════════════════════════
    [ObservableProperty] private bool   _isEncoderMode;
    [ObservableProperty] private string _encoderHome   = "—";
    [ObservableProperty] private string _encoderTarget = "—";

    // ══════════════════════════════════════════════════════════════
    // Auto sequence progress
    // ══════════════════════════════════════════════════════════════
    [ObservableProperty] private bool   _isAutoRunning;
    [ObservableProperty] private int    _autoProgress;
    [ObservableProperty] private int    _autoTotal    = TOTAL_AUTO_RUNS;
    [ObservableProperty] private string _autoStatusText  = "Idle";
    [ObservableProperty] private string _currentBatchText = "";
    [ObservableProperty] private string _currentSpeedText  = "";
    [ObservableProperty] private string _currentRunText    = "";
    [ObservableProperty] private string _lastPeakText      = "";

    // ══════════════════════════════════════════════════════════════
    // Live force display
    // ══════════════════════════════════════════════════════════════
    [ObservableProperty] private string _liveForce = "0.00000 N";

    // ══════════════════════════════════════════════════════════════
    // Emergency Stop lock
    // ══════════════════════════════════════════════════════════════
    [ObservableProperty] private bool _isEmergencyLocked;

    // ══════════════════════════════════════════════════════════════
    // Custom Auto Repeat
    // ══════════════════════════════════════════════════════════════
    [ObservableProperty] private string _customRepeatCount    = "10";
    [ObservableProperty] private string _selectedSpeedOption  = "1: ULTRA_FAST (600 µm/s)";

    // ══════════════════════════════════════════════════════════════
    // System Info Properties
    // ══════════════════════════════════════════════════════════════
    [ObservableProperty] private string _firmwareVersion  = "—";
    [ObservableProperty] private string _loadCellType     = "—";
    [ObservableProperty] private string _loadCellCapacity = "—";
    [ObservableProperty] private string _calFactor        = "—";
    [ObservableProperty] private string _overloadLimit    = "—";

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

    public ObservableCollection<string> MethodOptions   { get; } = new() { "Du Noüy Ring", "Wilhelmy Plate" };
    public ObservableCollection<string> LoadCellOptions { get; } = new() { "100g", "30g" };
    public ObservableCollection<string> UnitOptions     { get; } = new() { "mN/m", "dyn/cm" };

    // ══════════════════════════════════════════════════════════════
    // Results & Statistics
    // ══════════════════════════════════════════════════════════════
    public ObservableCollection<RunResultRow> RunResults { get; } = new();
    public ObservableCollection<StatsRow>     StatsRows  { get; } = new();
    [ObservableProperty] private RunResultRow? _selectedRunResult;

    // ══════════════════════════════════════════════════════════════
    // Serial Log
    // ══════════════════════════════════════════════════════════════
    [ObservableProperty] private string _serialLog     = "";
    [ObservableProperty] private bool   _showSerialLog = true;
    private readonly object _logLock = new();

    // ══════════════════════════════════════════════════════════════
    // Spike filter
    // ══════════════════════════════════════════════════════════════
    [ObservableProperty] private bool   _isSpikeFilterEnabled;
    [ObservableProperty] private string _spikeThresholdText = "";

    public double? SpikeThreshold =>
        double.TryParse(SpikeThresholdText, out double v) && v > 0 ? v : null;

    partial void OnIsSpikeFilterEnabledChanged(bool value)
    {
        SpikeFilterToggled?.Invoke();
        string thresholdInfo = SpikeThreshold.HasValue
            ? $", threshold={SpikeThreshold.Value} N" : ", auto-detect";
        AppendLog(value ? $"✓ Spike filter ON{thresholdInfo}" : "✓ Spike filter OFF");
    }

    partial void OnSpikeThresholdTextChanged(string value)
    {
        if (IsSpikeFilterEnabled) SpikeFilterToggled?.Invoke();
    }

    // ══════════════════════════════════════════════════════════════
    // Graph events (consumed by MainWindow code-behind)
    // ══════════════════════════════════════════════════════════════
    public event Action? GraphDataUpdated;
    public event Action? GraphRunCompleted;
    public event Action? SpikeFilterToggled;
    public event Action? LiveGraphCleared;
    public event Action? GraphCleared;
    public event Action? SessionLoaded;

    // ══════════════════════════════════════════════════════════════
    // Data access for code-behind (graph rendering)
    // ══════════════════════════════════════════════════════════════
    public List<double> LiveTimes  { get; } = new();
    public List<double> LiveForces { get; } = new();
    public IReadOnlyDictionary<string, SpeedGroup> AllData => _runs.AllData;

    // ── PDF capture callback (set by MainWindow) ──
    public Func<byte[]?>? CaptureGraphImage { get; set; }

    // ══════════════════════════════════════════════════════════════
    // Current-run control state
    // ══════════════════════════════════════════════════════════════
    private TestRun? _currentRun;
    private string?  _currentSpeed;
    private int      _currentBatch = 1;
    private bool     _isSingleTest;
    private int      _customAutoRemaining;
    private string?  _customAutoSpeedCmd;

    // ══════════════════════════════════════════════════════════════
    // Constructor
    // ══════════════════════════════════════════════════════════════
    public MainViewModel()
    {
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        WireProtocolEvents();

        _serial.LineReceived      += line => _dispatcher.Invoke(() => _protocol.Process(line));
        _serial.ConnectionChanged += c    => _dispatcher.Invoke(() =>
        {
            IsConnected      = c;
            ConnectionStatus = c ? $"Connected ({_serial.PortName})" : "Disconnected";
        });

        RefreshPorts();
        _currentSessionId = _db.CreateSession();
        RefreshSessions();
    }

    // ══════════════════════════════════════════════════════════════
    // Protocol event wiring
    // ══════════════════════════════════════════════════════════════
    private void WireProtocolEvents()
    {
        _protocol.AnyLineReceived      += line  => AppendLog(line, isRaw: true);
        _protocol.DataPointReceived    += OnDataPoint;
        _protocol.RunStartReceived     += OnRunStart;
        _protocol.StreamStarted        += ()    => AppendLog("▶ Stream started");
        _protocol.StreamEnded          += ()    => AppendLog($"■ Stream ended: {_currentRun?.PointCount ?? 0} pts");
        _protocol.ReadyOrHomeReceived  += OnReadyOrHome;
        _protocol.ContactAt            += pos   => { if (_currentRun != null) _currentRun.ContactPosition = pos; };
        _protocol.PeakValidated        += peak  => { if (_currentRun != null) { _currentRun.ValidatedPeak = peak; LastPeakText = $"Peak: {peak:F5} N"; } };
        _protocol.RunEndReceived       += OnRunEnd;
        _protocol.BatchComplete        += bn    => AppendLog($"✓ Batch {bn}/{AUTO_BATCHES} complete");
        _protocol.AllBatchesComplete   += ()    => { IsAutoRunning = false; AutoStatusText = $"Complete! {_runs.TotalRuns} runs"; AppendLog($"✓ All batches complete: {_runs.TotalRuns} runs"); };
        _protocol.EncoderHomeReceived  += v     => EncoderHome   = v + " mm";
        _protocol.EncoderTargetReceived+= v     => EncoderTarget = v + " mm";
        _protocol.EncoderExited        += ()    => IsEncoderMode = false;
        _protocol.OverloadDetected     += ()    => AppendLog("⚠ OVERLOAD detected!");
        _protocol.FirmwareReceived     += v     => FirmwareVersion  = v;
        _protocol.LoadCellTypeReceived += v     => LoadCellType     = v;
        _protocol.LoadCellCapReceived  += v     => LoadCellCapacity = v;
        _protocol.CalFactorReceived    += v     => CalFactor        = v;
        _protocol.OverloadLimitReceived+= v     => OverloadLimit    = v;
        _protocol.SystemInfoComplete   += ()    => { ConnectionStatus = $"Connected ({_serial.PortName}) — {LoadCellType} {LoadCellCapacity}"; AppendLog($"✓ System info: {FirmwareVersion} | Load Cell: {LoadCellType} ({LoadCellCapacity}) | Cal: {CalFactor}"); };
        _protocol.LoadCellChanged      += OnLoadCellChanged;
        _protocol.TareOk               += ()    => AppendLog("✓ Tare completed");
        _protocol.CalibrationOk        += ()    => { AppendLog("✓ Calibration completed successfully"); QuerySystemInfo(); };
        _protocol.CalibrationError     += msg   => AppendLog($"✗ Calibration error: {msg}");
        _protocol.MonitorForce         += f     => LiveForce = $"{f:F5} N";
    }

    // ══════════════════════════════════════════════════════════════
    // Protocol event handlers — run lifecycle
    // ══════════════════════════════════════════════════════════════
    private void OnDataPoint(double t, double f, double p, double pr)
    {
        if (_currentRun == null) return;
        _currentRun.Times.Add(t);
        _currentRun.Forces.Add(f);
        _currentRun.Positions.Add(p);
        _currentRun.RelPositions.Add(pr);
        LiveTimes.Add(t);
        LiveForces.Add(f);
        // Throttle graph updates (~20 Hz at 100 Hz data rate)
        if (_currentRun.Times.Count % 5 == 0)
            GraphDataUpdated?.Invoke();
    }

    private void OnRunStart(string speed, string runInfo, int batch)
    {
        _currentSpeed = speed;
        _currentBatch = batch;
        _currentRun   = new TestRun();
        LiveTimes.Clear();
        LiveForces.Clear();
        CurrentBatchText = $"Batch {batch}";
        CurrentSpeedText = speed;
        CurrentRunText   = $"Run {runInfo}";
        AutoStatusText   = $"Auto: {_runs.TotalRuns}/{TOTAL_AUTO_RUNS}  |  {speed} #{runInfo}";
    }

    private void OnReadyOrHome()
    {
        AppendLog($"[DBG] READY/HOME: single={_isSingleTest} run={_currentRun != null} speed={_currentSpeed} pts={_currentRun?.PointCount ?? 0}");

        if (!_isSingleTest || _currentRun == null || _currentSpeed == null || _currentRun.PointCount == 0)
            return;

        SaveSingleRun();
    }

    private void SaveSingleRun()
    {
        double pk  = _currentRun!.ValidatedPeak ?? _currentRun.PeakForce;
        double spd = SpeedProfile.All.FirstOrDefault(s => s.Name == _currentSpeed)?.SpeedMmS ?? 0;
        var group  = _runs.GetOrCreateGroup(_currentSpeed!, spd, _currentBatch);

        _runs.AddRun(group, _currentRun, pk);
        LastPeakText = $"Peak: {pk:F5} N";

        RunResults.Add(new RunResultRow
        {
            SpeedName = _currentSpeed!,
            Batch     = _currentBatch,
            RunNumber = group.Runs.Count,
            PeakForce = pk,
            Points    = _currentRun.PointCount,
        });

        RefreshStatsAndOutliers();

        try
        {
            _db.SaveRun(_currentSessionId, _currentSpeed!, group.SpeedMmS, _currentBatch, group.Runs.Count, _currentRun, false);
        }
        catch (Exception ex)
        {
            AppendLog($"[WARN] DB save failed: {ex.Message}");
        }

        AppendLog($"✓ Single test saved: {_currentSpeed} Peak={pk:F5}N ({_currentRun.PointCount} pts)");
        _currentRun  = null;
        _isSingleTest = false;
        GraphRunCompleted?.Invoke();

        AdvanceCustomAuto();
    }

    private void AdvanceCustomAuto()
    {
        if (_customAutoRemaining > 1 && _customAutoSpeedCmd != null && !IsEmergencyLocked)
        {
            _customAutoRemaining--;
            AutoProgress   = AutoTotal - _customAutoRemaining;
            AutoStatusText = $"Custom: {AutoProgress}/{AutoTotal} ({_currentSpeed})";
            _isSingleTest  = true;
            _serial.Flush();
            _serial.Send(_customAutoSpeedCmd);
        }
        else if (_customAutoRemaining > 0)
        {
            _customAutoRemaining = 0;
            IsAutoRunning  = false;
            AutoProgress   = AutoTotal;
            AutoStatusText = "Custom auto complete";
            AppendLog($"✓ Custom auto complete: {AutoTotal} runs");
        }
    }

    private void OnRunEnd(double peakForce, int batch)
    {
        if (_currentRun == null || _currentSpeed == null) return;

        _currentBatch = batch;
        double pk  = peakForce != 0 ? peakForce : _currentRun.PeakForce;
        double spd = SpeedProfile.All.FirstOrDefault(s => s.Name == _currentSpeed)?.SpeedMmS ?? 0;
        var group  = _runs.GetOrCreateGroup(_currentSpeed, spd, batch);

        _runs.AddRun(group, _currentRun, pk);
        AutoProgress   = _runs.TotalRuns;
        AutoStatusText = $"Auto: {_runs.TotalRuns}/{TOTAL_AUTO_RUNS}";
        LastPeakText   = $"Peak: {pk:F5} N";

        RunResults.Add(new RunResultRow
        {
            SpeedName = _currentSpeed,
            Batch     = batch,
            RunNumber = group.Runs.Count,
            PeakForce = pk,
            Points    = _currentRun.PointCount,
        });

        RefreshStatsAndOutliers();

        try
        {
            _db.SaveRun(_currentSessionId, _currentSpeed, group.SpeedMmS, batch, group.Runs.Count, _currentRun, false);
        }
        catch (Exception ex)
        {
            AppendLog($"[WARN] DB save failed: {ex.Message}");
        }

        _currentRun = null;
        GraphRunCompleted?.Invoke();
    }

    private void OnLoadCellChanged(string type)
    {
        LoadCellType = type;
        AppendLog($"✓ Load cell changed to {type}");
        QuerySystemInfo();
        MessageBox.Show(
            $"Load cell switched to {type}.\n\nRecalibration is recommended!\nUse Tools > Calibration Wizard or press Calibrate.",
            "Load Cell Changed", MessageBoxButton.OK, MessageBoxImage.Information);
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
                QuerySystemInfo();
            }
            else
            {
                AppendLog($"✗ Failed to connect to {SelectedPort}");
            }
        }
    }

    // ══════════════════════════════════════════════════════════════
    // Commands — Control panel
    // ══════════════════════════════════════════════════════════════
    [RelayCommand]
    private void RunSpeed(string speedNum)
    {
        if (!IsConnected || IsEmergencyLocked) return;
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
        _runs.Clear();
        RunResults.Clear();
        StatsRows.Clear();
        _currentBatch = 1;
        _isSingleTest = false;
        IsAutoRunning = true;
        AutoProgress  = 0;
        AutoStatusText = $"Auto: 0/{TOTAL_AUTO_RUNS}";
        _serial.Flush();
        _serial.Send('A');
        AppendLog($"→ Auto sequence started ({TOTAL_AUTO_RUNS} runs: {AUTO_BATCHES}x{NUM_SPEEDS}x{AUTO_REPEATS})");
    }

    [RelayCommand] private void Tare()     { if (!IsConnected) return; _serial.Flush(); _serial.Send('t'); AppendLog("→ Tare"); }
    [RelayCommand] private void SetHome()  { if (!IsConnected) return; _serial.Flush(); _serial.Send('0'); AppendLog("→ Set Home"); }
    [RelayCommand] private void GoHome()   { if (!IsConnected) return; _serial.Flush(); _serial.Send('h'); AppendLog("→ Go Home"); }
    [RelayCommand] private void Calibrate(){ if (!IsConnected) return; _serial.Flush(); _serial.Send('k'); AppendLog("→ Calibrate"); }
    [RelayCommand] private void Monitor()  { if (!IsConnected) return; _serial.Flush(); _serial.Send('m'); AppendLog("→ Monitor mode"); }

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
            _serial.Send('r');
            IsEmergencyLocked = false;
            AppendLog("🔓 Emergency unlocked — system ready");
            return;
        }
        _serial.Send('q');
        IsEmergencyLocked    = true;
        IsAutoRunning        = false;
        _isSingleTest        = false;
        _currentRun          = null;
        _customAutoRemaining = 0;
        AutoStatusText       = "EMERGENCY STOPPED";
        AppendLog("⚠ EMERGENCY STOP — all operations halted. Click again to unlock.");
    }

    [RelayCommand]
    private void GoTarget()
    {
        if (!IsConnected || IsEmergencyLocked) return;
        _serial.Flush();
        _serial.Send('g');
        AppendLog("→ Go Target");
    }

    // ── Custom Auto Repeat ──────────────────────────────────────
    [RelayCommand]
    private void RunCustomAuto()
    {
        if (!IsConnected || IsEmergencyLocked) return;
        if (!int.TryParse(CustomRepeatCount, out int count) || count < 1 || count > 999)
        {
            AppendLog("✗ Invalid repeat count (1-999)");
            return;
        }

        string speedCmd = SelectedSpeedOption.Split(':')[0].Trim();
        var profile = SpeedProfile.All.FirstOrDefault(s => s.SerialCmd == speedCmd[0]);
        if (profile == null) return;

        _currentSpeed        = profile.Name;
        _currentBatch        = 1;
        _isSingleTest        = true;
        _customAutoRemaining = count;
        _customAutoSpeedCmd  = speedCmd;
        AutoTotal            = count;
        AutoProgress         = 0;
        IsAutoRunning        = true;
        AutoStatusText       = $"Custom: 0/{count} ({profile.Name})";

        _serial.Flush();
        _serial.Send(speedCmd);
        AppendLog($"→ Custom auto: {count}x {profile.Name}");
    }

    // ── System Info / Load Cell ─────────────────────────────────
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
    [RelayCommand] private void EnterEncoderMode()  { if (!IsConnected) return; _serial.Flush(); _serial.Send('E'); IsEncoderMode = true;  AppendLog("→ Encoder mode"); }
    [RelayCommand] private void ExitEncoderMode()   { _serial.Send('q'); IsEncoderMode = false; AppendLog("→ Exit encoder mode"); }
    [RelayCommand] private void EncoderSetHome()    { _serial.Send('0'); AppendLog("→ Encoder: Set Home"); }
    [RelayCommand] private void EncoderSetTarget()  { _serial.Send('p'); AppendLog("→ Encoder: Set Target"); }
    [RelayCommand] private void EncoderGoHome()     { _serial.Send('h'); AppendLog("→ Encoder: Go Home"); }
    [RelayCommand] private void EncoderGoTarget()   { _serial.Send('g'); AppendLog("→ Encoder: Go Target"); }
    [RelayCommand] private void EncoderTare()       { _serial.Send('t'); AppendLog("→ Encoder: Tare"); }

    // ══════════════════════════════════════════════════════════════
    // Commands — Manual Outlier Toggle
    // ══════════════════════════════════════════════════════════════
    [RelayCommand]
    private void ToggleOutlier()
    {
        if (SelectedRunResult == null) return;
        var row = SelectedRunResult;
        bool nowOutlier = _runs.ToggleManualOutlier(row.SpeedName, row.Batch, row.RunNumber - 1);
        AppendLog(nowOutlier
            ? $"✓ Marked as outlier: {row.SpeedName} B{row.Batch} #{row.RunNumber} ({row.PeakForce:F6} N)"
            : $"✓ Unmarked outlier: {row.SpeedName} B{row.Batch} #{row.RunNumber}");
        RefreshStatsAndOutliers();
    }

    // ══════════════════════════════════════════════════════════════
    // Commands — Graph
    // ══════════════════════════════════════════════════════════════
    [RelayCommand]
    private void ClearGraph()
    {
        LiveTimes.Clear();
        LiveForces.Clear();
        LiveGraphCleared?.Invoke();
    }

    [RelayCommand]
    private void ClearAll()
    {
        if (_runs.AllData.Count > 0 || LiveTimes.Count > 0)
        {
            if (MessageBox.Show("Are you sure you want to clear all current data and graphs?",
                    "Confirm Clear", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                != MessageBoxResult.Yes) return;
        }

        _runs.Clear();
        _currentRun   = null;
        RunResults.Clear();
        StatsRows.Clear();
        LiveTimes.Clear();
        LiveForces.Clear();
        IsAutoRunning  = false;
        AutoProgress   = 0;
        AutoStatusText = "Idle";
        GraphCleared?.Invoke();
        AppendLog("✓ All data cleared");
    }

    // ══════════════════════════════════════════════════════════════
    // Commands — Export
    // ══════════════════════════════════════════════════════════════
    [RelayCommand]
    private void ExportExcel()
    {
        if (_runs.AllData.Count == 0) { AppendLog("✗ No data to export"); return; }
        try
        {
            var dlg = new SaveFileDialog
            {
                Title  = "Export Excel",
                Filter = "Excel Files (*.xlsx)|*.xlsx",
                FileName = $"surface_tension_results_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
                InitialDirectory = DocDir(),
            };
            if (dlg.ShowDialog() != true) return;

            string path = ExcelExportService.Export(
                _runs.AllData.ToDictionary(k => k.Key, v => v.Value),
                Path.GetDirectoryName(dlg.FileName)!);
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
    private void ExportPdf()
    {
        if (_runs.AllData.Count == 0) { AppendLog("✗ No data for PDF report"); return; }
        try
        {
            var dlg = new SaveFileDialog
            {
                Title  = "Export PDF Report",
                Filter = "PDF Files (*.pdf)|*.pdf",
                FileName = $"SurfaceTension_Report_{DateTime.Now:yyyyMMdd_HHmmss}.pdf",
                InitialDirectory = DocDir(),
            };
            if (dlg.ShowDialog() != true) return;

            byte[]? graphImage = CaptureGraphImage?.Invoke();
            string path = PdfReportService.GenerateReport(
                _runs.AllData.ToDictionary(k => k.Key, v => v.Value),
                Path.GetDirectoryName(dlg.FileName)!,
                graphImage,
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
            _runs.Clear();
            RunResults.Clear();
            StatsRows.Clear();
            LiveTimes.Clear();
            LiveForces.Clear();

            var loaded = _db.LoadSession(SelectedSession.Id);
            _runs.LoadData(loaded);

            foreach (var (_, group) in _runs.AllData)
            {
                for (int i = 0; i < group.Runs.Count; i++)
                {
                    RunResults.Add(new RunResultRow
                    {
                        SpeedName = group.BaseName,
                        Batch     = group.Batch,
                        RunNumber = i + 1,
                        PeakForce = group.PeakForces[i],
                        Points    = group.Runs[i].PointCount,
                    });
                }
            }

            RefreshStatsAndOutliers();
            GraphCleared?.Invoke();
            AppendLog($"✓ Loaded session: {SelectedSession.Name} ({_runs.TotalRuns} runs)");
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
        if (MessageBox.Show($"Delete session '{SelectedSession.Name}'?\nThis cannot be undone.",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning)
            == MessageBoxResult.Yes)
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
        _runs.Clear();
        RunResults.Clear();
        StatsRows.Clear();
        LiveTimes.Clear();
        LiveForces.Clear();
        GraphCleared?.Invoke();
        RefreshSessions();
        AppendLog("✓ New session started");
    }

    // ══════════════════════════════════════════════════════════════
    // Helpers
    // ══════════════════════════════════════════════════════════════
    private void RefreshStatsAndOutliers()
    {
        // Rebuild stats table
        StatsRows.Clear();
        foreach (var row in _runs.BuildStatsRows())
            StatsRows.Add(row);

        // Sync outlier flags on RunResultRow
        foreach (var row in RunResults)
            row.IsOutlier = _runs.IsOutlier(row.SpeedName, row.Batch, row.RunNumber - 1);
    }

    private int _logLineCount;
    private void AppendLog(string text, bool isRaw = false)
    {
        lock (_logLock)
        {
            _logLineCount++;
            if (_logLineCount > 500)
            {
                int idx = SerialLog.IndexOf('\n');
                if (idx > 0) SerialLog = SerialLog[(idx + 1)..];
                _logLineCount--;
            }
            SerialLog += (isRaw ? "" : "[APP] ") + text + "\n";
        }
    }

    private static string DocDir() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SurfaceTensionApp");

    public void Dispose()
    {
        _serial.Dispose();
        _db.Dispose();
        GC.SuppressFinalize(this);
    }
}
