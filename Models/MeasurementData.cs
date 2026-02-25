namespace SurfaceTensionApp.Models;

/// <summary>
/// A single data point from Arduino JSON: {"t":float,"f":float,"p":float,"pr":float}
/// </summary>
public readonly record struct MeasurementPoint(double Time, double Force, double Position, double RelPosition);
