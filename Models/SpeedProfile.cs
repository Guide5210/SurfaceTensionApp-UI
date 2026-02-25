namespace SurfaceTensionApp.Models;

/// <summary>
/// One of the 7 speed profiles matching Arduino v7.3 firmware.
/// </summary>
public class SpeedProfile
{
    public int Number { get; init; }          // 1-7
    public string Name { get; init; } = "";   // e.g. "ULTRA_FAST"
    public double SpeedMmS { get; init; }     // mm/s
    public string Description { get; init; } = "";
    public char SerialCmd { get; init; }      // '1'-'7'

    public string DisplayText => $"{Number}: {Description}";

    public static readonly SpeedProfile[] All =
    {
        new() { Number = 1, Name = "ULTRA_FAST", SpeedMmS = 0.600,      Description = "Ultra Fast (600 µm/s)",     SerialCmd = '1' },
        new() { Number = 2, Name = "FAST_UP",    SpeedMmS = 0.450,      Description = "Fast Up (450 µm/s)",        SerialCmd = '2' },
        new() { Number = 3, Name = "Veight",     SpeedMmS = 0.1335,     Description = "V8 (133.5 µm/s)",           SerialCmd = '3' },
        new() { Number = 4, Name = "Vsix",       SpeedMmS = 0.100125,   Description = "V6 (100.125 µm/s)",         SerialCmd = '4' },
        new() { Number = 5, Name = "Vfour",      SpeedMmS = 0.06675,    Description = "V4 (66.75 µm/s)",           SerialCmd = '5' },
        new() { Number = 6, Name = "Vtwo",       SpeedMmS = 0.0333375,  Description = "V2 (33.3375 µm/s)",         SerialCmd = '6' },
        new() { Number = 7, Name = "MEASURE_F",  SpeedMmS = 0.01875,    Description = "Measure Fast (18.75 µm/s)", SerialCmd = '7' },
    };
}
