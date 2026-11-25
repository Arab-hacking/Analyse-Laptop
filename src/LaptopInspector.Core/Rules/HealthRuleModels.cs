using LaptopInspector.Core.Models;

namespace LaptopInspector.Core.Rules;

public sealed record HealthRuleDefinition
{
    public string Id { get; init; } = string.Empty;

    public bool Enabled { get; init; } = true;

    public Severity Severity { get; init; } = Severity.Info;

    public string ScoreBucket { get; init; } = "hardware";

    public int? Penalty { get; init; }

    public string Category { get; init; } = "General";

    public string Title { get; init; } = string.Empty;

    public string Details { get; init; } = string.Empty;

    public string Recommendation { get; init; } = string.Empty;

    public HealthRuleCondition Condition { get; init; } = new();
}

public sealed record HealthRuleCondition
{
    public string Source { get; init; } = "data";

    public IReadOnlyList<string> Path { get; init; } = Array.Empty<string>();

    public string Operator { get; init; } = "exists";

    public double? Number { get; init; }

    public string? Text { get; init; }

    public bool? Boolean { get; init; }

    public IReadOnlyList<string> Values { get; init; } = Array.Empty<string>();

    public string? CollectorIdPattern { get; init; }

    public IReadOnlyList<string> Statuses { get; init; } = Array.Empty<string>();
}

public sealed record HealthEvaluation(
    IReadOnlyList<Finding> Findings,
    ReportSummary Summary,
    IReadOnlyDictionary<string, int> RulePenaltiesByBucket,
    IReadOnlyList<string> RuleLoadErrors);
