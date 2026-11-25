using System.Text.Json.Nodes;
using LaptopInspector.Core.Abstractions;
using LaptopInspector.Core.Models;
using LaptopInspector.Core.Services;

namespace LaptopInspector.Collectors;

public sealed class ScanConfigurationCollector : ICollector
{
    public string Id => "scan.configuration";

    public string Category => "Collection";

    public CollectorPriority Priority => CollectorPriority.Critical;

    public bool RequiresAdmin => false;

    public TimeSpan Timeout => TimeSpan.FromSeconds(3);

    public IReadOnlyList<string> DependsOn => Array.Empty<string>();

    public Task<CollectorResult> CollectAsync(CollectionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ScanOptions options = context.Options;

        JsonObject data = new()
        {
            ["runId"] = context.RunId,
            ["startedAt"] = context.StartedAt.ToString("O"),
            ["outputDirectory"] = options.OutputDirectory,
            ["formats"] = ToJsonArray(options.Formats),
            ["include"] = ToJsonArray(options.Include),
            ["exclude"] = ToJsonArray(options.Exclude),
            ["days"] = options.Days,
            ["benchmark"] = options.Benchmark,
            ["energy"] = options.Energy,
            ["anonymize"] = options.Anonymize,
            ["history"] = options.History,
            ["deepDisk"] = options.DeepDisk,
            ["online"] = options.Online,
            ["raw"] = options.Raw,
            ["language"] = options.Language,
            ["globalTimeoutSeconds"] = (long)options.GlobalTimeout.TotalSeconds,
            ["parallelism"] = options.Parallelism,
            ["quickMode"] = options.QuickMode
        };

        return Task.FromResult(CollectorResult.Success(Id, TimeSpan.Zero, data));
    }

    private static JsonArray ToJsonArray(IEnumerable<string> values)
    {
        JsonArray array = new();
        foreach (string value in values)
        {
            array.Add(value);
        }

        return array;
    }
}
