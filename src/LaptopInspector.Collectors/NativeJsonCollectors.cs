using LaptopInspector.Core.Abstractions;
using LaptopInspector.Core.Models;
using LaptopInspector.Core.Services;
using LaptopInspector.Interop;

namespace LaptopInspector.Collectors;

public abstract class NativeJsonCollectorBase : ICollector
{
    public abstract string Id { get; }

    public abstract string Category { get; }

    public virtual CollectorPriority Priority => CollectorPriority.High;

    public virtual bool RequiresAdmin => false;

    public virtual TimeSpan Timeout => TimeSpan.FromSeconds(20);

    public virtual IReadOnlyList<string> DependsOn => Array.Empty<string>();

    protected abstract NativeCollectionKind Kind { get; }

    public Task<CollectorResult> CollectAsync(CollectionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        NativeProbeResult probe = NativeBridge.Probe();
        if (!probe.Available)
        {
            return Task.FromResult(CollectorResult.Skipped(Id, TimeSpan.Zero, probe.Error ?? "Нативная библиотека недоступна."));
        }

        string? json = NativeBridge.TryCollect(Kind, out string? error);
        if (json is null)
        {
            return Task.FromResult(CollectorResult.Failed(Id, TimeSpan.Zero, error ?? "Нативный сборщик не вернул JSON."));
        }

        System.Text.Json.Nodes.JsonNode data = JsonPrivacyScrubber.Scrub(CollectorJsonUtilities.ParseOrWrap(json), context);
        if (CollectorJsonUtilities.HasStatus(data, "unsupported"))
        {
            string reason = CollectorJsonUtilities.ReadString(data, "reason") ?? "Нативный сборщик не поддерживается на этой платформе.";
            return Task.FromResult(CollectorResult.Skipped(Id, TimeSpan.Zero, reason));
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            return Task.FromResult(CollectorResult.Partial(Id, TimeSpan.Zero, data, Array.Empty<Finding>(), new[] { error }));
        }

        return Task.FromResult(CollectorResult.Success(Id, TimeSpan.Zero, data));
    }
}

public sealed class NativeDeviceTreeCollector : NativeJsonCollectorBase
{
    public override string Id => "native.deviceTree";

    public override string Category => "Devices";

    protected override NativeCollectionKind Kind => NativeCollectionKind.DeviceTree;
}

public sealed class NativeStorageCollector : NativeJsonCollectorBase
{
    public override string Id => "native.storage";

    public override string Category => "Storage";

    public override TimeSpan Timeout => TimeSpan.FromSeconds(45);

    protected override NativeCollectionKind Kind => NativeCollectionKind.Storage;
}

public sealed class NativeBatteryCollector : NativeJsonCollectorBase
{
    public override string Id => "native.battery";

    public override string Category => "Power";

    protected override NativeCollectionKind Kind => NativeCollectionKind.Battery;
}

public sealed class NativeUsbCollector : NativeJsonCollectorBase
{
    public override string Id => "native.usb";

    public override string Category => "USB";

    protected override NativeCollectionKind Kind => NativeCollectionKind.Usb;
}

public sealed class NativeDisplayCollector : NativeJsonCollectorBase
{
    public override string Id => "native.display";

    public override string Category => "Display";

    protected override NativeCollectionKind Kind => NativeCollectionKind.Display;
}

public sealed class NativeWlanCollector : NativeJsonCollectorBase
{
    public override string Id => "native.wlan";

    public override string Category => "Network";

    protected override NativeCollectionKind Kind => NativeCollectionKind.Wlan;
}

public sealed class NativeFirmwareCollector : NativeJsonCollectorBase
{
    public override string Id => "native.firmware";

    public override string Category => "Firmware";

    public override TimeSpan Timeout => TimeSpan.FromSeconds(45);

    protected override NativeCollectionKind Kind => NativeCollectionKind.Firmware;
}

public sealed class NativeTpmCollector : NativeJsonCollectorBase
{
    public override string Id => "native.tpm";

    public override string Category => "Security";

    protected override NativeCollectionKind Kind => NativeCollectionKind.Tpm;
}

public sealed class NativeSensorsCollector : NativeJsonCollectorBase
{
    public override string Id => "native.sensors";

    public override string Category => "Sensors";

    protected override NativeCollectionKind Kind => NativeCollectionKind.Sensors;
}

public sealed class NativeGpuVendorCollector : NativeJsonCollectorBase
{
    public override string Id => "native.gpuVendor";

    public override string Category => "Display";

    public override CollectorPriority Priority => CollectorPriority.Low;

    protected override NativeCollectionKind Kind => NativeCollectionKind.GpuVendor;
}
