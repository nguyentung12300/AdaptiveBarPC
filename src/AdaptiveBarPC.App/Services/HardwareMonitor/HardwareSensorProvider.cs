using AdaptiveBarPC.Models;

namespace AdaptiveBarPC.Services.HardwareMonitor;

/// <summary>
/// Production dynamic widget provider for curated live hardware monitoring.
/// Emits CPU, GPU, and RAM load/temperature sensors through the Dynamic Widget Engine.
/// </summary>
public sealed class HardwareSensorProvider : IWidgetProvider
{
    public const string DefaultProviderId = "hardware";

    public string Id { get; } = DefaultProviderId;

    private readonly IHardwareMonitorBackend _backend;
    private readonly AppLogger? _logger;
    private readonly int _sampleIntervalMs;
    private readonly int? _maxSensors;
    private readonly System.Threading.Timer? _timer;
    private readonly object _gate = new();

    private IReadOnlyList<DynamicItemSnapshot> _currentItems = Array.Empty<DynamicItemSnapshot>();
    private long _revisionCounter;
    private bool _disposed;

    public event EventHandler<WidgetItemsChangedEventArgs>? ItemsChanged;

    public IReadOnlyList<DynamicItemSnapshot> CurrentItems
    {
        get
        {
            lock (_gate)
                return _currentItems;
        }
    }

    public long CurrentRevision => Volatile.Read(ref _revisionCounter);
    public bool IsDisposed => _disposed;

    public HardwareSensorProvider(
        IHardwareMonitorBackend? backend = null,
        AppLogger? logger = null,
        int sampleIntervalMs = 1000,
        int? maxSensors = null)
    {
        _logger = logger;
        _sampleIntervalMs = Math.Clamp(sampleIntervalMs, 500, 5000);
        _maxSensors = maxSensors;
        _backend = backend ?? new LibreHardwareMonitorBackend(logger);

        // Initialize backend and take initial reading synchronously
        bool initialized = false;
        try
        {
            initialized = _backend.Initialize();
        }
        catch (Exception ex)
        {
            _logger?.Warn($"HardwareSensorProvider failed to initialize backend: {ex.Message}");
        }

        if (initialized)
        {
            // Initial snapshot
            SampleAndPublish(isInitial: true);

            // Start background polling timer
            _timer = new System.Threading.Timer(OnPollTimer, null, _sampleIntervalMs, _sampleIntervalMs);
        }
        else
        {
            _logger?.Warn("HardwareSensorProvider started in empty/unavailable state.");
        }
    }

    private void OnPollTimer(object? state)
    {
        if (_disposed) return;
        SampleAndPublish(isInitial: false);
    }

    public void SampleAndPublish(bool isInitial = false)
    {
        lock (_gate)
        {
            if (_disposed) return;

            IReadOnlyList<RawSensorReading> rawReadings;
            try
            {
                rawReadings = _backend.Sample();
            }
            catch (Exception ex)
            {
                _logger?.Warn($"Error during hardware sensor sampling: {ex.Message}");
                rawReadings = Array.Empty<RawSensorReading>();
            }

            var newSnapshot = BuildCuratedSnapshot(rawReadings, _maxSensors);

            // Change suppression: if values, labels, and order match exactly, suppress UI event
            if (!isInitial && SnapshotsEqual(_currentItems, newSnapshot))
            {
                return;
            }

            _currentItems = newSnapshot;
            long revision = Interlocked.Increment(ref _revisionCounter);

            try
            {
                ItemsChanged?.Invoke(this, new WidgetItemsChangedEventArgs(newSnapshot, revision));
            }
            catch (Exception ex)
            {
                _logger?.Warn($"Error invoking ItemsChanged event in HardwareSensorProvider: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Pure function selecting and normalizing curated sensors from raw readings.
    /// Internal for direct unit testing with fake backends.
    /// </summary>
    internal static IReadOnlyList<DynamicItemSnapshot> BuildCuratedSnapshot(
        IReadOnlyList<RawSensorReading> readings,
        int? maxSensors = null)
    {
        if (readings == null || readings.Count == 0)
        {
            return Array.Empty<DynamicItemSnapshot>();
        }

        var candidates = new List<PrioritizedSensor>();

        // 1. CPU
        var cpuReadings = readings.Where(r => r.Category == HardwareCategory.Cpu).ToList();
        if (cpuReadings.Count > 0)
        {
            // CPU Load
            var cpuLoad = SelectBestSensor(cpuReadings, SensorKind.Load, new[] { "total", "cpu total", "core max" });
            if (cpuLoad != null && IsValidPercent(cpuLoad.Value))
            {
                candidates.Add(new PrioritizedSensor(
                    Priority: 1, // CPU load highest priority
                    DisplayOrder: 10,
                    Snapshot: new DynamicItemSnapshot
                    {
                        Id = "hardware:cpu:load",
                        Type = WidgetTypes.Sensor,
                        Label = "CPU",
                        Value = Math.Round(cpuLoad.Value!.Value),
                        Unit = "%",
                        Format = "{0:F0}",
                        Metric = "cpu_load",
                        Width = 76
                    }));
            }

            // CPU Temperature
            var cpuTemp = SelectBestSensor(cpuReadings, SensorKind.Temperature,
                new[] { "package", "cpu package", "core (tctl/tdie)", "core max", "cpu core" });
            if (cpuTemp != null && IsValidTemp(cpuTemp.Value))
            {
                candidates.Add(new PrioritizedSensor(
                    Priority: 4, // CPU temperature priority 4
                    DisplayOrder: 20,
                    Snapshot: new DynamicItemSnapshot
                    {
                        Id = "hardware:cpu:temperature",
                        Type = WidgetTypes.Sensor,
                        Label = "CPU",
                        Value = Math.Round(cpuTemp.Value!.Value),
                        Unit = "°C",
                        Format = "{0:F0}",
                        Metric = "cpu_temp",
                        Width = 76
                    }));
            }
        }

        // 2. GPUs (Supports single & multiple GPUs)
        var gpuGroups = readings
            .Where(r => r.Category == HardwareCategory.Gpu)
            .GroupBy(r => r.HardwareId, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int gpuCount = gpuGroups.Count;
        for (int i = 0; i < gpuCount; i++)
        {
            var group = gpuGroups[i];
            var gpuReadings = group.ToList();
            string rawHwId = group.Key;
            string sanitizedHwId = SanitizeHardwareId(rawHwId, i);
            string gpuLabel = gpuCount > 1 ? $"GPU {i + 1}" : "GPU";
            double gpuWidth = gpuCount > 1 ? 84 : 76;

            // GPU Load
            var gpuLoad = SelectBestSensor(gpuReadings, SensorKind.Load,
                new[] { "gpu core", "core", "d3d 3d", "total" });
            if (gpuLoad != null && IsValidPercent(gpuLoad.Value))
            {
                candidates.Add(new PrioritizedSensor(
                    Priority: 2, // GPU load priority 2
                    DisplayOrder: 30 + (i * 2),
                    Snapshot: new DynamicItemSnapshot
                    {
                        Id = $"hardware:gpu:{sanitizedHwId}:load",
                        Type = WidgetTypes.Sensor,
                        Label = gpuLabel,
                        Value = Math.Round(gpuLoad.Value!.Value),
                        Unit = "%",
                        Format = "{0:F0}",
                        Metric = "gpu_load",
                        Width = gpuWidth
                    }));
            }

            // GPU Temperature
            var gpuTemp = SelectBestSensor(gpuReadings, SensorKind.Temperature,
                new[] { "gpu core", "core", "hot spot" });
            if (gpuTemp != null && IsValidTemp(gpuTemp.Value))
            {
                candidates.Add(new PrioritizedSensor(
                    Priority: 3, // GPU temperature priority 3
                    DisplayOrder: 31 + (i * 2),
                    Snapshot: new DynamicItemSnapshot
                    {
                        Id = $"hardware:gpu:{sanitizedHwId}:temperature",
                        Type = WidgetTypes.Sensor,
                        Label = gpuLabel,
                        Value = Math.Round(gpuTemp.Value!.Value),
                        Unit = "°C",
                        Format = "{0:F0}",
                        Metric = "gpu_temp",
                        Width = gpuWidth
                    }));
            }
        }

        // 3. RAM (Memory)
        var memReadings = readings
            .Where(r => r.Category == HardwareCategory.Memory)
            // Prefer physical /ram over /vram
            .OrderByDescending(r => r.HardwareId.Contains("/ram", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (memReadings.Count > 0)
        {
            var ramLoad = SelectBestSensor(memReadings, SensorKind.Load, new[] { "memory", "load" });
            if (ramLoad != null && IsValidPercent(ramLoad.Value))
            {
                candidates.Add(new PrioritizedSensor(
                    Priority: 5, // RAM load priority 5
                    DisplayOrder: 50,
                    Snapshot: new DynamicItemSnapshot
                    {
                        Id = "hardware:memory:load",
                        Type = WidgetTypes.Sensor,
                        Label = "RAM",
                        Value = Math.Round(ramLoad.Value!.Value),
                        Unit = "%",
                        Format = "{0:F0}",
                        Metric = "ram_load",
                        Width = 76
                    }));
            }
        }

        // Apply maxSensors prioritization if specified
        if (maxSensors.HasValue && maxSensors.Value > 0 && candidates.Count > maxSensors.Value)
        {
            candidates = candidates
                .OrderBy(c => c.Priority)
                .Take(maxSensors.Value)
                .ToList();
        }

        // Return candidates in natural visual display order (CPU -> GPU -> RAM)
        return candidates
            .OrderBy(c => c.DisplayOrder)
            .Select(c => c.Snapshot)
            .ToList();
    }

    private static RawSensorReading? SelectBestSensor(
        IReadOnlyList<RawSensorReading> readings,
        SensorKind kind,
        string[] namePreferences)
    {
        var matchingKind = readings.Where(r => r.Kind == kind && r.Value.HasValue).ToList();
        if (matchingKind.Count == 0)
            return null;

        // Rank by preferred name keywords
        foreach (var pref in namePreferences)
        {
            var match = matchingKind.FirstOrDefault(r =>
                r.SensorName.Contains(pref, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                return match;
        }

        // Fallback to first available sensor of the matching kind
        return matchingKind.FirstOrDefault();
    }

    private static bool IsValidPercent(float? val) =>
        val.HasValue && !float.IsNaN(val.Value) && !float.IsInfinity(val.Value) && val.Value >= 0f && val.Value <= 100.5f;

    private static bool IsValidTemp(float? val) =>
        val.HasValue && !float.IsNaN(val.Value) && !float.IsInfinity(val.Value) && val.Value > 0f && val.Value < 150f;

    private static string SanitizeHardwareId(string hwId, int fallbackIndex)
    {
        if (string.IsNullOrWhiteSpace(hwId))
            return fallbackIndex.ToString();

        var clean = hwId.Trim('/').Replace('/', '-').ToLowerInvariant();
        return string.IsNullOrWhiteSpace(clean) ? fallbackIndex.ToString() : clean;
    }

    private static bool SnapshotsEqual(IReadOnlyList<DynamicItemSnapshot> a, IReadOnlyList<DynamicItemSnapshot> b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Count != b.Count) return false;

        for (int i = 0; i < a.Count; i++)
        {
            var itemA = a[i];
            var itemB = b[i];

            if (itemA.Id != itemB.Id) return false;
            if (Math.Abs(itemA.Value - itemB.Value) > 0.01) return false;
            if (itemA.Label != itemB.Label) return false;
            if (itemA.Unit != itemB.Unit) return false;
        }

        return true;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;

            _timer?.Dispose();
            _backend.Dispose();
            _currentItems = Array.Empty<DynamicItemSnapshot>();
        }
    }

    private sealed record PrioritizedSensor(int Priority, int DisplayOrder, DynamicItemSnapshot Snapshot);
}
