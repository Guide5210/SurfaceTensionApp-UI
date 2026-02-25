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
        string? notes = null)
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
                page.Header().Element(c => ComposeHeader(c, title));
                page.Content().Element(c => ComposeContent(c, allData, graphImage, notes));
                page.Footer().Element(ComposeFooter);
            });
        }).GeneratePdf(path);

        return path;
    }

    private static void ComposeHeader(IContainer container, string? title)
    {
        container.Column(col =>
        {
            col.Item().BorderBottom(2).BorderColor("#4A9EFF").PaddingBottom(8).Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text(title ?? "Surface Tension Measurement Report")
                        .FontSize(18).Bold().FontColor("#1A1A2E");
                    c.Item().Text("Du Nouy Ring Method - Automated Analysis")
                        .FontSize(10).FontColor("#666666");
                    c.Item().Text($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
                        .FontSize(8).FontColor("#999999");
                });
            });
        });
    }

    private static void ComposeContent(IContainer container, Dictionary<string, SpeedGroup> allData, byte[]? graphImage, string? notes)
    {
        container.PaddingVertical(10).Column(col =>
        {
            // ── Notes ──
            if (!string.IsNullOrWhiteSpace(notes))
            {
                col.Item().Background("#F0F4FF").Padding(10).Column(nc =>
                {
                    nc.Item().Text("Notes").Bold().FontSize(10);
                    nc.Item().Text(notes).FontSize(9);
                });
                col.Item().PaddingVertical(8);
            }

            int sec = 1;

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

            // Compute global stats
            var allCleanPeaks = allData.Values.SelectMany(g => g.CleanPeaks).ToList();
            double globalMean = allCleanPeaks.Count > 0 ? allCleanPeaks.Average() : 0;
            double globalStd = allCleanPeaks.Count > 1 ? Math.Sqrt(allCleanPeaks.Sum(v => (v - globalMean) * (v - globalMean)) / allCleanPeaks.Count) : 0;
            double globalMin = allCleanPeaks.Count > 0 ? allCleanPeaks.Min() : 0;
            double globalMax = allCleanPeaks.Count > 0 ? allCleanPeaks.Max() : 0;
            double globalRange = globalMax - globalMin;
            double globalRsd = globalMean != 0 ? globalStd / globalMean * 100 : 0;

            // Confidence interval (95%, t-dist approximation)
            double tValue = allCleanPeaks.Count >= 30 ? 1.96 : 2.0; // simplified
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
                });
            });
            col.Item().PaddingVertical(8);
            sec++;

            // ── Descriptive Statistics ──
            col.Item().Text($"{sec}. Descriptive Statistics (All Valid Runs)").FontSize(14).Bold().FontColor("#1A1A2E");
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
            col.Item().Text($"{sec}. Per-Speed Statistical Analysis").FontSize(14).Bold().FontColor("#1A1A2E");
            col.Item().PaddingVertical(4);

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(2.2f);
                    columns.RelativeColumn(0.8f);
                    columns.RelativeColumn(1f);
                    columns.RelativeColumn(1.8f);
                    columns.RelativeColumn(1.8f);
                    columns.RelativeColumn(1.2f);
                    columns.RelativeColumn(1.8f);
                    columns.RelativeColumn(0.8f);
                });

                var hs = TextStyle.Default.FontSize(8).Bold().FontColor("#FFFFFF");
                table.Header(header =>
                {
                    foreach (var h in new[] { "Speed", "B", "Runs", "Avg (N)", "SD (N)", "RSD%", "95% CI (N)", "Out" })
                        header.Cell().Background("#2D3748").Padding(4).Text(h).Style(hs);
                });

                bool alt = false;
                foreach (var (_, g) in allData.OrderByDescending(x => x.Value.SpeedMmS).ThenBy(x => x.Value.Batch))
                {
                    var bg = alt ? "#F8F9FA" : "#FFFFFF";
                    alt = !alt;
                    double gSe = g.CleanPeaks.Count > 1 ? g.Std / Math.Sqrt(g.CleanPeaks.Count) : 0;
                    double gT = g.CleanPeaks.Count >= 30 ? 1.96 : 2.0;
                    string ci = $"+/-{(gT * gSe):F6}";

                    table.Cell().Background(bg).Padding(3).Text(g.BaseName).FontSize(8);
                    table.Cell().Background(bg).Padding(3).Text(g.Batch.ToString()).FontSize(8);
                    table.Cell().Background(bg).Padding(3).Text($"{g.CleanPeaks.Count}/{g.PeakForces.Count}").FontSize(8);
                    table.Cell().Background(bg).Padding(3).Text(g.Avg.ToString("F6")).FontSize(8);
                    table.Cell().Background(bg).Padding(3).Text(g.Std.ToString("F6")).FontSize(8);
                    table.Cell().Background(bg).Padding(3).Text(g.Rsd.ToString("F2") + "%").FontSize(8);
                    table.Cell().Background(bg).Padding(3).Text(ci).FontSize(8);
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
                    columns.RelativeColumn(2.5f);
                    columns.RelativeColumn(1f);
                    columns.RelativeColumn(1f);
                    columns.RelativeColumn(2f);
                    columns.RelativeColumn(1.2f);
                    columns.RelativeColumn(1.5f);
                });

                var hs = TextStyle.Default.FontSize(9).Bold().FontColor("#FFFFFF");
                table.Header(header =>
                {
                    foreach (var h in new[] { "Speed", "B", "#", "Peak (N)", "Points", "Status" })
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
                oc.Item().Text("Method: Modified Z-score with Median Absolute Deviation (MAD)").FontSize(9).Bold();
                oc.Item().Text("Formula: Modified Z = 0.6745 x (x - median) / MAD").FontSize(9);
                oc.Item().Text("Threshold: |Modified Z| > 3.5 = outlier").FontSize(9);
                oc.Item().Text("MAD floor: 2% of |median| to prevent over-rejection on tight data").FontSize(9);
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

            col.Item().Background("#F8F9FA").Padding(10).Column(ic =>
            {
                ic.Item().Text("Instrument: Du Nouy Ring Surface Tension Tester v7.3").FontSize(9);
                ic.Item().Text("Controller: Arduino Mega 2560").FontSize(9);
                ic.Item().Text("Motor Driver: TMC2209 Stepper Driver").FontSize(9);
                ic.Item().Text("Sampling: Position-based, 5 um intervals").FontSize(9);
                ic.Item().Text("Software: Surface Tension App (C# WPF)").FontSize(9);
                ic.Item().PaddingVertical(4);

                ic.Item().Text("Load Cell Specifications:").FontSize(9).Bold();
                ic.Item().Text("  Type: HX711 ADC + 100g Aluminum Alloy Load Cell").FontSize(8);
                ic.Item().Text("  Range: 0 - 100 g | Rated output: 0.6 +/- 0.15 mV/V").FontSize(8);
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
