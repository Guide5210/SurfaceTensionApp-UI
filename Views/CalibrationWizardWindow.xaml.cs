using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using SurfaceTensionApp.ViewModels;

namespace SurfaceTensionApp.Views;

public partial class CalibrationWizardWindow : Window
{
    private readonly MainViewModel _vm;
    private int _currentStep = 1;
    private const int TotalSteps = 5;
    private readonly DispatcherTimer _forceTimer;
    private bool _monitorStarted;

    public CalibrationWizardWindow()
    {
        InitializeComponent();
    }

    public CalibrationWizardWindow(MainViewModel vm) : this()
    {
        _vm = vm;

        // Update connection warning
        ConnectionWarning.Visibility = _vm.IsConnected ? Visibility.Collapsed : Visibility.Visible;

        // Subscribe to live force updates
        _vm.PropertyChanged += OnVmPropertyChanged;

        // Timer to poll LiveForce for display
        _forceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _forceTimer.Tick += (_, _) => UpdateLiveForce();
        _forceTimer.Start();

        Closed += (_, _) =>
        {
            _forceTimer.Stop();
            _vm.PropertyChanged -= OnVmPropertyChanged;
        };

        ShowStep(1);
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsConnected))
        {
            Dispatcher.Invoke(() =>
            {
                ConnectionWarning.Visibility = _vm.IsConnected ? Visibility.Collapsed : Visibility.Visible;
            });
        }
    }

    private void UpdateLiveForce()
    {
        string force = _vm.LiveForce;
        switch (_currentStep)
        {
            case 2: LiveForceStep2.Text = force; break;
            case 3: LiveForceStep3.Text = force; break;
            case 4: LiveForceStep4.Text = force; break;
            case 5: LiveForceStep5.Text = force; break;
        }
    }

    // ═══════════════════════════════════════════════
    // Step Navigation
    // ═══════════════════════════════════════════════
    private void ShowStep(int step)
    {
        _currentStep = step;

        PanelStep1.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        PanelStep2.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        PanelStep3.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        PanelStep4.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;
        PanelStep5.Visibility = step == 5 ? Visibility.Visible : Visibility.Collapsed;

        // Update step indicator dots
        UpdateStepDot(Step1Dot, step >= 1, step == 1);
        UpdateStepDot(Step2Dot, step >= 2, step == 2);
        UpdateStepDot(Step3Dot, step >= 3, step == 3);
        UpdateStepDot(Step4Dot, step >= 4, step == 4);
        UpdateStepDot(Step5Dot, step >= 5, step == 5);

        // Update step label
        StepLabel.Text = step switch
        {
            1 => "Preparation",
            2 => "Tare (Zero)",
            3 => "Place Weight",
            4 => "Calibrate",
            5 => "Verify",
            _ => ""
        };

        // Navigation buttons
        BtnBack.IsEnabled = step > 1;
        BtnNext.Content = step == TotalSteps ? "Close" : "Next";

        // On entering step 3 → update expected force text
        if (step == 3)
        {
            UpdateExpectedForce();
        }

        // On entering step 4 → populate summary and send 'k' to start calibration mode
        if (step == 4)
        {
            if (double.TryParse(KnownWeightBox.Text, out double w))
                CalWeightSummary.Text = $"Known weight: {w:F2} g";
            CalLoadCell.Text = Radio100g.IsChecked == true ? "Load cell: 100g" : "Load cell: 30g";

            // Send 'k' to enter calibration mode on Arduino
            if (_vm.IsConnected)
                _vm.CalibrateCommand.Execute(null);
        }

        // Start monitor mode ONCE (not on every step change — resending 'm'
        // can kick the Arduino out of calibration mode on the TFT)
        if (step >= 2 && step <= 5 && _vm.IsConnected && !_monitorStarted)
        {
            _vm.MonitorCommand.Execute(null);
            _monitorStarted = true;
        }
    }

    private static void UpdateStepDot(System.Windows.Controls.TextBlock dot, bool reached, bool active)
    {
        if (active)
        {
            dot.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4A9EFF"));
            dot.Foreground = Brushes.White;
        }
        else if (reached)
        {
            dot.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A5A8F"));
            dot.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B0B0C8"));
        }
        else
        {
            dot.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2A3E"));
            dot.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#808098"));
        }
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (_currentStep > 1) ShowStep(_currentStep - 1);
    }

    private void OnNextClick(object sender, RoutedEventArgs e)
    {
        if (_currentStep == TotalSteps)
        {
            Close();
            return;
        }

        // Validate before moving forward
        if (_currentStep == 1 && !_vm.IsConnected)
        {
            MessageBox.Show("Arduino is not connected.\nPlease connect before proceeding.",
                "Connection Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ShowStep(_currentStep + 1);
    }

    // ═══════════════════════════════════════════════
    // Tare
    // ═══════════════════════════════════════════════
    private void OnTareClick(object sender, RoutedEventArgs e)
    {
        if (!_vm.IsConnected)
        {
            TareStatus.Text = "Not connected!";
            TareStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6666"));
            return;
        }

        _vm.TareCommand.Execute(null);
        TareStatus.Text = "Tare command sent. Wait for reading to stabilize near 0.";
        TareStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#44FF88"));
    }

    // ═══════════════════════════════════════════════
    // Calibrate
    // ═══════════════════════════════════════════════
    private void OnCalibrateClick(object sender, RoutedEventArgs e)
    {
        if (!_vm.IsConnected)
        {
            CalStatus.Text = "Not connected!";
            CalStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6666"));
            return;
        }

        if (!double.TryParse(KnownWeightBox.Text, out double weight) || weight <= 0)
        {
            CalStatus.Text = "Invalid weight value!";
            CalStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6666"));
            return;
        }

        // Send the weight value to Arduino (calibration mode already started on step entry)
        _vm.SendCalibrationWeightCommand.Execute(KnownWeightBox.Text);
        CalStatus.Text = $"Calibration weight sent: {weight:F2} g";
        CalStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#44FF88"));

        // Update expected force for verification step
        UpdateExpectedForce();
    }

    private void UpdateExpectedForce()
    {
        if (double.TryParse(KnownWeightBox.Text, out double grams) && grams > 0)
        {
            double expectedN = grams * 9.81 / 1000.0;
            ExpectedForce.Text = $"Expected: ~{expectedN:F5} N for {grams:F2} g";
        }
    }
}