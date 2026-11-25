using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using LaptopInspector.Core.Models;

namespace LaptopInspector.Reporting.Writers;

public sealed class CsvReportWriter : IReportWriter
{
    public string Format => "csv";

    public string DefaultExtension => ".zip";

    public Task<ReportWriteResult> WriteAsync(
        InspectionReport report,
        string outputDirectory,
        string baseFileName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = Path.Combine(outputDirectory, baseFileName + "_csv" + DefaultExtension);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        using FileStream stream = File.Create(path);
        using ZipArchive archive = new(stream, ZipArchiveMode.Create);
        WriteCollectors(archive, report);
        WriteFindings(archive, report);
        WriteScores(archive, report);
        WriteDataSections(archive, report);
        WriteGenericArray(archive, report, "software_programs.csv", "software.inventory", "installedPrograms");
        WriteGenericArray(archive, report, "software_services.csv", "software.inventory", "services");
        WriteGenericArray(archive, report, "software_processes.csv", "software.inventory", "processes");
        WriteGenericArray(archive, report, "devices.csv", "native.deviceTree", "devices");
        WriteGenericArray(archive, report, "usb_devices.csv", "native.usb", "devices");
        WriteGenericArray(archive, report, "storage_physical_drives.csv", "native.storage", "physicalDrives");
        WriteGenericArray(archive, report, "battery.csv", "native.battery", "batteries");
        WriteGenericArray(archive, report, "events_bugchecks.csv", "stability.events", "bugChecks");
        WriteGenericArray(archive, report, "events_whea.csv", "stability.events", "wheaLogger");
        WriteGenericArray(archive, report, "events_disk.csv", "stability.events", "diskAndFileSystemErrors");
        WriteGenericArray(archive, report, "network_adapters.csv", "network.overview", "adapters");
        return Task.FromResult(new ReportWriteResult(Format, path));
    }

    private static void WriteCollectors(ZipArchive archive, InspectionReport report)
    {
        StringBuilder builder = new();
        builder.AppendLine("id,status,durationMs,skipReason,errors");
        foreach (CollectorResult collector in report.Collectors.OrderBy(static item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(Csv(collector.Id)).Append(',');
            builder.Append(Csv(collector.Status.ToString())).Append(',');
            builder.Append(collector.Duration.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture)).Append(',');
            builder.Append(Csv(collector.SkipReason ?? string.Empty)).Append(',');
            builder.AppendLine(Csv(string.Join("; ", collector.Errors)));
        }

        WriteEntry(archive, "collectors.csv", builder.ToString());
    }

    private static void WriteFindings(ZipArchive archive, InspectionReport report)
    {
        StringBuilder builder = new();
        builder.AppendLine("ruleId,severity,category,title,details,recommendation,evidence");
        foreach (Finding finding in report.Findings)
        {
            builder.Append(Csv(finding.RuleId)).Append(',');
            builder.Append(Csv(finding.Severity.ToString())).Append(',');
            builder.Append(Csv(finding.Category)).Append(',');
            builder.Append(Csv(finding.Title)).Append(',');
            builder.Append(Csv(finding.Details)).Append(',');
            builder.Append(Csv(finding.Recommendation)).Append(',');
            builder.AppendLine(Csv(finding.Evidence));
        }

        WriteEntry(archive, "findings.csv", builder.ToString());
    }

    private static void WriteScores(ZipArchive archive, InspectionReport report)
    {
        StringBuilder builder = new();
        builder.AppendLine("metric,value");
        builder.AppendLine("hardwareHealthScore," + report.Summary.HardwareHealthScore.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("securityScore," + report.Summary.SecurityScore.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("stabilityScore," + report.Summary.StabilityScore.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("overallScore," + report.Summary.OverallScore.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("verdict," + Csv(report.Summary.Verdict));
        foreach (KeyValuePair<string, int> penalty in report.Summary.PenaltiesByCategory.OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(Csv("penalty." + penalty.Key)).Append(',').AppendLine(penalty.Value.ToString(CultureInfo.InvariantCulture));
        }

        WriteEntry(archive, "scores.csv", builder.ToString());
    }

    private static void WriteDataSections(ZipArchive archive, InspectionReport report)
    {
        StringBuilder builder = new();
        builder.AppendLine("section,type,summary");
        foreach (KeyValuePair<string, JsonNode?> section in report.Data.OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            string type = section.Value switch
            {
                JsonObject => "object",
                JsonArray => "array",
                null => "null",
                _ => "value"
            };
            string summary = section.Value switch
            {
                JsonObject obj => obj.Count.ToString(CultureInfo.InvariantCulture) + " fields",
                JsonArray array => array.Count.ToString(CultureInfo.InvariantCulture) + " items",
                null => "null",
                _ => ReportDataExplorer.Text(section.Value)
            };
            builder.Append(Csv(section.Key)).Append(',').Append(Csv(type)).Append(',').AppendLine(Csv(summary));
        }

        WriteEntry(archive, "data_sections.csv", builder.ToString());
    }

    private static void WriteGenericArray(ZipArchive archive, InspectionReport report, string fileName, params string[] path)
    {
        JsonNode? node = ReportDataExplorer.Select(report.Data, path);
        if (node is not JsonArray array || array.Count == 0)
        {
            WriteEntry(archive, fileName, "status,message\nempty,No data\n");
            return;
        }

        List<Dictionary<string, string>> rows = new();
        SortedSet<string> columns = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonNode? item in array)
        {
            Dictionary<string, string> row = new(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> scalar in ReportDataExplorer.FlattenScalars(item))
            {
                if (scalar.Key.Count(static character => character == '.') > 4)
                {
                    continue;
                }

                row[scalar.Key] = scalar.Value;
                columns.Add(scalar.Key);
            }

            rows.Add(row);
        }

        if (columns.Count == 0)
        {
            WriteEntry(archive, fileName, "status,message\nempty,No scalar columns\n");
            return;
        }

        StringBuilder builder = new();
        builder.AppendLine(string.Join(',', columns.Select(Csv)));
        foreach (Dictionary<string, string> row in rows)
        {
            builder.AppendLine(string.Join(',', columns.Select(column => Csv(row.TryGetValue(column, out string? value) ? value : string.Empty))));
        }

        WriteEntry(archive, fileName, builder.ToString());
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using Stream entryStream = entry.Open();
        using StreamWriter writer = new(entryStream, new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string Csv(string value)
    {
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal).ReplaceLineEndings(" ") + "\"";
    }
}
