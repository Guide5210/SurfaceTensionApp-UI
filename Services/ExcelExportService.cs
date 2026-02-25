using System.IO;
using ClosedXML.Excel;
using SurfaceTensionApp.Models;

namespace SurfaceTensionApp.Services;

/// <summary>
/// Exports measurement data to .xlsx matching the Python v7.3 format exactly:
///  - {SpeedName}_Sum sheet: Raw stats + Cleaned stats + run table
///  - {SpeedName}_R{n} sheets: Time/Force/Position/RelPosition columns
/// </summary>
public static class ExcelExportService
{
    public static string Export(Dictionary<string, SpeedGroup> allData, string outputDir)
    {
        if (allData.Count == 0) throw new InvalidOperationException("No data to export.");

        Directory.CreateDirectory(outputDir);
        string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string path = Path.Combine(outputDir, $"surface_tension_results_{ts}.xlsx");

        using var wb = new XLWorkbook();

        var headerFill = XLColor.FromHtml("#D9E1F2");
        var titleFill = XLColor.FromHtml("#1F4E78");
        var outlierFill = XLColor.FromHtml("#FFE0E0");

        foreach (var (key, group) in allData)
        {
            group.ComputeOutliers();
            var runs = group.Runs;
            var peaks = group.PeakForces;
            int nr = runs.Count;

            // ── Summary sheet ──
            string sumName = TruncateSheetName($"{key}_Sum");
            var ws = wb.Worksheets.Add(sumName);

            // Title row
            ws.Cell("A1").Value = $"Summary - {key}";
            ws.Cell("A1").Style.Font.SetBold(true).Font.SetFontSize(14).Font.SetFontColor(XLColor.White);
            ws.Cell("A1").Style.Fill.SetBackgroundColor(titleFill);
            ws.Range("A1:F1").Merge();

            ws.Cell("A3").Value = "Speed (µm/s):";
            ws.Cell("B3").Value = group.SpeedMmS * 1000;
            ws.Cell("A4").Value = "Runs:";
            ws.Cell("B4").Value = nr;

            if (nr > 0 && group.CleanPeaks.Count > 0)
            {
                double rawAvg = peaks.Average();
                double rawStd = StdDev(peaks);
                double rawRsd = rawAvg != 0 ? rawStd / rawAvg * 100 : 0;

                // Raw stats
                ws.Cell("A6").Value = "--- RAW (all runs) ---";
                ws.Cell("A6").Style.Font.SetBold(true).Font.SetFontColor(XLColor.FromHtml("#808080"));
                ws.Cell("A7").Value = "Raw Avg (N):"; ws.Cell("B7").Value = Math.Round(rawAvg, 6);
                ws.Cell("A8").Value = "Raw Std (N):"; ws.Cell("B8").Value = Math.Round(rawStd, 6);
                ws.Cell("A9").Value = "Raw RSD (%):"; ws.Cell("B9").Value = Math.Round(rawRsd, 2);

                // Cleaned stats
                ws.Cell("A11").Value = "--- CLEANED (outliers removed) ---";
                ws.Cell("A11").Style.Font.SetBold(true).Font.SetFontColor(XLColor.FromHtml("#1F4E78"));
                ws.Cell("A12").Value = "Avg Peak (N):";
                ws.Cell("B12").Value = Math.Round(group.Avg, 6);
                ws.Cell("B12").Style.Font.SetBold(true);
                ws.Cell("A13").Value = "Std Dev (N):";
                ws.Cell("B13").Value = Math.Round(group.Std, 6);
                ws.Cell("B13").Style.Font.SetBold(true);
                ws.Cell("A14").Value = "RSD (%):";
                ws.Cell("B14").Value = Math.Round(group.Rsd, 2);
                ws.Cell("B14").Style.Font.SetBold(true);
                ws.Cell("A15").Value = "Valid Runs:"; ws.Cell("B15").Value = group.CleanPeaks.Count;
                ws.Cell("A16").Value = "Outliers:"; ws.Cell("B16").Value = group.OutlierIndices.Count;
                if (group.OutlierValues.Count > 0)
                {
                    ws.Cell("C16").Value = $"Removed: {string.Join(", ", group.OutlierValues.Select(v => v.ToString("F6")))}";
                    ws.Cell("C16").Style.Font.SetFontColor(XLColor.Red);
                }
            }

            // Run table header (row 18)
            string[] headers = { "Run #", "Peak (N)", "Validated Peak", "Points", "Contact (mm)", "Status" };
            for (int c = 0; c < headers.Length; c++)
            {
                var cell = ws.Cell(18, c + 1);
                cell.Value = headers[c];
                cell.Style.Fill.SetBackgroundColor(headerFill);
                cell.Style.Font.SetBold(true);
            }

            // Run rows
            for (int ri = 0; ri < nr; ri++)
            {
                int row = 19 + ri;
                var run = runs[ri];
                double pf = peaks[ri];
                bool isOutlier = group.OutlierIndices.Contains(ri);

                ws.Cell(row, 1).Value = ri + 1;
                ws.Cell(row, 2).Value = Math.Round(pf, 6);
                ws.Cell(row, 3).Value = run.ValidatedPeak.HasValue ? Math.Round(run.ValidatedPeak.Value, 6).ToString() : "N/A";
                ws.Cell(row, 4).Value = run.PointCount;
                ws.Cell(row, 5).Value = run.ContactPosition.HasValue ? Math.Round(run.ContactPosition.Value, 4).ToString() : "N/A";
                ws.Cell(row, 6).Value = isOutlier ? "OUTLIER" : "OK";

                if (isOutlier)
                {
                    for (int c = 1; c <= 6; c++)
                        ws.Cell(row, c).Style.Fill.SetBackgroundColor(outlierFill);
                    ws.Cell(row, 6).Style.Font.SetBold(true).Font.SetFontColor(XLColor.Red);
                }
            }

            ws.Column(1).Width = 24;
            ws.Column(2).Width = 16;
            ws.Column(3).Width = 16;
            ws.Column(4).Width = 12;
            ws.Column(5).Width = 14;
            ws.Column(6).Width = 12;

            // ── Data sheets (one per run) ──
            for (int ri = 0; ri < nr; ri++)
            {
                string dataName = TruncateSheetName($"{key}_R{ri + 1}");
                var wd = wb.Worksheets.Add(dataName);
                var run = runs[ri];

                wd.Cell("A1").Value = $"{key} Run #{ri + 1}";
                wd.Cell("A1").Style.Font.SetBold(true).Font.SetFontSize(12).Font.SetFontColor(XLColor.White);
                wd.Cell("A1").Style.Fill.SetBackgroundColor(titleFill);
                wd.Range("A1:D1").Merge();

                string[] dHeaders = { "Time (s)", "Force (N)", "Position (mm)", "Rel. Position (mm)" };
                for (int c = 0; c < dHeaders.Length; c++)
                {
                    var cell = wd.Cell(3, c + 1);
                    cell.Value = dHeaders[c];
                    cell.Style.Fill.SetBackgroundColor(headerFill);
                    cell.Style.Font.SetBold(true);
                }

                int count = run.Times.Count;
                for (int di = 0; di < count; di++)
                {
                    int row = 4 + di;
                    wd.Cell(row, 1).Value = Math.Round(run.Times[di], 3);
                    wd.Cell(row, 2).Value = Math.Round(run.Forces[di], 5);
                    wd.Cell(row, 3).Value = Math.Round(run.Positions[di], 4);
                    if (di < run.RelPositions.Count)
                        wd.Cell(row, 4).Value = Math.Round(run.RelPositions[di], 4);
                }

                for (int c = 1; c <= 4; c++)
                    wd.Column(c).Width = 16;
            }
        }

        wb.SaveAs(path);
        return path;
    }

    private static string TruncateSheetName(string name) =>
        name.Length > 31 ? name[..31] : name;

    private static double StdDev(List<double> vals)
    {
        if (vals.Count == 0) return 0;
        double avg = vals.Average();
        return Math.Sqrt(vals.Sum(v => (v - avg) * (v - avg)) / vals.Count);
    }
}
