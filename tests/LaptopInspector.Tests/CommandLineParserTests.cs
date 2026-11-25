using LaptopInspector.Cli.CommandLine;
using LaptopInspector.Core.Models;
using Xunit;

namespace LaptopInspector.Tests;

public sealed class CommandLineParserTests
{
    [Fact]
    public void Parse_DefaultsToScan()
    {
        CommandLineParser parser = new();

        ParsedCommand command = parser.Parse(Array.Empty<string>());

        Assert.Equal(CliCommandKind.Scan, command.Kind);
        Assert.Contains("html", command.ScanOptions.Formats);
        Assert.Contains("json", command.ScanOptions.Formats);
        Assert.Contains("txt", command.ScanOptions.Formats);
    }

    [Fact]
    public void Parse_QuickSetsQuickModeAndTimeout()
    {
        CommandLineParser parser = new();

        ParsedCommand command = parser.Parse(new[] { "quick", "--format", "json,md", "--anonymize" });

        Assert.Equal(CliCommandKind.Quick, command.Kind);
        Assert.True(command.ScanOptions.QuickMode);
        Assert.Equal(TimeSpan.FromSeconds(30), command.ScanOptions.GlobalTimeout);
        Assert.True(command.ScanOptions.Anonymize);
        Assert.Equal(new[] { "json", "md" }, command.ScanOptions.Formats);
    }

    [Fact]
    public void Parse_DiffRequiresTwoReports()
    {
        CommandLineParser parser = new();

        ParsedCommand command = parser.Parse(new[] { "diff", "old.json", "new.json", "--out", "diff.html" });

        Assert.Equal(CliCommandKind.Diff, command.Kind);
        Assert.NotNull(command.DiffOptions);
        Assert.EndsWith("old.json", command.DiffOptions!.OldReportPath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("new.json", command.DiffOptions.NewReportPath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("diff.html", command.DiffOptions.OutputPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_RejectsUnknownFormat()
    {
        CommandLineParser parser = new();

        Assert.Throws<CommandLineException>(() => parser.Parse(new[] { "scan", "--format", "exe" }));
    }
}
