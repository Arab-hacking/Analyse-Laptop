using System.Text.Json.Nodes;
using LaptopInspector.Core.Models;
using LaptopInspector.Core.Rules;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LaptopInspector.Tests;

public sealed class HealthRulesEngineTests
{
    [Fact]
    public void Evaluate_AddsCriticalFinding_ForNvmeCriticalWarning()
    {
        JsonObject data = new()
        {
            ["native.storage"] = JsonNode.Parse("""
            {
              "physicalDrives": [
                { "nvmeHealth": { "criticalWarning": 4, "percentageUsed": 10 } }
              ]
            }
            """)
        };
        HealthRulesEngine engine = new(NullLogger<HealthRulesEngine>.Instance);

        HealthEvaluation evaluation = engine.Evaluate(data, Array.Empty<CollectorResult>(), Array.Empty<Finding>());

        Assert.Contains(evaluation.Findings, finding => finding.RuleId == "STO-001" && finding.Severity == Severity.Critical);
        Assert.True(evaluation.Summary.HardwareHealthScore < 100);
    }

    [Fact]
    public void Evaluate_AddsCollectorFinding_ForFailedCollector()
    {
        CollectorResult failed = CollectorResult.Failed("native.storage", TimeSpan.FromMilliseconds(5), "expected");
        HealthRulesEngine engine = new(NullLogger<HealthRulesEngine>.Instance);

        HealthEvaluation evaluation = engine.Evaluate(new JsonObject(), new[] { failed }, Array.Empty<Finding>());

        Assert.Contains(evaluation.Findings, finding => finding.RuleId == "COL-001");
        Assert.True(evaluation.Summary.StabilityScore < 100);
    }

    [Fact]
    public void Evaluate_CalculatesSecurityPenalty_ForDisabledSecureBoot()
    {
        JsonObject data = new()
        {
            ["native.firmware"] = JsonNode.Parse("""
            { "secureBoot": { "available": true, "enabled": false } }
            """)
        };
        HealthRulesEngine engine = new(NullLogger<HealthRulesEngine>.Instance);

        HealthEvaluation evaluation = engine.Evaluate(data, Array.Empty<CollectorResult>(), Array.Empty<Finding>());

        Assert.Contains(evaluation.Findings, finding => finding.RuleId == "SEC-001");
        Assert.True(evaluation.Summary.SecurityScore < 100);
    }
}
