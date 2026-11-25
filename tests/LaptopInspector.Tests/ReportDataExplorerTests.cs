using System.Text.Json.Nodes;
using LaptopInspector.Reporting;
using Xunit;

namespace LaptopInspector.Tests;

public sealed class ReportDataExplorerTests
{
    [Fact]
    public void SelectAll_ExpandsWildcardArrays()
    {
        JsonNode root = JsonNode.Parse("""
        { "data": { "native.storage": { "physicalDrives": [ { "nvmeHealth": { "percentageUsed": 10 } }, { "nvmeHealth": { "percentageUsed": 20 } } ] } } }
        """)!;

        IReadOnlyList<JsonNode?> nodes = ReportDataExplorer.SelectAll(root, new[] { "data", "native.storage", "physicalDrives", "*", "nvmeHealth", "percentageUsed" });

        Assert.Equal(2, nodes.Count);
        Assert.Equal(20d, ReportDataExplorer.Number(nodes[1]).GetValueOrDefault());
    }

    [Fact]
    public void FlattenScalars_ReturnsNestedScalarPaths()
    {
        JsonNode root = JsonNode.Parse("""
        { "device": { "name": "SSD", "health": { "score": 99 } } }
        """)!;

        Dictionary<string, string> values = ReportDataExplorer.FlattenScalars(root).ToDictionary(static item => item.Key, static item => item.Value);

        Assert.Equal("SSD", values["device.name"]);
        Assert.Equal("99", values["device.health.score"]);
    }
}
