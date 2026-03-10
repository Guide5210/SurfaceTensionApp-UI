using Xunit;
using SurfaceTensionApp.Models;

namespace SurfaceTensionApp.Tests;

/// <summary>
/// Unit tests for surface tension formula implementations.
/// Errors here produce wrong published scientific values — test everything.
/// </summary>
public class SurfaceTensionCalcTests
{
    // ══════════════════════════════════════════════════════
    // Du Noüy Ring method
    // ══════════════════════════════════════════════════════

    [Fact]
    public void DuNouyRing_KnownWater_ReturnsApprox72mNm()
    {
        // Water at 20 °C: γ ≈ 72.75 mN/m
        // Using a 9.55 mm mean radius ring (standard ASTM D971 ring)
        // F = γ × 4π × R  →  F = 0.07275 × 4π × 0.00955 ≈ 8.745 × 10⁻³ N
        var config = new MeasurementConfig
        {
            Method    = "Du Noüy Ring",
            RingRadius = 9.55,           // mm
            CorrectionFactor = 1.0,      // no Harkins-Jordan correction
        };

        double peakForce = 0.07275 * 4 * Math.PI * 0.00955; // N
        double? gamma    = config.CalculateSurfaceTension(peakForce);

        Assert.NotNull(gamma);
        Assert.Equal(72.75, gamma!.Value, precision: 1);
    }

    [Fact]
    public void DuNouyRing_WithCorrectionFactor_ScalesResult()
    {
        var config = new MeasurementConfig
        {
            Method = "Du Noüy Ring",
            RingRadius = 10.0,
            CorrectionFactor = 0.9,
        };
        var configNoCf = new MeasurementConfig
        {
            Method = "Du Noüy Ring",
            RingRadius = 10.0,
            CorrectionFactor = 1.0,
        };

        double force = 0.05;
        double? gammaWithCf   = config.CalculateSurfaceTension(force);
        double? gammaWithoutCf = configNoCf.CalculateSurfaceTension(force);

        Assert.NotNull(gammaWithCf);
        Assert.NotNull(gammaWithoutCf);
        Assert.Equal(gammaWithoutCf!.Value * 0.9, gammaWithCf!.Value, precision: 10);
    }

    [Fact]
    public void DuNouyRing_ZeroRadius_ReturnsNull()
    {
        var config = new MeasurementConfig
        {
            Method     = "Du Noüy Ring",
            RingRadius = 0.0,
        };
        Assert.Null(config.CalculateSurfaceTension(0.05));
    }

    [Fact]
    public void DuNouyRing_MissingRadius_CannotCalculate()
    {
        var config = new MeasurementConfig { Method = "Du Noüy Ring" };
        Assert.False(config.CanCalculate);
        Assert.Null(config.CalculateSurfaceTension(0.05));
    }

    // ══════════════════════════════════════════════════════
    // Wilhelmy Plate method
    // ══════════════════════════════════════════════════════

    [Fact]
    public void WilhelmyPlate_KnownWater_ReturnsApprox72mNm()
    {
        // Standard platinum plate: 19.6 mm wide, 0.1 mm thick
        // F = γ × 2(W+T)  →  F = 0.07275 × 2 × (0.0196 + 0.0001) ≈ 2.867 × 10⁻³ N
        var config = new MeasurementConfig
        {
            Method          = "Wilhelmy Plate",
            PlateWidth      = 19.6,   // mm
            PlateThickness  = 0.1,    // mm
        };

        double peakForce = 0.07275 * 2 * (0.0196 + 0.0001); // N
        double? gamma    = config.CalculateSurfaceTension(peakForce);

        Assert.NotNull(gamma);
        Assert.Equal(72.75, gamma!.Value, precision: 1);
    }

    [Fact]
    public void WilhelmyPlate_MissingWidth_CannotCalculate()
    {
        var config = new MeasurementConfig
        {
            Method         = "Wilhelmy Plate",
            PlateThickness = 0.1,
        };
        Assert.False(config.CanCalculate);
        Assert.Null(config.CalculateSurfaceTension(0.01));
    }

    [Fact]
    public void WilhelmyPlate_MissingThickness_CannotCalculate()
    {
        var config = new MeasurementConfig
        {
            Method     = "Wilhelmy Plate",
            PlateWidth = 19.6,
        };
        Assert.False(config.CanCalculate);
    }

    [Fact]
    public void WilhelmyPlate_ZeroWettedPerimeter_ReturnsNull()
    {
        var config = new MeasurementConfig
        {
            Method         = "Wilhelmy Plate",
            PlateWidth     = 0.0,
            PlateThickness = 0.0,
        };
        Assert.Null(config.CalculateSurfaceTension(0.01));
    }

    // ══════════════════════════════════════════════════════
    // Unit conversion (mN/m output)
    // ══════════════════════════════════════════════════════

    [Fact]
    public void CalculateSurfaceTension_OutputUnit_IsMilliNewtonPerMeter()
    {
        // The formula must multiply N/m by 1000 to give mN/m
        var config = new MeasurementConfig
        {
            Method    = "Wilhelmy Plate",
            PlateWidth     = 10.0,   // mm → 0.010 m
            PlateThickness = 0.0,    // simplify: perimeter = 2 × W = 0.020 m
        };
        // Force = γ(N/m) × perimeter → if γ = 1 N/m and perimeter = 0.020 m → F = 0.020 N
        double? gamma = config.CalculateSurfaceTension(0.020);
        Assert.NotNull(gamma);
        // Expected: 1 N/m = 1000 mN/m
        Assert.Equal(1000.0, gamma!.Value, precision: 6);
    }

    // ══════════════════════════════════════════════════════
    // WettedLengthMm
    // ══════════════════════════════════════════════════════

    [Fact]
    public void WettedLengthMm_Ring_Returns4PiR()
    {
        var config = new MeasurementConfig
        {
            Method     = "Du Noüy Ring",
            RingRadius = 10.0,
        };
        double? wl = config.WettedLengthMm;
        Assert.NotNull(wl);
        Assert.Equal(4 * Math.PI * 10.0, wl!.Value, precision: 6);
    }

    [Fact]
    public void WettedLengthMm_Plate_Returns2WPlusT()
    {
        var config = new MeasurementConfig
        {
            Method         = "Wilhelmy Plate",
            PlateWidth     = 20.0,
            PlateThickness = 0.5,
        };
        double? wl = config.WettedLengthMm;
        Assert.NotNull(wl);
        Assert.Equal(2 * (20.0 + 0.5), wl!.Value, precision: 6);
    }
}
