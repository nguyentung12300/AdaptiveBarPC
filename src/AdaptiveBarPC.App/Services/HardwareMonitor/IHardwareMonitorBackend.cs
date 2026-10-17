namespace AdaptiveBarPC.Services.HardwareMonitor;

/// <summary>
/// Internal backend abstraction for hardware sensor discovery and sampling.
/// Allows unit tests to verify sensor selection and normalization without accessing physical hardware.
/// </summary>
public interface IHardwareMonitorBackend : IDisposable
{
    /// <summary>
    /// Initializes and opens the hardware monitor. Returns true if hardware was opened successfully.
    /// </summary>
    bool Initialize();

    /// <summary>
    /// Samples current hardware readings for CPU, GPU, and Memory.
    /// </summary>
    IReadOnlyList<RawSensorReading> Sample();
}
