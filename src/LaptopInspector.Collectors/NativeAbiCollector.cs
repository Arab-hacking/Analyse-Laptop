using System.Text.Json.Nodes;
using LaptopInspector.Core.Abstractions;
using LaptopInspector.Core.Models;
using LaptopInspector.Core.Services;
using LaptopInspector.Interop;

namespace LaptopInspector.Collectors;

public sealed class NativeAbiCollector : ICollector
{
    public string Id => "native.abi";

    public string Category => "Native";

    public CollectorPriority Priority => CollectorPriority.High;

    public bool RequiresAdmin => false;

    public TimeSpan Timeout => TimeSpan.FromSeconds(5);

    public IReadOnlyList<string> DependsOn => Array.Empty<string>();

    public Task<CollectorResult> CollectAsync(CollectionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        NativeProbeResult probe = NativeBridge.Probe();
        JsonObject data = new()
        {
            ["available"] = probe.Available,
            ["abiVersion"] = probe.AbiVersion,
            ["runtimeJson"] = probe.RuntimeJson,
            ["error"] = probe.Error
        };

        if (!probe.Available)
        {
            return Task.FromResult(CollectorResult.Skipped(Id, TimeSpan.Zero, probe.Error ?? "Нативная DLL недоступна."));
        }

        if (!string.IsNullOrWhiteSpace(probe.Error))
        {
            return Task.FromResult(CollectorResult.Partial(
                Id,
                TimeSpan.Zero,
                data,
                Array.Empty<Finding>(),
                new[] { probe.Error }));
        }

        return Task.FromResult(CollectorResult.Success(Id, TimeSpan.Zero, data));
    }
}
