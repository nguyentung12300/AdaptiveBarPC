namespace AdaptiveBarPC.Services.HardwareMonitor;

public enum HardwareCategory
{
    Cpu,
    Gpu,
    Memory
}

public enum SensorKind
{
    Load,
    Temperature
}

/// <summary>
/// Raw sensor reading sampled from the hardware monitoring backend.
/// Decouples LibreHardwareMonitorLib types from the provider and UI layers.
/// </summary>
public sealed record RawSensorReading(
    string HardwareId,
    string HardwareName,
    HardwareCategory Category,
    SensorKind Kind,
    string SensorName,
    float? Value);
