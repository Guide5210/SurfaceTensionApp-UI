using System.Text.Json;

namespace SurfaceTensionApp.Services;

/// <summary>
/// Stateless parser for the Arduino serial protocol.
/// Converts raw text lines into strongly-typed events consumed by MainViewModel.
/// All state decisions (is a run active? single vs auto?) remain in MainViewModel.
/// </summary>
public class SerialProtocolHandler
{
    // ── Raw line pass-through (for serial log) ──
    public event Action<string>? AnyLineReceived;

    // ── Data streaming ──
    public event Action<double t, double f, double p, double pr>? DataPointReceived;

    // ── Run lifecycle ──
    public event Action<string speed, string runInfo, int batch>? RunStartReceived;
    public event Action? StreamStarted;
    public event Action? StreamEnded;
    public event Action? ReadyOrHomeReceived;
    public event Action<double pos>? ContactAt;
    public event Action<double peak>? PeakValidated;
    /// <summary>RUN_END: speed is the name from the message; peakForce from Arduino; batch from message.</summary>
    public event Action<double peakForce, int batch>? RunEndReceived;

    // ── Auto sequence progress ──
    public event Action<int batchNum>? BatchComplete;
    public event Action? AllBatchesComplete;

    // ── Encoder mode ──
    public event Action<string value>? EncoderHomeReceived;
    public event Action<string value>? EncoderTargetReceived;
    public event Action? EncoderExited;

    // ── Alerts ──
    public event Action? OverloadDetected;

    // ── System info (from 'I' command) ──
    public event Action<string firmware>? FirmwareReceived;
    public event Action<string type>? LoadCellTypeReceived;
    public event Action<string cap>? LoadCellCapReceived;
    public event Action<string factor>? CalFactorReceived;
    public event Action<string limit>? OverloadLimitReceived;
    public event Action? SystemInfoComplete;

    // ── Hardware / calibration ──
    public event Action<string type>? LoadCellChanged;
    public event Action? TareOk;
    public event Action? CalibrationOk;
    public event Action<string msg>? CalibrationError;

    // ── Monitor mode ──
    public event Action<double force>? MonitorForce;

    // ══════════════════════════════════════════════════════
    // Entry point
    // ══════════════════════════════════════════════════════
    public void Process(string line)
    {
        AnyLineReceived?.Invoke(line);

        // ── JSON data point ──
        if (line.StartsWith('{') && line.EndsWith('}'))
        {
            ParseDataPoint(line);
            return;
        }

        if (line.StartsWith("RUN_START:"))            { ParseRunStart(line);                               return; }
        if (line.Contains("START_STREAM"))             { StreamStarted?.Invoke();                           return; }
        if (line.Contains("END_STREAM"))               { StreamEnded?.Invoke();                             return; }
        if (line.Contains("READY") ||
            line.Contains("HOME_OK") ||
            line.Contains("HOME reached"))             { ReadyOrHomeReceived?.Invoke();                     return; }
        if (line.Contains("CONTACT_AT:"))              { ParseContactAt(line);                              return; }
        if (line.Contains("PEAK_VALIDATED:"))          { ParsePeakValidated(line);                          return; }
        if (line.StartsWith("RUN_END:"))               { ParseRunEnd(line);                                 return; }
        if (line.StartsWith("SPEED_STATS:"))           { /* computed locally — ignore */ return; }
        if (line.StartsWith("BATCH_COMPLETE:"))        { ParseBatchComplete(line);                          return; }
        if (line.Contains("ALL BATCHES COMPLETE"))     { AllBatchesComplete?.Invoke();                      return; }
        if (line.Contains("ENC_HOME:"))                { EncoderHomeReceived?.Invoke(line.Split(':')[^1].Trim()); return; }
        if (line.Contains("ENC_TARGET:"))              { EncoderTargetReceived?.Invoke(line.Split(':')[^1].Trim()); return; }
        if (line.Contains("ENC_EXIT"))                 { EncoderExited?.Invoke();                           return; }
        if (line.Contains("OVERLOAD"))                 { OverloadDetected?.Invoke();                        return; }
        if (line.StartsWith("FIRMWARE:"))              { FirmwareReceived?.Invoke(line[9..]);               return; }
        if (line.StartsWith("LOADCELL_TYPE:"))         { LoadCellTypeReceived?.Invoke(line[14..]);          return; }
        if (line.StartsWith("LOADCELL_CAP:"))          { LoadCellCapReceived?.Invoke(line[13..]);           return; }
        if (line.StartsWith("CAL_FACTOR:"))            { CalFactorReceived?.Invoke(line[11..]);             return; }
        if (line.StartsWith("OVERLOAD_LIM:"))          { OverloadLimitReceived?.Invoke(line[13..]);         return; }
        if (line == "END_INFO")                        { SystemInfoComplete?.Invoke();                      return; }
        if (line.StartsWith("LOADCELL_CHANGED:"))      { LoadCellChanged?.Invoke(line[17..]);               return; }
        if (line.Contains("TARE_OK"))                  { TareOk?.Invoke();                                  return; }
        if (line.StartsWith("CAL_OK") ||
            line.StartsWith("CAL_DONE"))               { CalibrationOk?.Invoke();                           return; }
        if (line.StartsWith("CAL_ERR") ||
            line.StartsWith("CAL_FAIL"))               { CalibrationError?.Invoke(line);                    return; }
        if (line.Contains("Force:"))                   { ParseMonitorForce(line); }
    }

    // ══════════════════════════════════════════════════════
    // Parsers
    // ══════════════════════════════════════════════════════
    private void ParseDataPoint(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            double t  = root.TryGetProperty("t",  out var tv)  ? tv.GetDouble()  : 0;
            double f  = root.TryGetProperty("f",  out var fv)  ? fv.GetDouble()  : 0;
            double p  = root.TryGetProperty("p",  out var pv)  ? pv.GetDouble()  : 0;
            double pr = root.TryGetProperty("pr", out var prv) ? prv.GetDouble() : p;
            DataPointReceived?.Invoke(t, f, p, pr);
        }
        catch (JsonException)
        {
            // Malformed JSON from serial — skip silently; caller logs the raw line
        }
    }

    private void ParseRunStart(string line)
    {
        var parts = line.Split(':');
        if (parts.Length < 3) return;
        string speed   = parts[1];
        string runInfo = parts[2];
        int batch      = parts.Length >= 4 && int.TryParse(parts[3], out int b) ? b : 1;
        RunStartReceived?.Invoke(speed, runInfo, batch);
    }

    private void ParseContactAt(string line)
    {
        if (double.TryParse(line.Split(':')[^1], out double pos))
            ContactAt?.Invoke(pos);
    }

    private void ParsePeakValidated(string line)
    {
        if (double.TryParse(line.Split(':')[^1], out double peak))
            PeakValidated?.Invoke(peak);
    }

    private void ParseRunEnd(string line)
    {
        // Format: RUN_END:speedName:runNum:peakForce[:batch]
        var parts = line.Split(':');
        if (parts.Length < 4) return;
        double peakForce = double.TryParse(parts[3], out double pk) ? pk : 0;
        int batch        = parts.Length >= 5 && int.TryParse(parts[4], out int b) ? b : 1;
        RunEndReceived?.Invoke(peakForce, batch);
    }

    private void ParseBatchComplete(string line)
    {
        if (int.TryParse(line.Split(':')[^1], out int bn))
            BatchComplete?.Invoke(bn);
    }

    private void ParseMonitorForce(string line)
    {
        int idx = line.IndexOf("Force:");
        if (idx < 0) return;
        var token = line[(idx + 6)..].Trim().Split(' ')[0];
        if (double.TryParse(token, out double f))
            MonitorForce?.Invoke(f);
    }
}
