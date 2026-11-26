using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using LaptopInspector.Core.Models;
using LaptopInspector.Core.Rules;
using LaptopInspector.Core.Services;

namespace LaptopInspector.Reporting;

public sealed class ReportFactory
{
    private readonly HealthRulesEngine _healthRulesEngine;

    public ReportFactory(HealthRulesEngine healthRulesEngine)
    {
        _healthRulesEngine = healthRulesEngine;
    }

    public InspectionReport Create(CollectionRunResult runResult)
    {
        JsonObject data = new();
        List<Finding> collectorFindings = new();

        foreach (CollectorResult result in runResult.Results)
        {
            data[result.Id] = result.Data.DeepClone();
            collectorFindings.AddRange(result.Findings);
        }

        HealthEvaluation health = _healthRulesEngine.Evaluate(data, runResult.Results, collectorFindings);
        data["health.rules"] = new JsonObject
        {
            ["ruleLoadErrors"] = ToJsonArray(health.RuleLoadErrors),
            ["penaltiesByBucket"] = new JsonObject
            {
                ["hardware"] = health.RulePenaltiesByBucket.TryGetValue("hardware", out int hardwarePenalty) ? hardwarePenalty : 0,
                ["security"] = health.RulePenaltiesByBucket.TryGetValue("security", out int securityPenalty) ? securityPenalty : 0,
                ["stability"] = health.RulePenaltiesByBucket.TryGetValue("stability", out int stabilityPenalty) ? stabilityPenalty : 0
            },
            ["formula"] = "overall = hardware*0.45 + security*0.35 + stability*0.20; bucket score = 100 - capped penalties"
        };

        ReportMetadata metadata = CreateMetadata(runResult, data);

        InspectionReport report = new()
        {
            SchemaVersion = "1.0.0",
            Metadata = metadata,
            Summary = health.Summary,
            Collectors = runResult.Results,
            Findings = health.Findings,
            Data = data
        };

        // Hash считается один раз в фабрике, чтобы его одинаково видели все форматы отчёта
        // (JSON, HTML, TXT, Markdown), а не только JSON-writer.
        return report with { Metadata = metadata with { ReportHashSha256 = ComputeReportHash(report) } };
    }

    public static string ComputeReportHash(InspectionReport report)
    {
        InspectionReport unsigned = report with
        {
            Metadata = report.Metadata with { ReportHashSha256 = null }
        };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(unsigned, ReportSerialization.CreateOptions());
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static ReportMetadata CreateMetadata(CollectionRunResult runResult, JsonObject data)
    {
        string? computerName = null;
        if (data.TryGetPropertyValue("runtime.environment", out JsonNode? runtimeNode) &&
            runtimeNode is JsonObject runtimeObject &&
            runtimeObject.TryGetPropertyValue("computerName", out JsonNode? computerNode) &&
            computerNode is JsonValue computerValue &&
            computerValue.TryGetValue(out string? computerText))
        {
            computerName = computerText;
        }

        return new ReportMetadata
        {
            Generator = "LaptopInspector",
            GeneratorVersion = "0.8.0-stage8",
            CreatedAt = DateTimeOffset.UtcNow,
            DurationMs = (long)runResult.Duration.TotalMilliseconds,
            IsAdministrator = runResult.Context.IsAdministrator,
            Language = runResult.Context.Options.Language,
            ComputerName = computerName,
            OperatingSystem = RuntimeInformation.OSDescription,
            RequestedFormats = runResult.Context.Options.Formats,
            Anonymized = runResult.Context.Options.Anonymize
        };
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
