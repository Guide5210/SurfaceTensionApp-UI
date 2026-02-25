using CommunityToolkit.Mvvm.ComponentModel;

namespace SurfaceTensionApp.Models;

/// <summary>
/// Measurement parameters for surface tension calculation.
/// </summary>
public partial class MeasurementConfig : ObservableObject
{
    // ── Method Selection ──
    [ObservableProperty] private string _method = "Du Noüy Ring";  // "Du Noüy Ring" or "Wilhelmy Plate"
    [ObservableProperty] private string _loadCellType = "100g";     // "100g" or "30g"

    // ── Du Noüy Ring Parameters ──
    [ObservableProperty] private double _ringRadius = 9.55;        // mm (mean radius)
    [ObservableProperty] private double _wireRadius = 0.185;       // mm (wire radius)
    [ObservableProperty] private string _ringMaterial = "Platinum-Iridium";

    // ── Wilhelmy Plate Parameters ──
    [ObservableProperty] private double _plateWidth = 19.62;       // mm
    [ObservableProperty] private double _plateThickness = 0.1;     // mm
    [ObservableProperty] private string _plateMaterial = "Platinum";

    // ── Correction Factor ──
    [ObservableProperty] private double _correctionFactor = 1.0;   // Harkins-Jordan or custom

    // ── Sample Information ──
    [ObservableProperty] private string _liquidName = "";
    [ObservableProperty] private double _temperature = 25.0;       // °C
    [ObservableProperty] private string _operatorName = "";
    [ObservableProperty] private string _sampleId = "";
    [ObservableProperty] private string _notes = "";

    // ── Display Unit ──
    [ObservableProperty] private string _unit = "mN/m";            // "mN/m" or "dyn/cm"

    /// <summary>
    /// Calculate surface tension from peak force (N) using the configured method and parameters.
    /// </summary>
    public double CalculateSurfaceTension(double peakForceN)
    {
        double gammaNPerM;

        if (Method == "Wilhelmy Plate")
        {
            // γ = F / L   where L = wetted perimeter = 2(W + T)
            double wettedPerimeterM = 2 * (PlateWidth + PlateThickness) / 1000.0;
            if (wettedPerimeterM < 1e-9) return 0;
            gammaNPerM = peakForceN / wettedPerimeterM;
        }
        else // Du Noüy Ring
        {
            // γ = F / (4πR) × correction_factor
            double ringRadiusM = RingRadius / 1000.0;
            if (ringRadiusM < 1e-9) return 0;
            gammaNPerM = (peakForceN / (4 * Math.PI * ringRadiusM)) * CorrectionFactor;
        }

        // N/m → mN/m (same numeric value as dyn/cm)
        return gammaNPerM * 1000.0;
    }

    /// <summary>Wetted length used in calculation (mm).</summary>
    public double WettedLengthMm => Method == "Wilhelmy Plate"
        ? 2 * (PlateWidth + PlateThickness)
        : 4 * Math.PI * RingRadius;

    /// <summary>Formula description for reports.</summary>
    public string FormulaDescription => Method == "Wilhelmy Plate"
        ? "γ = F / L,  L = 2(W + T)"
        : "γ = (F / 4πR) × f,  R = ring mean radius, f = correction factor";
}
