using CommunityToolkit.Mvvm.ComponentModel;

namespace SurfaceTensionApp.Models;

/// <summary>
/// Measurement parameters for surface tension calculation.
/// All numeric fields are nullable — null means "not specified" and won't appear in PDF.
/// </summary>
public partial class MeasurementConfig : ObservableObject
{
    // ── Method Selection ──
    [ObservableProperty] private string _method = "Du Noüy Ring";
    [ObservableProperty] private string _loadCellType = "100g";

    // ── Du Noüy Ring Parameters (all optional) ──
    [ObservableProperty] private double? _ringRadius;
    [ObservableProperty] private double? _wireRadius;
    [ObservableProperty] private string _ringMaterial = "";

    // ── Wilhelmy Plate Parameters (all optional) ──
    [ObservableProperty] private double? _plateWidth;
    [ObservableProperty] private double? _plateThickness;
    [ObservableProperty] private string _plateMaterial = "";

    // ── Correction Factor (optional — defaults to 1.0 if not specified) ──
    [ObservableProperty] private double? _correctionFactor;

    // ── Sample Information (all optional) ──
    [ObservableProperty] private string _liquidName = "";
    [ObservableProperty] private double? _temperature;
    [ObservableProperty] private string _operatorName = "";
    [ObservableProperty] private string _sampleId = "";
    [ObservableProperty] private string _notes = "";

    // ── Display Unit ──
    [ObservableProperty] private string _unit = "mN/m";

    /// <summary>
    /// Whether enough parameters are specified to calculate surface tension.
    /// </summary>
    public bool CanCalculate => Method == "Wilhelmy Plate"
        ? PlateWidth.HasValue && PlateThickness.HasValue
        : RingRadius.HasValue;

    /// <summary>
    /// Calculate surface tension from peak force (N).
    /// Returns null if parameters are insufficient.
    /// </summary>
    public double? CalculateSurfaceTension(double peakForceN)
    {
        if (!CanCalculate) return null;

        double gammaNPerM;
        double cf = CorrectionFactor ?? 1.0;

        if (Method == "Wilhelmy Plate")
        {
            double w = PlateWidth!.Value;
            double t = PlateThickness!.Value;
            double wettedPerimeterM = 2 * (w + t) / 1000.0;
            if (wettedPerimeterM < 1e-9) return null;
            gammaNPerM = peakForceN / wettedPerimeterM;
        }
        else
        {
            double ringRadiusM = RingRadius!.Value / 1000.0;
            if (ringRadiusM < 1e-9) return null;
            gammaNPerM = (peakForceN / (4 * Math.PI * ringRadiusM)) * cf;
        }

        return gammaNPerM * 1000.0;
    }

    /// <summary>Wetted length if calculable (mm).</summary>
    public double? WettedLengthMm => Method == "Wilhelmy Plate"
        ? (PlateWidth.HasValue && PlateThickness.HasValue ? 2 * (PlateWidth.Value + PlateThickness.Value) : null)
        : (RingRadius.HasValue ? 4 * Math.PI * RingRadius.Value : null);

    /// <summary>Formula description for reports.</summary>
    public string FormulaDescription => Method == "Wilhelmy Plate"
        ? "γ = F / L,  L = 2(W + T)"
        : "γ = (F / 4πR) × f,  R = ring mean radius, f = correction factor";
}
