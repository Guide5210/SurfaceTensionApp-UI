using System.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SurfaceTensionApp.Models;

namespace SurfaceTensionApp.Services;

public static class PdfReportService
{
    static PdfReportService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static string GenerateReport(
        Dictionary<string, SpeedGroup> allData,
        string outputDir,
        byte[]? graphImage = null,
        string? title = null,
        string? notes = null,
        MeasurementConfig? config = null,
        bool spikeFilterEnabled = false,
        double? spikeThreshold = null)
    {
        Directory.CreateDirectory(outputDir);
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string path = Path.Combine(outputDir, $"SurfaceTension_Report_{timestamp}.pdf");

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10));
                page.Header().Element(c => ComposeHeader(c, title, config));
                page.Content().Element(c => ComposeContent(c, allData, graphImage, notes, config, spikeFilterEnabled, spikeThreshold));
                page.Footer().Element(ComposeFooter);
            });
        }).GeneratePdf(path);

        return path;
    }

    private static void ComposeHeader(IContainer container, string? title, MeasurementConfig? config)
    {
        string method = config?.Method ?? "Du Noüy Ring";
        container.Column(col =>
        {
            col.Item().BorderBottom(2).BorderColor("#4A9EFF").PaddingBottom(8).Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text(title ?? "Surface Tension Measurement Report")
                        .FontSize(18).Bold().FontColor("#1A1A2E");
                    c.Item().Text($"{method} Method - Automated Analysis")
                        .FontSize(10).FontColor("#666666");
                    c.Item().Text($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
                        .FontSize(8).FontColor("#999999");
                });
            });
        });
    }

    private static void ComposeContent(IContainer container, Dictionary<string, SpeedGroup> allData,
        byte[]? graphImage, string? notes, MeasurementConfig? config, bool spikeFilterEnabled,
        double? spikeThreshold)
    {
        container.PaddingVertical(10).Column(col =>
        {
            int sec = 1;

            // ── Measurement Parameters (only show fields the user filled in) ──
            if (config != null)
            {
                var rows = new List<string[]>();
                rows.Add(new[] { "Method", config.Method });
                rows.Add(new[] { "Load Cell", config.LoadCellType });

                if (config.Method == "Wilhelmy Plate")
                {
                    if (config.PlateWidth.HasValue)
                        rows.Add(new[] { "Plate Width", $"{config.PlateWidth.Value} mm" });
                    if (config.PlateThickness.HasValue)
                        rows.Add(new[] { "Plate Thickness", $"{config.PlateThickness.Value} mm" });
                    if (!string.IsNullOrWhiteSpace(config.PlateMaterial))
                        rows.Add(new[] { "Plate Material", config.PlateMaterial });
                    if (config.WettedLengthMm.HasValue)
                        rows.Add(new[] { "Wetted Perimeter", $"{config.WettedLengthMm.Value:F3} mm" });
                }
                else
                {
                    if (config.RingRadius.HasValue)
                        rows.Add(new[] { "Ring Mean Radius (R)", $"{config.RingRadius.Value} mm" });
                    if (config.WireRadius.HasValue)
                        rows.Add(new[] { "Wire Radius (r)", $"{config.WireRadius.Value} mm" });
                    if (!string.IsNullOrWhiteSpace(config.RingMaterial))
                        rows.Add(new[] { "Ring Material", config.RingMaterial });
                    if (config.WettedLengthMm.HasValue)
                        rows.Add(new[] { "Wetted Length (4piR)", $"{config.WettedLengthMm.Value:F3} mm" });
                }

                if (config.TravelDistanceMm.HasValue)
                    rows.Add(new[] { "Travel Distance (Home→Target)", $"{config.TravelDistanceMm.Value:F3} mm" });
                if (config.CorrectionFactor.HasValue)
                    rows.Add(new[] { "Correction Factor", $"{config.CorrectionFactor.Value}" });
                if (config.CanCalculate)
                    rows.Add(new[] { "Formula", config.FormulaDescription });
                rows.Add(new[] { "Display Unit", config.Unit });
                if (!string.IsNullOrWhiteSpace(config.LiquidName))
                    rows.Add(new[] { "Liquid / Sample", config.LiquidName });
                if (config.Temperature.HasValue)
                    rows.Add(new[] { "Temperature", $"{config.Temperature.Value} °C" });
                if (!string.IsNullOrWhiteSpace(config.SampleId))
                    rows.Add(new[] { "Sample ID", config.SampleId });
                if (!string.IsNullOrWhiteSpace(config.OperatorName))
                    rows.Add(new[] { "Operator", config.OperatorName });

                // Only show section if there's something beyond just method + load cell
                if (rows.Count > 2)
                {
                    col.Item().Text($"{sec}. Measurement Parameters").FontSize(14).Bold().FontColor("#1A1A2E");
                    col.Item().PaddingVertical(4);

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3f);
                            columns.RelativeColumn(3f);
                        });

                        var labelStyle = TextStyle.Default.FontSize(9).Bold();
                        var valStyle = TextStyle.Default.FontSize(9);

                        bool alt = false;
                        foreach (var r in rows)
                        {
                            var bg = alt ? "#F8F9FA" : "#FFFFFF";
                            alt = !alt;
                            table.Cell().Background(bg).Padding(4).Text(r[0]).Style(labelStyle);
                            table.Cell().Background(bg).Padding(4).Text(r[1]).Style(valStyle);
                        }
                    });
                    col.Item().PaddingVertical(8);
                    sec++;
                }
            }

            // ── Notes ──
            string combinedNotes = "";
            if (!string.IsNullOrWhiteSpace(notes)) combinedNotes = notes;
            if (config != null && !string.IsNullOrWhiteSpace(config.Notes))
                combinedNotes = string.IsNullOrEmpty(combinedNotes) ? config.Notes : combinedNotes + "\n" + config.Notes;

            if (!string.IsNullOrWhiteSpace(combinedNotes))
            {
                col.Item().Background("#F0F4FF").Padding(10).Column(nc =>
                {
                    nc.Item().Text("Notes").Bold().FontSize(10);
                    nc.Item().Text(combinedNotes).FontSize(9);
                });
                col.Item().PaddingVertical(8);
            }

            // ── Graph ──
            if (graphImage != null && graphImage.Length > 0)
            {
                col.Item().Text($"{sec}. Force vs Time Graph").FontSize(14).Bold().FontColor("#1A1A2E");
                col.Item().PaddingVertical(4);
                col.Item().Border(1).BorderColor("#CCCCCC").Image(graphImage).FitWidth();
                col.Item().PaddingVertical(8);
                sec++;
            }

            // ── Summary ──
            int totalRuns = allData.Values.Sum(g => g.PeakForces.Count);
            int totalOutliers = allData.Values.Sum(g => g.OutlierIndices.Count);
            int totalClean = allData.Values.Sum(g => g.CleanPeaks.Count);
            int speedCount = allData.Count;

            var allCleanPeaks = allData.Values.SelectMany(g => g.CleanPeaks).ToList();
            double globalMean = allCleanPeaks.Count > 0 ? allCleanPeaks.Average() : 0;
            double globalStd = allCleanPeaks.Count > 1 ? Math.Sqrt(allCleanPeaks.Sum(v => (v - globalMean) * (v - globalMean)) / allCleanPeaks.Count) : 0;
            double globalMin = allCleanPeaks.Count > 0 ? allCleanPeaks.Min() : 0;
            double globalMax = allCleanPeaks.Count > 0 ? allCleanPeaks.Max() : 0;
            double globalRange = globalMax - globalMin;
            double globalRsd = globalMean != 0 ? globalStd / globalMean * 100 : 0;

            double tValue = allCleanPeaks.Count >= 30 ? 1.96 : 2.0;
            double se = allCleanPeaks.Count > 1 ? globalStd / Math.Sqrt(allCleanPeaks.Count) : 0;
            double ciLower = globalMean - tValue * se;
            double ciUpper = globalMean + tValue * se;

            col.Item().Text($"{sec}. Summary").FontSize(14).Bold().FontColor("#1A1A2E");
            col.Item().PaddingVertical(4);

            col.Item().Row(row =>
            {
                row.RelativeItem().Background("#F8F9FA").Padding(8).Column(c =>
                {
                    c.Item().Text($"Total runs: {totalRuns}").FontSize(10);
                    c.Item().Text($"Valid runs (after outlier rejection): {totalClean}").FontSize(10);
                    c.Item().Text($"Outliers rejected: {totalOutliers}").FontSize(10);
                    c.Item().Text($"Speed profiles tested: {speedCount}").FontSize(10);
                    c.Item().Text("Outlier method: Modified Z-score (MAD, threshold=3.5)").FontSize(9).FontColor("#666666");
                    if (spikeFilterEnabled)
                    {
                        string thInfo = spikeThreshold.HasValue ? $"threshold={spikeThreshold.Value} N" : "auto-detect";
                        c.Item().Text($"Spike filter: Enabled ({thInfo}, linear interpolation)").FontSize(9).FontColor("#666666");
                    }
                });
            });
            col.Item().PaddingVertical(8);
            sec++;

            // ── Surface Tension Results ──
            if (config != null && config.CanCalculate && allCleanPeaks.Count > 0)
            {
                string unit = config.Unit;
                double? stMean = config.CalculateSurfaceTension(globalMean);
                double? stMin = config.CalculateSurfaceTension(globalMin);
                double? stMax = config.CalculateSurfaceTension(globalMax);
                double? stStd = config.CalculateSurfaceTension(globalStd);
                double? stSe = config.CalculateSurfaceTension(se);
                double? stCiLower = config.CalculateSurfaceTension(ciLower);
                double? stCiUpper = config.CalculateSurfaceTension(ciUpper);

                if (stMean.HasValue)
                {
                    col.Item().Text($"{sec}. Surface Tension Results").FontSize(14).Bold().FontColor("#1A1A2E");
                    col.Item().PaddingVertical(4);

                    col.Item().Background("#E8F5E9").Border(1).BorderColor("#4CAF50").Padding(12).Column(stc =>
                    {
                        stc.Item().Text($"Mean Surface Tension: {stMean.Value:F3} {unit}")
                            .FontSize(14).Bold().FontColor("#2E7D32");
                        stc.Item().PaddingVertical(4);
                        if (stStd.HasValue) stc.Item().Text($"Standard Deviation: {stStd.Value:F3} {unit}").FontSize(10);
                        if (stSe.HasValue) stc.Item().Text($"Standard Error: {stSe.Value:F3} {unit}").FontSize(10);
                        if (stCiLower.HasValue && stCiUpper.HasValue)
                            stc.Item().Text($"95% CI: [{stCiLower.Value:F3}, {stCiUpper.Value:F3}] {unit}").FontSize(10);
                        if (stMin.HasValue && stMax.HasValue)
                            stc.Item().Text($"Range: {stMin.Value:F3} - {stMax.Value:F3} {unit}").FontSize(10);
                        stc.Item().PaddingVertical(4);
                        if (config.Temperature.HasValue)
                            stc.Item().Text($"Temperature: {config.Temperature.Value} °C").FontSize(9).FontColor("#555555");
                        stc.Item().Text($"Calculation: {config.FormulaDescription}").FontSize(8).FontColor("#888888");
                    });
                    col.Item().PaddingVertical(8);
                    sec++;
                }
            }

            // ── Descriptive Statistics (Force) ──
            col.Item().Text($"{sec}. Descriptive Statistics — Peak Force").FontSize(14).Bold().FontColor("#1A1A2E");
            col.Item().PaddingVertical(4);

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(3f);
                    columns.RelativeColumn(2f);
                });

                var labelStyle = TextStyle.Default.FontSize(9).Bold();
                var valStyle = TextStyle.Default.FontSize(9);
                string[][] rows = {
                    new[] { "Sample Size (n)", totalClean.ToString() },
                    new[] { "Mean Peak Force", $"{globalMean:F6} N" },
                    new[] { "Standard Deviation (SD)", $"{globalStd:F6} N" },
                    new[] { "Relative SD (RSD%)", $"{globalRsd:F2}%" },
                    new[] { "Minimum", $"{globalMin:F6} N" },
                    new[] { "Maximum", $"{globalMax:F6} N" },
                    new[] { "Range", $"{globalRange:F6} N" },
                    new[] { "Standard Error (SE)", $"{se:F6} N" },
                    new[] { "95% Confidence Interval", $"[{ciLower:F6}, {ciUpper:F6}] N" },
                };

                bool alt = false;
                foreach (var r in rows)
                {
                    var bg = alt ? "#F8F9FA" : "#FFFFFF";
                    alt = !alt;
                    table.Cell().Background(bg).Padding(4).Text(r[0]).Style(labelStyle);
                    table.Cell().Background(bg).Padding(4).Text(r[1]).Style(valStyle);
                }
            });
            col.Item().PaddingVertical(8);
            sec++;

            // ── Per-Speed Statistics ──
            bool hasST = config?.CanCalculate == true;
            string stUnit = config?.Unit ?? "mN/m";

            col.Item().Text($"{sec}. Per-Speed Statistical Analysis").FontSize(14).Bold().FontColor("#1A1A2E");
            col.Item().PaddingVertical(4);

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(2f);
                    columns.RelativeColumn(0.6f);
                    columns.RelativeColumn(0.8f);
                    columns.RelativeColumn(1.6f);
                    columns.RelativeColumn(1.6f);
                    columns.RelativeColumn(1f);
                    if (hasST) columns.RelativeColumn(1.6f);
                    columns.RelativeColumn(0.6f);
                });

                var hs = TextStyle.Default.FontSize(8).Bold().FontColor("#FFFFFF");
                table.Header(header =>
                {
                    var headers = new List<string> { "Speed", "B", "Runs", "Avg (N)", "SD (N)", "RSD%" };
                    if (hasST) headers.Add($"ST ({stUnit})");
                    headers.Add("Out");
                    foreach (var h in headers)
                        header.Cell().Background("#2D3748").Padding(4).Text(h).Style(hs);
                });

                bool alt = false;
                foreach (var (_, g) in allData.OrderByDescending(x => x.Value.SpeedMmS).ThenBy(x => x.Value.Batch))
                {
                    var bg = alt ? "#F8F9FA" : "#FFFFFF";
                    alt = !alt;

                    table.Cell().Background(bg).Padding(3).Text(g.BaseName).FontSize(8);
                    table.Cell().Background(bg).Padding(3).Text(g.Batch.ToString()).FontSize(8);
                    table.Cell().Background(bg).Padding(3).Text($"{g.CleanPeaks.Count}/{g.PeakForces.Count}").FontSize(8);
                    table.Cell().Background(bg).Padding(3).Text(g.Avg.ToString("F6")).FontSize(8);
                    table.Cell().Background(bg).Padding(3).Text(g.Std.ToString("F6")).FontSize(8);
                    table.Cell().Background(bg).Padding(3).Text(g.Rsd.ToString("F2") + "%").FontSize(8);
                    if (hasST)
                    {
                        double? st = config!.CalculateSurfaceTension(g.Avg);
                        table.Cell().Background(bg).Padding(3).Text(st?.ToString("F3") ?? "—").FontSize(8);
                    }
                    table.Cell().Background(bg).Padding(3).Text(g.OutlierIndices.Count.ToString()).FontSize(8);
                }
            });
            col.Item().PaddingVertical(8);
            sec++;

            // ── Individual Run Results ──
            col.Item().Text($"{sec}. Individual Run Results").FontSize(14).Bold().FontColor("#1A1A2E");
            col.Item().PaddingVertical(4);

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(2.2f);
                    columns.RelativeColumn(0.8f);
                    columns.RelativeColumn(0.8f);
                    columns.RelativeColumn(1.8f);
                    if (hasST) columns.RelativeColumn(1.5f);
                    columns.RelativeColumn(1f);
                    columns.RelativeColumn(1.2f);
                });

                var hs = TextStyle.Default.FontSize(9).Bold().FontColor("#FFFFFF");
                var headers = new List<string> { "Speed", "B", "#", "Peak (N)" };
                if (hasST) headers.Add($"ST ({stUnit})");
                headers.AddRange(new[] { "Points", "Status" });
                table.Header(header =>
                {
                    foreach (var h in headers)
                        header.Cell().Background("#2D3748").Padding(5).Text(h).Style(hs);
                });

                bool alt = false;
                foreach (var (_, g) in allData.OrderByDescending(x => x.Value.SpeedMmS).ThenBy(x => x.Value.Batch))
                {
                    for (int i = 0; i < g.Runs.Count; i++)
                    {
                        var run = g.Runs[i];
                        bool isOutlier = g.OutlierIndices.Contains(i);
                        var bg = isOutlier ? "#FFF0F0" : (alt ? "#F8F9FA" : "#FFFFFF");
                        alt = !alt;
                        table.Cell().Background(bg).Padding(3).Text(g.BaseName).FontSize(8);
                        table.Cell().Background(bg).Padding(3).Text(g.Batch.ToString()).FontSize(8);
                        table.Cell().Background(bg).Padding(3).Text((i + 1).ToString()).FontSize(8);
                        table.Cell().Background(bg).Padding(3).Text(g.PeakForces[i].ToString("F6")).FontSize(8);
                        if (hasST)
                        {
                            double? st = config!.CalculateSurfaceTension(g.PeakForces[i]);
                            table.Cell().Background(bg).Padding(3).Text(st?.ToString("F3") ?? "—").FontSize(8);
                        }
                        table.Cell().Background(bg).Padding(3).Text(run.PointCount.ToString()).FontSize(8);
                        table.Cell().Background(bg).Padding(3)
                            .Text(isOutlier ? "OUTLIER" : "OK").FontSize(8)
                            .FontColor(isOutlier ? "#E53E3E" : "#38A169");
                    }
                }
            });
            col.Item().PaddingVertical(8);
            sec++;

            // ── Outlier Analysis ──
            col.Item().Text($"{sec}. Outlier Analysis").FontSize(14).Bold().FontColor("#1A1A2E");
            col.Item().PaddingVertical(4);

            col.Item().Background("#FFFBEB").Padding(10).Column(oc =>
            {
                oc.Item().Text("Peak Outlier Rejection").FontSize(10).Bold();
                oc.Item().Text("Method: Modified Z-score with Median Absolute Deviation (MAD)").FontSize(9).Bold();
                oc.Item().Text("Formula: Modified Z = 0.6745 x (x - median) / MAD").FontSize(9);
                oc.Item().Text("Threshold: |Modified Z| > 3.5 = outlier").FontSize(9);
                oc.Item().Text("MAD floor: 2% of |median| to prevent over-rejection on tight data").FontSize(9);
                oc.Item().PaddingVertical(4);

                if (spikeFilterEnabled)
                {
                    string thresholdDesc = spikeThreshold.HasValue
                        ? $"{spikeThreshold.Value} N (user-specified)"
                        : "Auto-detect (Q3 + 2.5 x IQR)";
                    oc.Item().Text("Spike Noise Filter (Time-Series Data)").FontSize(10).Bold();
                    oc.Item().Text("Applied: Yes — spikes replaced with interpolated values").FontSize(9).FontColor("#2E7D32");
                    oc.Item().Text("Method: Threshold-based detection + linear interpolation").FontSize(9);
                    oc.Item().Text($"Threshold: {thresholdDesc}").FontSize(9);
                    oc.Item().Text("Algorithm:").FontSize(9).Bold();
                    oc.Item().Text("  1. Points where force > threshold are marked as spikes").FontSize(8);
                    oc.Item().Text("  2. Consecutive spike points are grouped into regions").FontSize(8);
                    oc.Item().Text("  3. Each spike region is replaced by linear interpolation").FontSize(8);
                    oc.Item().Text("     between the nearest clean neighbors on each side").FontSize(8);
                    oc.Item().Text("  4. Output has the same number of points — no data removed").FontSize(8);
                    oc.Item().PaddingVertical(2);
                    oc.Item().Text("Note: Raw data is always preserved. Filter is applied at render time only.")
                        .FontSize(8).FontColor("#888888");
                }
                else
                {
                    oc.Item().Text("Spike Noise Filter: Not applied").FontSize(9).FontColor("#888888");
                }
            });
            col.Item().PaddingVertical(4);

            if (totalOutliers > 0)
            {
                foreach (var (_, g) in allData.OrderByDescending(x => x.Value.SpeedMmS).ThenBy(x => x.Value.Batch))
                {
                    if (g.OutlierIndices.Count == 0) continue;
                    col.Item().Text($"{g.BaseName} (Batch {g.Batch}):").FontSize(9).Bold();
                    for (int i = 0; i < g.OutlierIndices.Count; i++)
                        col.Item().Text($"  Run #{g.OutlierIndices[i] + 1}: {g.OutlierValues[i]:F6} N")
                            .FontSize(8).FontColor("#E53E3E");
                    col.Item().PaddingVertical(2);
                }
            }
            else
            {
                col.Item().Text("No outliers detected in any speed group.").FontSize(9).FontColor("#38A169");
            }
            col.Item().PaddingVertical(8);
            sec++;

            // ── Instrument Information ──
            col.Item().Text($"{sec}. Instrument Information").FontSize(14).Bold().FontColor("#1A1A2E");
            col.Item().PaddingVertical(4);

            string loadCellDesc = config?.LoadCellType == "30g"
                ? "Type: HX711 ADC + 30g Aluminum Alloy Load Cell"
                : "Type: HX711 ADC + 100g Aluminum Alloy Load Cell";
            string loadCellRange = config?.LoadCellType == "30g"
                ? "Range: 0 - 30 g | Rated output: 0.6 +/- 0.15 mV/V"
                : "Range: 0 - 100 g | Rated output: 0.6 +/- 0.15 mV/V";

            col.Item().Background("#F8F9FA").Padding(10).Column(ic =>
            {
                ic.Item().Text($"Instrument: {config?.Method ?? "Du Noüy Ring"} Surface Tension Tester v7.3").FontSize(9);
                ic.Item().Text("Controller: Arduino Mega 2560").FontSize(9);
                ic.Item().Text("Motor Driver: TMC2209 Stepper Driver").FontSize(9);
                ic.Item().Text("Sampling: Position-based, 5 um intervals").FontSize(9);
                ic.Item().Text("Software: Surface Tension App (C# WPF)").FontSize(9);
                ic.Item().PaddingVertical(4);

                ic.Item().Text("Load Cell Specifications:").FontSize(9).Bold();
                ic.Item().Text($"  {loadCellDesc}").FontSize(8);
                ic.Item().Text($"  {loadCellRange}").FontSize(8);
                ic.Item().Text("  Non-linearity: 0.03% F.S (= +/- 0.03 g)").FontSize(8);
                ic.Item().Text("  Hysteresis: 0.03% F.S | Repeatability: 0.03% F.S").FontSize(8);
                ic.Item().Text("  Creep: 0.03% F.S / 3 min").FontSize(8);
                ic.Item().Text("  Temperature effect: 0.03% F.S / 10 C (zero & span)").FontSize(8);
                ic.Item().Text("  Operating range: -10 C to +40 C").FontSize(8);
                ic.Item().Text("  Safe overload: 120% F.S | Ultimate overload: 150% F.S").FontSize(8);
                ic.Item().PaddingVertical(4);

                ic.Item().Text("Designed and developed by Soranan Suebsilpasakul").FontSize(9).Italic();
                ic.Item().Text("School of Integrated Innovative Technology (SIITec)").FontSize(9).Italic();
                ic.Item().Text("Department of Nanoscience and Nanotechnology").FontSize(9).Italic();
                ic.Item().Text("King Mongkut's Institute of Technology Ladkrabang (KMITL)").FontSize(9).Italic();
            });
        });
    }

    private static void ComposeFooter(IContainer container)
    {
        container.BorderTop(1).BorderColor("#CCCCCC").PaddingTop(4).Row(row =>
        {
            row.RelativeItem().Text(t =>
            {
                t.Span("Surface Tension Tester v7.3 - ").FontSize(7).FontColor("#999999");
                t.Span("S. Suebsilpasakul, SIITec, KMITL")
                    .FontSize(7).FontColor("#999999");
            });
            row.ConstantItem(50).AlignRight().Text(t =>
            {
                t.CurrentPageNumber().FontSize(7);
                t.Span(" / ").FontSize(7);
                t.TotalPages().FontSize(7);
            });
        });
    }
}