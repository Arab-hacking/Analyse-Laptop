using LaptopInspector.Core.Abstractions;
using LaptopInspector.Core.Models;
using LaptopInspector.Core.Services;

namespace LaptopInspector.Collectors;

public abstract class WindowsPowerShellCollectorBase : ICollector
{
    public abstract string Id { get; }

    public abstract string Category { get; }

    public virtual CollectorPriority Priority => CollectorPriority.Normal;

    public virtual bool RequiresAdmin => false;

    public virtual TimeSpan Timeout => TimeSpan.FromSeconds(20);

    public virtual IReadOnlyList<string> DependsOn => Array.Empty<string>();

    protected virtual int JsonDepth => 8;

    protected abstract string BuildScript(CollectionContext context);

    protected virtual IReadOnlyList<Finding> Analyze(JsonNodeAdapter data, CollectionContext context)
    {
        return Array.Empty<Finding>();
    }

    public async Task<CollectorResult> CollectAsync(CollectionContext context, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return CollectorResult.Skipped(Id, TimeSpan.Zero, "Сборщик использует PowerShell/CIM/реестр Windows и недоступен на этой платформе.");
        }

        string command = BuildWrappedCommand(context);
        CommandExecutionResult result = await CommandRunner.RunAsync(
            "powershell.exe",
            new[]
            {
                "-NoProfile",
                "-NonInteractive",
                "-ExecutionPolicy",
                "Bypass",
                "-Command",
                command
            },
            Timeout,
            cancellationToken).ConfigureAwait(false);

        if (result.TimedOut)
        {
            return CollectorResult.TimedOut(Id, result.Duration, Timeout);
        }

        JsonNodeAdapter data = new(JsonPrivacyScrubber.Scrub(CollectorJsonUtilities.ParseOrWrap(result.StandardOutput), context));
        List<string> errors = new();
        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            errors.Add(result.StandardError);
        }

        if (result.ExitCode != 0)
        {
            errors.Add("PowerShell завершился с кодом " + result.ExitCode + ".");
        }

        IReadOnlyList<Finding> findings = Analyze(data, context);
        return CollectorJsonUtilities.ResultFromStatus(Id, result.Duration, data.Node, findings, errors);
    }

    private string BuildWrappedCommand(CollectionContext context)
    {
        string script = BuildScript(context);
        return "& { " +
            "$ErrorActionPreference = 'Continue'; " +
            "$ProgressPreference = 'SilentlyContinue'; " +
            script +
            " } | ConvertTo-Json -Depth " + JsonDepth.ToString(System.Globalization.CultureInfo.InvariantCulture) + " -Compress";
    }
}

public readonly record struct JsonNodeAdapter(System.Text.Json.Nodes.JsonNode Node)
{
    public string? ReadString(string propertyName)
    {
        return CollectorJsonUtilities.ReadString(Node, propertyName);
    }

    public bool HasStatus(string expectedStatus)
    {
        return CollectorJsonUtilities.HasStatus(Node, expectedStatus);
    }
}
