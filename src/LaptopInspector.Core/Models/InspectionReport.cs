using System.Text.Json.Nodes;

namespace LaptopInspector.Core.Models;

public sealed record InspectionReport
{
    public string SchemaVersion { get; init; } = "1.0.0";

    public ReportMetadata Metadata { get; init; } = new();

    public ReportSummary Summary { get; init; } = new();

    public IReadOnlyList<CollectorResult> Collectors { get; init; } = Array.Empty<CollectorResult>();

    public IReadOnlyList<Finding> Findings { get; init; } = Array.Empty<Finding>();

    public JsonObject Data { get; init; } = new();
}

public sealed record ReportMetadata
{
    public string Generator { get; init; } = "LaptopInspector";

    public string GeneratorVersion { get; init; } = "0.8.0-stage8";

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public long DurationMs { get; init; }

    public bool IsAdministrator { get; init; }

    public string Language { get; init; } = "ru";

    public string? ComputerName { get; init; }

    public string? OperatingSystem { get; init; }

    public IReadOnlyList<string> RequestedFormats { get; init; } = Array.Empty<string>();

    public bool Anonymized { get; init; }

    public string? ReportHashSha256 { get; init; }
}

public sealed record ReportSummary
{
    public int HardwareHealthScore { get; init; } = 100;

    public int SecurityScore { get; init; } = 100;

    public int StabilityScore { get; init; } = 100;

    public int OverallScore { get; init; } = 100;

    public string Verdict { get; init; } = "Отличное";

    public IReadOnlyDictionary<string, int> PenaltiesByCategory { get; init; } = new Dictionary<string, int>();
}
