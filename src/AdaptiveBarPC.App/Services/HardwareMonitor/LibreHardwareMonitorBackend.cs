using LibreHardwareMonitor.Hardware;

namespace AdaptiveBarPC.Services.HardwareMonitor;

/// <summary>
/// Production hardware monitoring backend powered by LibreHardwareMonitorLib.
/// Opens a single Computer instance and samples CPU, GPU, and Memory sensors.
/// </summary>
public sealed class LibreHardwareMonitorBackend : IHardwareMonitorBackend
{
    private readonly AppLogger? _logger;
    private Computer? _computer;
    private bool _initialized;
    private bool _disposed;
    private readonly object _gate = new();

    public LibreHardwareMonitorBackend(AppLogger? logger = null)
    {
        _logger = logger;
    }

    public bool Initialize()
    {
        lock (_gate)
        {
            if (_disposed) return false;
            if (_initialized) return true;

            try
            {
                var computer = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    IsMemoryEnabled = true
                };

                computer.Open();
                _computer = computer;
                _initialized = true;

                LogDiscovery(computer);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.Warn($"LibreHardwareMonitor failed to initialize: {ex.Message}");
                _initialized = false;
                _computer = null;
                return false;
            }
        }
    }

    private void LogDiscovery(Computer computer)
    {
        try
        {
            _logger?.Info("LibreHardwareMonitor backend initialized successfully.");
            foreach (var hw in computer.Hardware)
            {
                _logger?.Info($"Discovered hardware: {hw.Name} ({hw.HardwareType}) [ID: {hw.Identifier}]");
            }
        }
        catch
        {
            // Non-fatal logging
        }
    }

    public IReadOnlyList<RawSensorReading> Sample()
    {
        lock (_gate)
        {
            if (_disposed || !_initialized || _computer == null)
            {
                return Array.Empty<RawSensorReading>();
            }

            var readings = new List<RawSensorReading>();

            try
            {
                foreach (var hardware in _computer.Hardware)
                {
                    SampleHardware(hardware, readings);
                }
            }
            catch (Exception ex)
            {
                _logger?.Warn($"Exception during hardware sensor sampling: {ex.Message}");
            }

            return readings;
        }
    }

    private static void SampleHardware(IHardware hardware, List<RawSensorReading> readings)
    {
        try
        {
            hardware.Update();

            var category = MapHardwareCategory(hardware.HardwareType);
            if (category.HasValue)
            {
                CollectSensors(hardware, category.Value, readings);
            }

            foreach (var sub in hardware.SubHardware)
            {
                sub.Update();
                var subCategory = MapHardwareCategory(sub.HardwareType) ?? category;
                if (subCategory.HasValue)
                {
                    CollectSensors(sub, subCategory.Value, readings);
                }
            }
        }
        catch
        {
            // Tolerate hardware read issues gracefully
        }
    }

    private static HardwareCategory? MapHardwareCategory(HardwareType type)
    {
        return type switch
        {
            HardwareType.Cpu => HardwareCategory.Cpu,
            HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => HardwareCategory.Gpu,
            HardwareType.Memory => HardwareCategory.Memory,
            _ => null
        };
    }

    private static void CollectSensors(IHardware hardware, HardwareCategory category, List<RawSensorReading> readings)
    {
        var hwId = hardware.Identifier.ToString();
        var hwName = hardware.Name;

        foreach (var sensor in hardware.Sensors)
        {
            SensorKind? kind = sensor.SensorType switch
            {
                SensorType.Load => SensorKind.Load,
                SensorType.Temperature => SensorKind.Temperature,
                _ => null
            };

            if (kind.HasValue)
            {
                readings.Add(new RawSensorReading(
                    HardwareId: hwId,
                    HardwareName: hwName,
                    Category: category,
                    Kind: kind.Value,
                    SensorName: sensor.Name,
                    Value: sensor.Value));
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _computer?.Close();
            }
            catch (Exception ex)
            {
                _logger?.Warn($"Error closing LibreHardwareMonitor Computer: {ex.Message}");
            }
            finally
            {
                _computer = null;
                _initialized = false;
            }
        }
    }
}
