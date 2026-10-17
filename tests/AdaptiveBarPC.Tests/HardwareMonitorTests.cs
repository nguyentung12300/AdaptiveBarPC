using AdaptiveBarPC.Models;
using AdaptiveBarPC.Services.HardwareMonitor;
using AdaptiveBarPC.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace AdaptiveBarPC.Tests;

public class FakeHardwareMonitorBackend : IHardwareMonitorBackend
{
    public bool ShouldFailInitialize { get; set; }
    public bool ThrowOnInitialize { get; set; }
    public List<RawSensorReading> Readings { get; set; } = new();
    public int SampleCount { get; private set; }
    public bool IsDisposed { get; private set; }

    public bool Initialize()
    {
        if (ThrowOnInitialize)
            throw new InvalidOperationException("Failed to open hardware backend.");
        return !ShouldFailInitialize;
    }

    public IReadOnlyList<RawSensorReading> Sample()
    {
        SampleCount++;
        return Readings.ToList();
    }

    public void Dispose()
    {
        IsDisposed = true;
    }
}

public class HardwareMonitorTests
{
    private readonly ITestOutputHelper _output;

    public HardwareMonitorTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void LiveHardwareDiscovery_OnLocalMachine()
    {
        using var backend = new LibreHardwareMonitorBackend();
        bool initialized = backend.Initialize();
        Assert.True(initialized);

        var readings = backend.Sample();
        Assert.NotEmpty(readings);

        _output.WriteLine($"Sampled {readings.Count} raw readings from physical hardware:");
        foreach (var r in readings)
        {
            _output.WriteLine($"[{r.Category}] {r.HardwareName} ({r.HardwareId}) -> {r.SensorName} ({r.Kind}) = {r.Value}");
        }

        var curated = HardwareSensorProvider.BuildCuratedSnapshot(readings);
        _output.WriteLine($"\nCurated items published to AdaptiveBar ({curated.Count}):");
        foreach (var item in curated)
        {
            _output.WriteLine($"ID: {item.Id} | Label: {item.Label} | Value: {item.Value}{item.Unit}");
        }
    }
    [Fact]
    public void CpuLoadNormalization_SelectsAndNormalizesCorrectly()
    {
        var readings = new List<RawSensorReading>
        {
            new("/intelcpu/0", "Intel Core i7", HardwareCategory.Cpu, SensorKind.Load, "CPU Total", 27.48f)
        };

        var snapshots = HardwareSensorProvider.BuildCuratedSnapshot(readings);

        Assert.Single(snapshots);
        var cpu = snapshots[0];
        Assert.Equal("hardware:cpu:load", cpu.Id);
        Assert.Equal(WidgetTypes.Sensor, cpu.Type);
        Assert.Equal("CPU", cpu.Label);
        Assert.Equal(27.0, cpu.Value);
        Assert.Equal("%", cpu.Unit);
        Assert.Equal("{0:F0}", cpu.Format);

        // Test with SensorItemViewModel formatting
        var vm = new SensorItemViewModel
        {
            Label = cpu.Label,
            Value = cpu.Value,
            Unit = cpu.Unit ?? "",
            Format = cpu.Format ?? ""
        };
        Assert.Equal("27%", vm.ValueText);
    }

    [Fact]
    public void CpuTemperatureSelection_PrefersPackageOverCore()
    {
        var readings = new List<RawSensorReading>
        {
            new("/intelcpu/0", "Intel Core i7", HardwareCategory.Cpu, SensorKind.Temperature, "CPU Core #1", 52.0f),
            new("/intelcpu/0", "Intel Core i7", HardwareCategory.Cpu, SensorKind.Temperature, "CPU Package", 58.2f),
            new("/intelcpu/0", "Intel Core i7", HardwareCategory.Cpu, SensorKind.Temperature, "CPU Core #2", 50.0f)
        };

        var snapshots = HardwareSensorProvider.BuildCuratedSnapshot(readings);

        Assert.Single(snapshots);
        var temp = snapshots[0];
        Assert.Equal("hardware:cpu:temperature", temp.Id);
        Assert.Equal("CPU", temp.Label);
        Assert.Equal(58.0, temp.Value);
        Assert.Equal("°C", temp.Unit);

        var vm = new SensorItemViewModel
        {
            Label = temp.Label,
            Value = temp.Value,
            Unit = temp.Unit ?? "",
            Format = temp.Format ?? ""
        };
        Assert.Equal("58°C", vm.ValueText);
    }

    [Fact]
    public void GpuLoadSelection_PrefersCoreOrTotalLoad()
    {
        var readings = new List<RawSensorReading>
        {
            new("/gpu-nvidia/0", "NVIDIA RTX 3070", HardwareCategory.Gpu, SensorKind.Load, "GPU Memory", 35.0f),
            new("/gpu-nvidia/0", "NVIDIA RTX 3070", HardwareCategory.Gpu, SensorKind.Load, "GPU Core", 63.4f)
        };

        var snapshots = HardwareSensorProvider.BuildCuratedSnapshot(readings);

        Assert.Single(snapshots);
        var gpuLoad = snapshots[0];
        Assert.Equal("hardware:gpu:gpu-nvidia-0:load", gpuLoad.Id);
        Assert.Equal("GPU", gpuLoad.Label);
        Assert.Equal(63.0, gpuLoad.Value);
        Assert.Equal("%", gpuLoad.Unit);
    }

    [Fact]
    public void GpuTemperatureSelection_PrefersCoreTemperature()
    {
        var readings = new List<RawSensorReading>
        {
            new("/gpu-nvidia/0", "NVIDIA RTX 3070", HardwareCategory.Gpu, SensorKind.Temperature, "GPU Hot Spot", 82.0f),
            new("/gpu-nvidia/0", "NVIDIA RTX 3070", HardwareCategory.Gpu, SensorKind.Temperature, "GPU Core", 71.1f)
        };

        var snapshots = HardwareSensorProvider.BuildCuratedSnapshot(readings);

        Assert.Single(snapshots);
        var gpuTemp = snapshots[0];
        Assert.Equal("hardware:gpu:gpu-nvidia-0:temperature", gpuTemp.Id);
        Assert.Equal("GPU", gpuTemp.Label);
        Assert.Equal(71.0, gpuTemp.Value);
        Assert.Equal("°C", gpuTemp.Unit);
    }

    [Fact]
    public void RamLoadSelection_SelectsMemoryLoad()
    {
        var readings = new List<RawSensorReading>
        {
            new("/ram", "Total Memory", HardwareCategory.Memory, SensorKind.Load, "Memory", 54.2f)
        };

        var snapshots = HardwareSensorProvider.BuildCuratedSnapshot(readings);

        Assert.Single(snapshots);
        var ram = snapshots[0];
        Assert.Equal("hardware:memory:load", ram.Id);
        Assert.Equal("RAM", ram.Label);
        Assert.Equal(54.0, ram.Value);
        Assert.Equal("%", ram.Unit);
    }

    [Fact]
    public void NullSensorValue_OmittedGracefully()
    {
        var readings = new List<RawSensorReading>
        {
            new("/intelcpu/0", "Intel Core i7", HardwareCategory.Cpu, SensorKind.Load, "CPU Total", null),
            new("/intelcpu/0", "Intel Core i7", HardwareCategory.Cpu, SensorKind.Temperature, "CPU Package", null),
            new("/ram", "Total Memory", HardwareCategory.Memory, SensorKind.Load, "Memory", 60.0f)
        };

        var snapshots = HardwareSensorProvider.BuildCuratedSnapshot(readings);

        // CPU load and temp had null values, so only RAM should be present
        Assert.Single(snapshots);
        Assert.Equal("hardware:memory:load", snapshots[0].Id);
    }

    [Fact]
    public void InvalidValues_NaN_Infinity_NegativeTemp_OmittedGracefully()
    {
        var readings = new List<RawSensorReading>
        {
            new("/intelcpu/0", "Intel Core i7", HardwareCategory.Cpu, SensorKind.Load, "CPU Total", float.NaN),
            new("/intelcpu/0", "Intel Core i7", HardwareCategory.Cpu, SensorKind.Temperature, "CPU Package", float.PositiveInfinity),
            new("/gpu-nvidia/0", "GPU", HardwareCategory.Gpu, SensorKind.Temperature, "GPU Core", -10.0f), // nonsensical temp
            new("/ram", "Total Memory", HardwareCategory.Memory, SensorKind.Load, "Memory", 150.0f) // > 100.5%
        };

        var snapshots = HardwareSensorProvider.BuildCuratedSnapshot(readings);

        // All invalid values must be omitted
        Assert.Empty(snapshots);
    }

    [Fact]
    public void StableIds_AcrossUpdates()
    {
        var readings1 = new List<RawSensorReading>
        {
            new("/intelcpu/0", "CPU", HardwareCategory.Cpu, SensorKind.Load, "CPU Total", 20f),
            new("/gpu-nvidia/0", "GPU", HardwareCategory.Gpu, SensorKind.Load, "GPU Core", 40f),
            new("/ram", "RAM", HardwareCategory.Memory, SensorKind.Load, "Memory", 50f)
        };

        var readings2 = new List<RawSensorReading>
        {
            new("/intelcpu/0", "CPU", HardwareCategory.Cpu, SensorKind.Load, "CPU Total", 65f),
            new("/gpu-nvidia/0", "GPU", HardwareCategory.Gpu, SensorKind.Load, "GPU Core", 80f),
            new("/ram", "RAM", HardwareCategory.Memory, SensorKind.Load, "Memory", 55f)
        };

        var snapshots1 = HardwareSensorProvider.BuildCuratedSnapshot(readings1);
        var snapshots2 = HardwareSensorProvider.BuildCuratedSnapshot(readings2);

        Assert.Equal(3, snapshots1.Count);
        Assert.Equal(3, snapshots2.Count);

        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(snapshots1[i].Id, snapshots2[i].Id);
            Assert.NotEqual(snapshots1[i].Value, snapshots2[i].Value);
        }
    }

    [Fact]
    public void MultipleGpus_ProduceUniqueIdsAndDisambiguatedLabels()
    {
        var readings = new List<RawSensorReading>
        {
            new("/gpu-nvidia/0", "NVIDIA RTX 3070", HardwareCategory.Gpu, SensorKind.Load, "GPU Core", 63f),
            new("/gpu-nvidia/0", "NVIDIA RTX 3070", HardwareCategory.Gpu, SensorKind.Temperature, "GPU Core", 71f),
            new("/gpu-intel/0", "Intel UHD Graphics", HardwareCategory.Gpu, SensorKind.Load, "GPU Core", 12f),
            new("/gpu-intel/0", "Intel UHD Graphics", HardwareCategory.Gpu, SensorKind.Temperature, "GPU Core", 45f)
        };

        var snapshots = HardwareSensorProvider.BuildCuratedSnapshot(readings);

        Assert.Equal(4, snapshots.Count);

        var ids = snapshots.Select(s => s.Id).ToList();
        Assert.Equal(ids.Distinct().Count(), ids.Count);

        // Disambiguated labels for multi-GPU
        var labels = snapshots.Select(s => s.Label).Distinct().ToList();
        Assert.Contains("GPU 1", labels);
        Assert.Contains("GPU 2", labels);
    }

    [Fact]
    public void SingleGpu_UsesCompactLabelWithoutIndex()
    {
        var readings = new List<RawSensorReading>
        {
            new("/gpu-nvidia/0", "NVIDIA RTX 3070", HardwareCategory.Gpu, SensorKind.Load, "GPU Core", 63f),
            new("/gpu-nvidia/0", "NVIDIA RTX 3070", HardwareCategory.Gpu, SensorKind.Temperature, "GPU Core", 71f)
        };

        var snapshots = HardwareSensorProvider.BuildCuratedSnapshot(readings);

        Assert.Equal(2, snapshots.Count);
        Assert.All(snapshots, s => Assert.Equal("GPU", s.Label));
    }

    [Fact]
    public void MonotonicRevisions_StartingAtOne()
    {
        var fake = new FakeHardwareMonitorBackend();
        fake.Readings = new List<RawSensorReading>
        {
            new("/intelcpu/0", "CPU", HardwareCategory.Cpu, SensorKind.Load, "CPU Total", 25f)
        };

        using var provider = new HardwareSensorProvider(fake, sampleIntervalMs: 5000);

        // Initial revision must start at 1
        Assert.Equal(1, provider.CurrentRevision);

        // Update readings and trigger sample
        fake.Readings = new List<RawSensorReading>
        {
            new("/intelcpu/0", "CPU", HardwareCategory.Cpu, SensorKind.Load, "CPU Total", 35f)
        };
        provider.SampleAndPublish();

        Assert.Equal(2, provider.CurrentRevision);

        // Another update
        fake.Readings = new List<RawSensorReading>
        {
            new("/intelcpu/0", "CPU", HardwareCategory.Cpu, SensorKind.Load, "CPU Total", 45f)
        };
        provider.SampleAndPublish();

        Assert.Equal(3, provider.CurrentRevision);
    }

    [Fact]
    public void UnchangedSensorIdentity_UpdatesExistingDynamicListItemInPlace()
    {
        var fake = new FakeHardwareMonitorBackend();
        fake.Readings = new List<RawSensorReading>
        {
            new("/intelcpu/0", "CPU", HardwareCategory.Cpu, SensorKind.Load, "CPU Total", 27f),
            new("/ram", "RAM", HardwareCategory.Memory, SensorKind.Load, "Memory", 54f)
        };

        using var provider = new HardwareSensorProvider(fake, sampleIntervalMs: 5000);
        using var vm = new DynamicListItemViewModel(provider);

        Assert.Equal(2, vm.Items.Count);
        var initialCpuItem = vm.Items[0] as SensorItemViewModel;
        var initialRamItem = vm.Items[1] as SensorItemViewModel;

        Assert.NotNull(initialCpuItem);
        Assert.NotNull(initialRamItem);
        Assert.Equal(27.0, initialCpuItem.Value);
        Assert.Equal("27%", initialCpuItem.ValueText);

        // Now change readings and publish update
        fake.Readings = new List<RawSensorReading>
        {
            new("/intelcpu/0", "CPU", HardwareCategory.Cpu, SensorKind.Load, "CPU Total", 42f),
            new("/ram", "RAM", HardwareCategory.Memory, SensorKind.Load, "Memory", 60f)
        };
        provider.SampleAndPublish();

        // Items should be updated IN PLACE
        Assert.Equal(2, vm.Items.Count);
        Assert.Same(initialCpuItem, vm.Items[0]);
        Assert.Same(initialRamItem, vm.Items[1]);
        Assert.Equal(42.0, initialCpuItem.Value);
        Assert.Equal("42%", initialCpuItem.ValueText);
        Assert.Equal(60.0, initialRamItem.Value);
        Assert.Equal("60%", initialRamItem.ValueText);
    }

    [Fact]
    public void MissingSensor_RemovesCorrespondingDynamicItem()
    {
        var fake = new FakeHardwareMonitorBackend();
        fake.Readings = new List<RawSensorReading>
        {
            new("/intelcpu/0", "CPU", HardwareCategory.Cpu, SensorKind.Load, "CPU Total", 27f),
            new("/gpu-nvidia/0", "GPU", HardwareCategory.Gpu, SensorKind.Load, "GPU Core", 63f),
            new("/ram", "RAM", HardwareCategory.Memory, SensorKind.Load, "Memory", 54f)
        };

        using var provider = new HardwareSensorProvider(fake, sampleIntervalMs: 5000);
        using var vm = new DynamicListItemViewModel(provider);

        Assert.Equal(3, vm.Items.Count);

        // GPU disappears
        fake.Readings = new List<RawSensorReading>
        {
            new("/intelcpu/0", "CPU", HardwareCategory.Cpu, SensorKind.Load, "CPU Total", 27f),
            new("/ram", "RAM", HardwareCategory.Memory, SensorKind.Load, "Memory", 54f)
        };
        provider.SampleAndPublish();

        Assert.Equal(2, vm.Items.Count);
        Assert.Equal("hardware:cpu:load", vm.Items[0].Id);
        Assert.Equal("hardware:memory:load", vm.Items[1].Id);
    }

    [Fact]
    public void MaxItems_AndPriorityBehavior_PrioritizesCorrectly()
    {
        var readings = new List<RawSensorReading>
        {
            // Priority 1
            new("/intelcpu/0", "CPU", HardwareCategory.Cpu, SensorKind.Load, "CPU Total", 30f),
            // Priority 4
            new("/intelcpu/0", "CPU", HardwareCategory.Cpu, SensorKind.Temperature, "CPU Package", 60f),
            // Priority 2
            new("/gpu-nvidia/0", "GPU", HardwareCategory.Gpu, SensorKind.Load, "GPU Core", 70f),
            // Priority 3
            new("/gpu-nvidia/0", "GPU", HardwareCategory.Gpu, SensorKind.Temperature, "GPU Core", 75f),
            // Priority 5
            new("/ram", "RAM", HardwareCategory.Memory, SensorKind.Load, "Memory", 50f)
        };

        // If maxSensors is 3:
        // Prioritized: CPU load (1), GPU load (2), GPU temp (3).
        // Dropped: CPU temp (4), RAM load (5).
        var snapshots = HardwareSensorProvider.BuildCuratedSnapshot(readings, maxSensors: 3);

        Assert.Equal(3, snapshots.Count);
        var ids = snapshots.Select(s => s.Id).ToList();

        Assert.Contains("hardware:cpu:load", ids);
        Assert.Contains("hardware:gpu:gpu-nvidia-0:load", ids);
        Assert.Contains("hardware:gpu:gpu-nvidia-0:temperature", ids);

        Assert.DoesNotContain("hardware:cpu:temperature", ids);
        Assert.DoesNotContain("hardware:memory:load", ids);

        // Display order remains natural: CPU -> GPU
        Assert.Equal("hardware:cpu:load", snapshots[0].Id);
        Assert.Equal("hardware:gpu:gpu-nvidia-0:load", snapshots[1].Id);
        Assert.Equal("hardware:gpu:gpu-nvidia-0:temperature", snapshots[2].Id);
    }

    [Fact]
    public void ProviderDisposal_StopsFurtherEventsAndDisposesBackend()
    {
        var fake = new FakeHardwareMonitorBackend();
        fake.Readings = new List<RawSensorReading>
        {
            new("/intelcpu/0", "CPU", HardwareCategory.Cpu, SensorKind.Load, "CPU Total", 20f)
        };

        var provider = new HardwareSensorProvider(fake, sampleIntervalMs: 5000);
        int eventsFired = 0;
        provider.ItemsChanged += (_, _) => eventsFired++;

        Assert.False(provider.IsDisposed);
        Assert.False(fake.IsDisposed);

        provider.Dispose();

        Assert.True(provider.IsDisposed);
        Assert.True(fake.IsDisposed);
        Assert.Empty(provider.CurrentItems);

        // Attempting to sample after disposal must do nothing
        provider.SampleAndPublish();
        Assert.Equal(0, eventsFired);
    }

    [Fact]
    public void InitializationFailure_DegradesGracefully()
    {
        var failingBackend = new FakeHardwareMonitorBackend { ShouldFailInitialize = true };
        using var provider = new HardwareSensorProvider(failingBackend, sampleIntervalMs: 5000);

        Assert.Empty(provider.CurrentItems);
        Assert.Equal(0, provider.CurrentRevision);

        // Also test exception on initialize
        var throwingBackend = new FakeHardwareMonitorBackend { ThrowOnInitialize = true };
        using var provider2 = new HardwareSensorProvider(throwingBackend, sampleIntervalMs: 5000);

        Assert.Empty(provider2.CurrentItems);
        Assert.Equal(0, provider2.CurrentRevision);
    }
}
