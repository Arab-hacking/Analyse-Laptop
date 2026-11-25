using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using LaptopInspector.Core.Abstractions;
using LaptopInspector.Core.Models;
using LaptopInspector.Core.Privacy;
using LaptopInspector.Core.Services;

namespace LaptopInspector.Collectors;

public sealed class RuntimeEnvironmentCollector : ICollector
{
    public string Id => "runtime.environment";

    public string Category => "System";

    public CollectorPriority Priority => CollectorPriority.Critical;

    public bool RequiresAdmin => false;

    public TimeSpan Timeout => TimeSpan.FromSeconds(5);

    public IReadOnlyList<string> DependsOn => Array.Empty<string>();

    public Task<CollectorResult> CollectAsync(CollectionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string computerName = PrivacyMasker.MaybeMask(
            Environment.MachineName,
            context.AnonymizationSalt,
            context.Options.Anonymize,
            "computer");
        string userName = PrivacyMasker.MaybeMask(
            Environment.UserName,
            context.AnonymizationSalt,
            context.Options.Anonymize,
            "user");

        JsonObject data = new()
        {
            ["computerName"] = computerName,
            ["userName"] = userName,
            ["isAdministrator"] = context.IsAdministrator,
            ["osDescription"] = RuntimeInformation.OSDescription,
            ["osArchitecture"] = RuntimeInformation.OSArchitecture.ToString(),
            ["processArchitecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
            ["frameworkDescription"] = RuntimeInformation.FrameworkDescription,
            ["is64BitOperatingSystem"] = Environment.Is64BitOperatingSystem,
            ["is64BitProcess"] = Environment.Is64BitProcess,
            ["processorCount"] = Environment.ProcessorCount,
            ["systemDirectory"] = OperatingSystem.IsWindows() ? Environment.SystemDirectory : string.Empty,
            ["currentDirectory"] = Environment.CurrentDirectory,
            ["machineNameMasked"] = context.Options.Anonymize
        };

        return Task.FromResult(CollectorResult.Success(Id, TimeSpan.Zero, data));
    }
}
