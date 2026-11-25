using LaptopInspector.Core.Models;
using LaptopInspector.Core.Services;

namespace LaptopInspector.Core.Abstractions;

public interface ICollector
{
    string Id { get; }

    string Category { get; }

    CollectorPriority Priority { get; }

    bool RequiresAdmin { get; }

    TimeSpan Timeout { get; }

    IReadOnlyList<string> DependsOn { get; }

    Task<CollectorResult> CollectAsync(CollectionContext context, CancellationToken cancellationToken);
}
