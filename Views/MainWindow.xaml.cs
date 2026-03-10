using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using ScottPlot;
using SurfaceTensionApp.Models;
using SurfaceTensionApp.ViewModels;

namespace SurfaceTensionApp.Views;

public class RunPlotEntry
{
    public int RunIndex { get; init; }
    public ScottPlot.Plottables.Scatter Scatter { get; set; } = null!;
    public ScottPlot.Plottables.Marker? PeakMarker { get; set; }
    public ScottPlot.Color OrigColor { get; set; }
    public bool IsVisible { get; set; } = true;
    public double[] RawTimes { get; init; } = Array.Empty<double>();
    public double[] RawForces { get; init; } = Array.Empty<double>();
}

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private ScottPlot.Plottables.Scatter? _livePlot;
    private readonly List<RunPlotEntry> _runEntries = new();
    private readonly DispatcherTimer _renderTimer;
    private bool _needsRender;

    private readonly record struct AxisState(double XMin, double XMax, double YMin, double YMax);
    private readonly List<AxisState> _viewHistory = new();
    private int _viewIndex = -1;
    private bool _suppressSave;

    private static readonly ScottPlot.Color[] RunColors =
    {
        ScottPlot.Color.FromHex("#4A9EFF"), ScottPlot.Color.FromHex("#FF6B6B"),
        ScottPlot.Color.FromHex("#44FF88"), ScottPlot.Color.FromHex("#FFD93D"),
        ScottPlot.Color.FromHex("#B088FF"), ScottPlot.Color.FromHex("#FF88C0"),
        ScottPlot.Color.FromHex("#88DDFF"), ScottPlot.Color.FromHex("#C0FF88"),
        ScottPlot.Color.FromHex("#FF9944"), ScottPlot.Color.FromHex("#AAAAAA"),
    };
    private int _colorIndex;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;
        SetupPlot();

        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _renderTimer.Tick += (_, _) => RenderFrame();
        _renderTimer.Start();

        _vm.GraphDataUpdated += () => _needsRender = true;
        _vm.GraphRunCompleted += OnGraphRunCompleted;
        _vm.GraphCleared += OnGraphCleared;
        _vm.LiveGraphCleared += OnLiveGraphCleared;
        _vm.SpikeFilterToggled += OnSpikeFilterToggled;
        _vm.CaptureGraphImage = CaptureGraph;
        _vm.SessionLoaded += OnSessionLoaded;
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SerialLog))
                Dispatcher.InvokeAsync(() => LogTextBox.ScrollToEnd(), DispatcherPriority.Background);
        };

        PlotControl.MouseUp += (s, e) => OnPlotInteracted(s, e);
        PlotControl.MouseWheel += (s, e) => OnPlotInteracted(s, e);

        Closed += (_, _) => { _renderTimer.Stop(); _vm.Dispose(); };
    }

    private void SetupPlot()
    {
        var p = PlotControl.Plot;
        p.FigureBackground.Color = ScottPlot.Color.FromHex("#1E1E2E");
        p.DataBackground.Color = ScottPlot.Color.FromHex("#0A0A18");
        p.Axes.Bottom.Label.Text = "Time (s)";
        p.Axes.Left.Label.Text = "Force (N)";
        p.Axes.Title.Label.Text = "Du Noüy Ring — Force vs Time (Live)";
        p.Axes.Bottom.Label.ForeColor = ScottPlot.Color.FromHex("#808098");
        p.Axes.Left.Label.ForeColor = ScottPlot.Color.FromHex("#808098");
        p.Axes.Title.Label.ForeColor = ScottPlot.Color.FromHex("#E0E0E0");
        p.Axes.Bottom.TickLabelStyle.ForeColor = ScottPlot.Color.FromHex("#808098");
        p.Axes.Left.TickLabelStyle.ForeColor = ScottPlot.Color.FromHex("#808098");
        p.Axes.Bottom.MajorTickStyle.Color = ScottPlot.Color.FromHex("#3A3A52");
        p.Axes.Left.MajorTickStyle.Color = ScottPlot.Color.FromHex("#3A3A52");
        p.Axes.Bottom.MinorTickStyle.Color = ScottPlot.Color.FromHex("#252538");
        p.Axes.Left.MinorTickStyle.Color = ScottPlot.Color.FromHex("#252538");
        p.Grid.MajorLineColor = ScottPlot.Color.FromHex("#2A2A3E");
        p.Add.HorizontalLine(0, 1, ScottPlot.Color.FromHex("#555555"));
        PlotControl.Refresh();
    }

    private void RenderFrame()
    {
        if (!_needsRender) return;
        _needsRender = false;
        try
        {
            var times = _vm.LiveTimes;
            var forces = _vm.LiveForces;
            if (times.Count < 2) return;
            if (_livePlot != null) PlotControl.Plot.Remove(_livePlot);

            // Live data is always raw — filtering is post-run only
            _livePlot = PlotControl.Plot.Add.Scatter(times.ToArray(), forces.ToArray());
            _livePlot.Color = ScottPlot.Color.FromHex("#4A9EFF");
            _livePlot.LineWidth = 2;
            _livePlot.MarkerSize = 0;
            PlotControl.Plot.Axes.AutoScale();
            PlotControl.Refresh();
        }
        catch { }
    }

    private void OnGraphCleared()
    {
        Dispatcher.InvokeAsync(() =>
        {
            try
            {
                PlotControl.Plot.Clear();
                _livePlot = null;
                _runEntries.Clear();
                _colorIndex = 0;
                PlotControl.Plot.Add.HorizontalLine(0, 1, ScottPlot.Color.FromHex("#555555"));
                PlotControl.Plot.Axes.AutoScale();
                PlotControl.Refresh();
            }
            catch { }
        });
    }

    private void OnLiveGraphCleared()
    {
        Dispatcher.InvokeAsync(() =>
        {
            try
            {
                // Only remove the live (in-progress) plot — keep completed run plots
                if (_livePlot != null)
                {
                    PlotControl.Plot.Remove(_livePlot);
                    _livePlot = null;
                }
                PlotControl.Plot.Axes.AutoScale();
                PlotControl.Refresh();
            }
            catch { }
        });
    }

    private void OnGraphRunCompleted()
    {
        Dispatcher.InvokeAsync(() =>
        {
            try
            {
                if (_livePlot == null) return;
                var color = RunColors[_colorIndex % RunColors.Length];

                // Capture raw data before LiveTimes/LiveForces get cleared
                double[] rawTimes = _vm.LiveTimes.ToArray();
                double[] rawForces = _vm.LiveForces.ToArray();

                // Build display data: apply post-run filter if enabled
                double[] displayTimes, displayForces;
                if (_vm.IsSpikeFilterEnabled)
                {
                    var (ft, ff, _) = SpikeFilter.Apply(rawTimes, rawForces, _vm.SpikeThreshold);
                    displayTimes = ft;
                    displayForces = ff;
                }
                else
                {
                    displayTimes = rawTimes;
                    displayForces = rawForces;
                }

                // Replace live plot with a proper completed-run scatter
                PlotControl.Plot.Remove(_livePlot);
                var scatter = PlotControl.Plot.Add.Scatter(displayTimes, displayForces);
                scatter.Color = color;
                scatter.LineWidth = 1.5f;
                scatter.MarkerSize = 0;

                // Find peak from display data
                ScottPlot.Plottables.Marker? peakMarker = null;
                if (displayForces.Length > 0)
                {
                    int peakIdx = 0; double peakVal = displayForces[0];
                    for (int i = 1; i < displayForces.Length; i++)
                        if (displayForces[i] > peakVal) { peakVal = displayForces[i]; peakIdx = i; }
                    if (peakIdx < displayTimes.Length)
                    {
                        peakMarker = PlotControl.Plot.Add.Marker(displayTimes[peakIdx], peakVal);
                        peakMarker.Color = color;
                        peakMarker.Size = 6;
                    }
                }

                int idx = _runEntries.Count;
                _runEntries.Add(new RunPlotEntry
                {
                    RunIndex = idx + 1,
                    Scatter = scatter,
                    PeakMarker = peakMarker,
                    OrigColor = color,
                    RawTimes = rawTimes,
                    RawForces = rawForces,
                });

                // Link to latest RunResultRow
                if (_vm.RunResults.Count > 0)
                {
                    var row = _vm.RunResults[^1];
                    row.PlotIndex = idx;
                    row.ColorHex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                    row.IsVisible = true;
                }

                _livePlot = null;
                _colorIndex++;
                PlotControl.Refresh();
            }
            catch { }
        });
    }

    private void OnSessionLoaded()
    {
        Dispatcher.InvokeAsync(() =>
        {
            try
            {
                PlotControl.Plot.Clear();
                _runEntries.Clear();
                _colorIndex = 0;
                PlotControl.Plot.Add.HorizontalLine(0, 1, ScottPlot.Color.FromHex("#555555"));

                // วนลูปนำข้อมูลที่โหลดมา วาดกลับไปบนกราฟ
                foreach (var (key, group) in _vm.AllData)
                {
                    for (int i = 0; i < group.Runs.Count; i++)
                    {
                        var run = group.Runs[i];
                        var color = RunColors[_colorIndex % RunColors.Length];

                        double[] rawTimes = run.Times.ToArray();
                        double[] rawForces = run.Forces.ToArray();

                        // เช็คว่าเปิด Spike Filter อยู่หรือไม่
                        double[] displayTimes, displayForces;
                        if (_vm.IsSpikeFilterEnabled)
                        {
                            var (ft, ff, _) = SpikeFilter.Apply(rawTimes, rawForces, _vm.SpikeThreshold);
                            displayTimes = ft;
                            displayForces = ff;
                        }
                        else
                        {
                            displayTimes = rawTimes;
                            displayForces = rawForces;
                        }

                        // วาดเส้นกราฟ
                        var scatter = PlotControl.Plot.Add.Scatter(displayTimes, displayForces);
                        scatter.Color = color;
                        scatter.LineWidth = 1.5f;
                        scatter.MarkerSize = 0;

                        // วาดจุด Peak
                        ScottPlot.Plottables.Marker? peakMarker = null;
                        if (displayForces.Length > 0)
                        {
                            int peakIdx = 0; double peakVal = displayForces[0];
                            for (int j = 1; j < displayForces.Length; j++)
                                if (displayForces[j] > peakVal) { peakVal = displayForces[j]; peakIdx = j; }
                            if (peakIdx < displayTimes.Length)
                            {
                                peakMarker = PlotControl.Plot.Add.Marker(displayTimes[peakIdx], peakVal);
                                peakMarker.Color = color;
                                peakMarker.Size = 6;
                            }
                        }

                        int idx = _runEntries.Count;
                        _runEntries.Add(new RunPlotEntry
                        {
                            RunIndex = idx + 1,
                            Scatter = scatter,
                            PeakMarker = peakMarker,
                            OrigColor = color,
                            RawTimes = rawTimes,
                            RawForces = rawForces,
                        });

                        // เชื่อมสีให้ตรงกับตาราง DataGrid
                        if (idx < _vm.RunResults.Count)
                        {
                            var row = _vm.RunResults[idx];
                            row.PlotIndex = idx;
                            row.ColorHex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                        }

                        _colorIndex++;
                    }
                }

                PlotControl.Plot.Axes.AutoScale();
                PlotControl.Refresh();
            }
            catch { }
        });
    }

    // ═══════════════════════════════════════════════════════
    // Spike Filter Toggle — rebuild all completed run plots
    // ═══════════════════════════════════════════════════════
    private void OnSpikeFilterToggled()
    {
        Dispatcher.InvokeAsync(() =>
        {
            try
            {
                bool filter = _vm.IsSpikeFilterEnabled;

                foreach (var entry in _runEntries)
                {
                    if (entry.RawForces.Length == 0) continue;

                    // Remove old scatter and peak marker
                    PlotControl.Plot.Remove(entry.Scatter);
                    if (entry.PeakMarker != null)
                        PlotControl.Plot.Remove(entry.PeakMarker);

                    // Rebuild with filtered or raw data (threshold + interpolation)
                    double[] displayTimes, displayForces;
                    if (filter)
                    {
                        var (ft, ff, _) = SpikeFilter.Apply(entry.RawTimes, entry.RawForces, _vm.SpikeThreshold);
                        displayTimes = ft;
                        displayForces = ff;
                    }
                    else
                    {
                        displayTimes = entry.RawTimes;
                        displayForces = entry.RawForces;
                    }

                    var scatter = PlotControl.Plot.Add.Scatter(displayTimes, displayForces);
                    scatter.Color = entry.OrigColor;
                    scatter.LineWidth = 1.5f;
                    scatter.MarkerSize = 0;
                    scatter.IsVisible = entry.IsVisible;
                    entry.Scatter = scatter;

                    // Rebuild peak marker from display data
                    if (displayForces.Length > 0)
                    {
                        int peakIdx = 0; double peakVal = displayForces[0];
                        for (int i = 1; i < displayForces.Length; i++)
                            if (displayForces[i] > peakVal) { peakVal = displayForces[i]; peakIdx = i; }

                        var marker = PlotControl.Plot.Add.Marker(displayTimes[peakIdx], peakVal);
                        marker.Color = entry.OrigColor;
                        marker.Size = 6;
                        marker.IsVisible = entry.IsVisible;
                        entry.PeakMarker = marker;
                    }
                }

                // Re-render live data too
                _needsRender = true;
                PlotControl.Plot.Axes.AutoScale();
                PlotControl.Refresh();
            }
            catch { }
        });
    }

    // ═══════════════════════════════════════════════════════
    // Run Results — Visibility Toggle (from DataGrid checkbox)
    // ═══════════════════════════════════════════════════════
    private void OnVisibilityToggle(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox cb || cb.Tag is not RunResultRow row) return;
        if (row.PlotIndex < 0 || row.PlotIndex >= _runEntries.Count) return;
        var entry = _runEntries[row.PlotIndex];
        bool vis = cb.IsChecked ?? true;
        entry.IsVisible = vis;
        entry.Scatter.IsVisible = vis;
        if (entry.PeakMarker != null) entry.PeakMarker.IsVisible = vis;
        row.IsVisible = vis;
        PlotControl.Plot.Axes.AutoScale();
        PlotControl.Refresh();
    }

    // ═══════════════════════════════════════════════════════
    // Run Results — Color Picker Popup (from DataGrid button)
    // ═══════════════════════════════════════════════════════
    private void OnColorSwatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not RunResultRow row) return;
        if (row.PlotIndex < 0 || row.PlotIndex >= _runEntries.Count) return;
        ShowColorPicker(btn, row);
    }

    private void ShowColorPicker(Button anchor, RunResultRow row)
    {
        var popup = new Popup
        {
            PlacementTarget = anchor,
            Placement = PlacementMode.Left,
            StaysOpen = false,
            AllowsTransparency = true,
        };

        var panel = new StackPanel { Width = 234 };
        var border = new Border
        {
            Background = new SolidColorBrush(C(42, 42, 62)),
            BorderBrush = new SolidColorBrush(C(85, 85, 119)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
            Child = panel,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            { Color = System.Windows.Media.Colors.Black, Opacity = 0.5, BlurRadius = 10, ShadowDepth = 3 }
        };

        AddSection(panel, "Standard Colors", new[] {
            "#FF4444","#FF6B6B","#FF9944","#FFD93D","#44FF88",
            "#4A9EFF","#88DDFF","#B088FF","#FF88C0","#AAAAAA",
            "#E53E3E","#DD6B20","#D69E2E","#38A169","#3182CE",
            "#805AD5","#D53F8C","#718096","#2B6CB0","#C05621",
        }, row, anchor, popup);

        AddSection(panel, "Pastel Colors", new[] {
            "#FFB3BA","#FFDFBA","#FFFFBA","#BAFFC9","#BAE1FF",
            "#E8BAFF","#FFB3E6","#C9BAFF","#B3FFD9","#FFE4B3",
            "#D4A5FF","#A5D4FF","#A5FFD4","#FFA5D4","#FFD4A5",
            "#B3D9FF","#FFB3D9","#D9FFB3","#B3FFE6","#E6B3FF",
        }, row, anchor, popup);

        AddSection(panel, "High Contrast", new[] {
            "#FF0000","#00FF00","#0000FF","#FFFF00","#FF00FF",
            "#00FFFF","#FF8000","#8000FF","#00FF80","#FFFFFF",
        }, row, anchor, popup);

        popup.Child = border;
        popup.IsOpen = true;
    }

    private void AddSection(StackPanel parent, string title, string[] hexColors,
        RunResultRow row, Button anchor, Popup popup)
    {
        parent.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(C(160, 160, 180)),
            Margin = new Thickness(0, parent.Children.Count > 0 ? 6 : 0, 0, 4)
        });
        var wrap = new WrapPanel();
        foreach (var hex in hexColors)
        {
            var mc = (System.Windows.Media.Color)ColorConverter.ConvertFromString(hex);
            var swatch = new Button
            {
                Width = 20,
                Height = 20,
                Margin = new Thickness(1),
                Background = new SolidColorBrush(mc),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(C(80, 80, 100)),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = hex,
            };
            var h = hex;
            swatch.Click += (_, _) => { ApplyColor(row, h, anchor); popup.IsOpen = false; };
            wrap.Children.Add(swatch);
        }
        parent.Children.Add(wrap);
    }

    private void ApplyColor(RunResultRow row, string hex, Button anchor)
    {
        if (row.PlotIndex < 0 || row.PlotIndex >= _runEntries.Count) return;
        var entry = _runEntries[row.PlotIndex];
        var sc = ScottPlot.Color.FromHex(hex);
        entry.OrigColor = sc;
        entry.Scatter.Color = sc;
        if (entry.PeakMarker != null) entry.PeakMarker.Color = sc;
        row.ColorHex = hex;
        anchor.Background = new SolidColorBrush(
            (System.Windows.Media.Color)ColorConverter.ConvertFromString(hex));
        PlotControl.Refresh();
    }

    private static System.Windows.Media.Color C(byte r, byte g, byte b) =>
        System.Windows.Media.Color.FromRgb(r, g, b);

    // ═══════════════════════════════════════════════════════
    // PDF Graph Capture
    // ═══════════════════════════════════════════════════════
    private byte[]? CaptureGraph()
    {
        try
        {
            var origFigBg = PlotControl.Plot.FigureBackground.Color;
            var origDataBg = PlotControl.Plot.DataBackground.Color;
            var origTitle = PlotControl.Plot.Axes.Title.Label.ForeColor;
            var origXLbl = PlotControl.Plot.Axes.Bottom.Label.ForeColor;
            var origYLbl = PlotControl.Plot.Axes.Left.Label.ForeColor;
            var origXTick = PlotControl.Plot.Axes.Bottom.TickLabelStyle.ForeColor;
            var origYTick = PlotControl.Plot.Axes.Left.TickLabelStyle.ForeColor;
            var origGrid = PlotControl.Plot.Grid.MajorLineColor;

            PlotControl.Plot.FigureBackground.Color = ScottPlot.Color.FromHex("#FFFFFF");
            PlotControl.Plot.DataBackground.Color = ScottPlot.Color.FromHex("#FFFFFF");
            PlotControl.Plot.Axes.Title.Label.ForeColor = ScottPlot.Color.FromHex("#1A1A2E");
            PlotControl.Plot.Axes.Bottom.Label.ForeColor = ScottPlot.Color.FromHex("#333333");
            PlotControl.Plot.Axes.Left.Label.ForeColor = ScottPlot.Color.FromHex("#333333");
            PlotControl.Plot.Axes.Bottom.TickLabelStyle.ForeColor = ScottPlot.Color.FromHex("#333333");
            PlotControl.Plot.Axes.Left.TickLabelStyle.ForeColor = ScottPlot.Color.FromHex("#333333");
            PlotControl.Plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#E0E0E0");

            var origW = new Dictionary<ScottPlot.Plottables.Scatter, float>();
            foreach (var en in _runEntries) { origW[en.Scatter] = en.Scatter.LineWidth; en.Scatter.LineWidth = 3f; }
            if (_livePlot != null) { origW[_livePlot] = _livePlot.LineWidth; _livePlot.LineWidth = 3f; }

            var img = PlotControl.Plot.GetImage(2400, 1200);
            byte[] bytes = img.GetImageBytes(ScottPlot.ImageFormat.Png);

            PlotControl.Plot.FigureBackground.Color = origFigBg;
            PlotControl.Plot.DataBackground.Color = origDataBg;
            PlotControl.Plot.Axes.Title.Label.ForeColor = origTitle;
            PlotControl.Plot.Axes.Bottom.Label.ForeColor = origXLbl;
            PlotControl.Plot.Axes.Left.Label.ForeColor = origYLbl;
            PlotControl.Plot.Axes.Bottom.TickLabelStyle.ForeColor = origXTick;
            PlotControl.Plot.Axes.Left.TickLabelStyle.ForeColor = origYTick;
            PlotControl.Plot.Grid.MajorLineColor = origGrid;
            foreach (var (p, w) in origW) p.LineWidth = w;
            PlotControl.Refresh();
            return bytes;
        }
        catch { return null; }
    }

    // ═══════════════════════════════════════════════════════
    // Graph Navigation — Home / Undo / Redo
    // ═══════════════════════════════════════════════════════
    private void SaveViewState()
    {
        if (_suppressSave) return;
        var a = PlotControl.Plot.Axes;
        var s = new AxisState(a.Bottom.Min, a.Bottom.Max, a.Left.Min, a.Left.Max);
        if (_viewIndex < _viewHistory.Count - 1)
            _viewHistory.RemoveRange(_viewIndex + 1, _viewHistory.Count - _viewIndex - 1);
        if (_viewHistory.Count > 0)
        {
            var l = _viewHistory[^1];
            if (Math.Abs(l.XMin - s.XMin) < 1e-10 && Math.Abs(l.XMax - s.XMax) < 1e-10 &&
                Math.Abs(l.YMin - s.YMin) < 1e-10 && Math.Abs(l.YMax - s.YMax) < 1e-10) return;
        }
        _viewHistory.Add(s);
        if (_viewHistory.Count > 50) _viewHistory.RemoveAt(0);
        _viewIndex = _viewHistory.Count - 1;
    }

    private void ApplyViewState(AxisState s)
    {
        _suppressSave = true;
        PlotControl.Plot.Axes.SetLimits(s.XMin, s.XMax, s.YMin, s.YMax);
        PlotControl.Refresh();
        _suppressSave = false;
    }

    // ═══════════════════════════════════════════════════════
    // Menu — Measurement Setup Dialog
    // ═══════════════════════════════════════════════════════
    private void OnMeasurementSetupClick(object sender, RoutedEventArgs e)
    {
        var dialog = new MeasurementSetupWindow
        {
            Owner = this,
            DataContext = _vm
        };
        dialog.ShowDialog();
    }

    // ═══════════════════════════════════════════════════════
    // Menu — Tools & Help
    // ═══════════════════════════════════════════════════════
    private void OnCalibrationWizardClick(object sender, RoutedEventArgs e)
    {
        // เปิดหน้าต่าง Calibration Wizard
        var dialog = new CalibrationWizardWindow(_vm)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        // แสดงกล่องข้อความ About
        MessageBox.Show("Surface Tension Tester v7.3\n\nDesigned and Developed by Soranan Suebsilpasakul\nSchool of Integrated Innovative Technology (SIITec)\nKing Mongkut's Institute of Technology Ladkrabang (KMITL)",
            "About Surface Tension Tester",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void OnGraphHome(object sender, RoutedEventArgs e)
    { SaveViewState(); PlotControl.Plot.Axes.AutoScale(); PlotControl.Refresh(); SaveViewState(); }

    private void OnGraphUndo(object sender, RoutedEventArgs e)
    {
        if (_viewIndex <= 0) return;
        if (_viewIndex == _viewHistory.Count - 1) SaveViewState();
        _viewIndex--;
        ApplyViewState(_viewHistory[_viewIndex]);
    }

    private void OnGraphRedo(object sender, RoutedEventArgs e)
    {
        if (_viewIndex >= _viewHistory.Count - 1) return;
        _viewIndex++;
        ApplyViewState(_viewHistory[_viewIndex]);
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {

    }

    private void OnPlotInteracted(object? sender, EventArgs e) => SaveViewState();
}