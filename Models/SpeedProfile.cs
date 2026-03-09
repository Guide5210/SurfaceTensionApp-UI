namespace SurfaceTensionApp.Models;

/// <summary>
/// One of the 12 speed profiles matching Arduino v7.3 firmware.
/// </summary>
public class SpeedProfile
{
    public int Number { get; init; }          // 1-12
    public string Name { get; init; } = "";   // e.g. "ULTRA_FAST"
    public double SpeedMmS { get; init; }     // mm/s
    public string Description { get; init; } = "";
    public char SerialCmd { get; init; }      // '1'-'9', 'B', 'C', or '\0' for auto-only
    public bool ManualAvailable => SerialCmd != '\0';

    public string DisplayText => $"{Number}: {Description}";

    public static readonly SpeedProfile[] All =
    {
        new() { Number = 1,  Name = "ULTRA_FAST", SpeedMmS = 0.600,       Description = "Ultra Fast (600 µm/s)",       SerialCmd = '1' },
        new() { Number = 2,  Name = "FAST_UP",    SpeedMmS = 0.450,       Description = "Fast Up (450 µm/s)",          SerialCmd = '2' },
        new() { Number = 3,  Name = "FAST_DN",    SpeedMmS = 0.150,       Description = "Fast Down (150 µm/s)",        SerialCmd = '3' },
        new() { Number = 4,  Name = "Veight",     SpeedMmS = 0.1335,      Description = "V8 (133.5 µm/s)",             SerialCmd = '4' },
        new() { Number = 5,  Name = "Vsix",       SpeedMmS = 0.100125,    Description = "V6 (100.125 µm/s)",           SerialCmd = '5' },
        new() { Number = 6,  Name = "Vfour",      SpeedMmS = 0.06675,     Description = "V4 (66.75 µm/s)",             SerialCmd = '6' },
        new() { Number = 7,  Name = "Vtwo",       SpeedMmS = 0.0333375,   Description = "V2 (33.3375 µm/s)",           SerialCmd = '7' },
        new() { Number = 8,  Name = "MEASURE_F",  SpeedMmS = 0.01875,     Description = "Measure F (18.75 µm/s)",      SerialCmd = '8' },
        new() { Number = 9,  Name = "MEASURE_M",  SpeedMmS = 0.0075,      Description = "Measure M (7.50 µm/s)",       SerialCmd = '9' },
        new() { Number = 10, Name = "MEASURE_U",  SpeedMmS = 0.00375,     Description = "Measure U (3.75 µm/s)",       SerialCmd = '\0' }, // Auto only
        new() { Number = 11, Name = "MEASURE_X",  SpeedMmS = 0.001875,    Description = "Measure X (1.875 µm/s)",      SerialCmd = 'B' },
        new() { Number = 12, Name = "MEASURE_Z",  SpeedMmS = 0.00075,     Description = "Measure Z (0.75 µm/s)",       SerialCmd = 'C' },
    };
}