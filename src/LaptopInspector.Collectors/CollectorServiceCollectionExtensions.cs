using LaptopInspector.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LaptopInspector.Collectors;

public static class CollectorServiceCollectionExtensions
{
    public static IServiceCollection AddLaptopInspectorCollectors(this IServiceCollection services)
    {
        services.AddSingleton<ICollector, RuntimeEnvironmentCollector>();
        services.AddSingleton<ICollector, ScanConfigurationCollector>();
        services.AddSingleton<ICollector, NativeAbiCollector>();

        services.AddSingleton<ICollector, NativeFirmwareCollector>();
        services.AddSingleton<ICollector, NativeDeviceTreeCollector>();
        services.AddSingleton<ICollector, NativeStorageCollector>();
        services.AddSingleton<ICollector, NativeBatteryCollector>();
        services.AddSingleton<ICollector, NativeUsbCollector>();
        services.AddSingleton<ICollector, NativeDisplayCollector>();
        services.AddSingleton<ICollector, NativeWlanCollector>();
        services.AddSingleton<ICollector, NativeTpmCollector>();
        services.AddSingleton<ICollector, NativeSensorsCollector>();
        services.AddSingleton<ICollector, NativeGpuVendorCollector>();

        services.AddSingleton<ICollector, SystemIdentificationCollector>();
        services.AddSingleton<ICollector, ProcessorCollector>();
        services.AddSingleton<ICollector, MemoryCollector>();
        services.AddSingleton<ICollector, MotherboardBusCollector>();
        services.AddSingleton<ICollector, OperatingSystemCollector>();
        services.AddSingleton<ICollector, SoftwareInventoryCollector>();
        services.AddSingleton<ICollector, SecurityCollector>();
        services.AddSingleton<ICollector, NetworkCollector>();
        services.AddSingleton<ICollector, PowerThermalCollector>();
        services.AddSingleton<ICollector, AudioCameraInputCollector>();
        services.AddSingleton<ICollector, EventLogStabilityCollector>();
        services.AddSingleton<ICollector, PerformanceSnapshotCollector>();
        services.AddSingleton<ICollector, VirtualizationCollector>();
        services.AddSingleton<ICollector, SyntheticBenchmarkCollector>();
        services.AddSingleton<ICollector, PowerEnergyCollector>();
        services.AddSingleton<ICollector, DeepDiskCollector>();
        services.AddSingleton<ICollector, OptionalDriverProbeCollector>();
        services.AddSingleton<ICollector, CollectionCoverageCollector>();
        return services;
    }
}
