using System.Text.Json.Nodes;
using LaptopInspector.Core.Abstractions;
using LaptopInspector.Core.Models;
using LaptopInspector.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LaptopInspector.Tests;

public sealed class CollectionSchedulerTests
{
    [Fact]
    public async Task RunAsync_SkipsCollector_WhenDependencyFails()
    {
        ICollector failed = new DelegateCollector("a.failed", Array.Empty<string>(), _ =>
            Task.FromResult(CollectorResult.Failed("a.failed", TimeSpan.Zero, "expected")));
        ICollector dependent = new DelegateCollector("b.dependent", new[] { "a.failed" }, _ =>
            Task.FromResult(CollectorResult.Success("b.dependent", TimeSpan.Zero, new JsonObject())));
        CollectionScheduler scheduler = new(
            new[] { failed, dependent },
            new FixedPrivilegeDetector(false),
            NullLogger<CollectionScheduler>.Instance);

        CollectionRunResult result = await scheduler.RunAsync(new ScanOptions(), CancellationToken.None);

        CollectorResult skipped = Assert.Single(result.Results.Where(item => item.Id == "b.dependent"));
        Assert.Equal(CollectorStatus.Skipped, skipped.Status);
    }

    [Fact]
    public async Task RunAsync_AppliesIncludeFilter()
    {
        ICollector first = new DelegateCollector("runtime.environment", Array.Empty<string>(), _ =>
            Task.FromResult(CollectorResult.Success("runtime.environment", TimeSpan.Zero, new JsonObject())));
        ICollector second = new DelegateCollector("storage.smart", Array.Empty<string>(), _ =>
            Task.FromResult(CollectorResult.Success("storage.smart", TimeSpan.Zero, new JsonObject())));
        CollectionScheduler scheduler = new(
            new[] { first, second },
            new FixedPrivilegeDetector(false),
            NullLogger<CollectionScheduler>.Instance);

        CollectionRunResult result = await scheduler.RunAsync(
            new ScanOptions { Include = new[] { "runtime.*" } },
            CancellationToken.None);

        Assert.Single(result.Results);
        Assert.Equal("runtime.environment", result.Results[0].Id);
    }

    private sealed class FixedPrivilegeDetector : IPrivilegeDetector
    {
        private readonly bool _isAdministrator;

        public FixedPrivilegeDetector(bool isAdministrator)
        {
            _isAdministrator = isAdministrator;
        }

        public bool IsAdministrator()
        {
            return _isAdministrator;
        }
    }

    private sealed class DelegateCollector : ICollector
    {
        private readonly Func<CollectionContext, Task<CollectorResult>> _handler;

        public DelegateCollector(
            string id,
            IReadOnlyList<string> dependsOn,
            Func<CollectionContext, Task<CollectorResult>> handler)
        {
            Id = id;
            DependsOn = dependsOn;
            _handler = handler;
        }

        public string Id { get; }

        public string Category => "Tests";

        public CollectorPriority Priority => CollectorPriority.Normal;

        public bool RequiresAdmin => false;

        public TimeSpan Timeout => TimeSpan.FromSeconds(5);

        public IReadOnlyList<string> DependsOn { get; }

        public Task<CollectorResult> CollectAsync(CollectionContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _handler(context);
        }
    }
}
