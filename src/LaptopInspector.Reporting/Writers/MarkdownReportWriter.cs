using System.Text;
using LaptopInspector.Core.Models;

namespace LaptopInspector.Reporting.Writers;

public sealed class MarkdownReportWriter : IReportWriter
{
    public string Format => "md";

    public string DefaultExtension => ".md";

    public async Task<ReportWriteResult> WriteAsync(
        InspectionReport report,
        string outputDirectory,
        string baseFileName,
        CancellationToken cancellationToken)
    {
        string path = Path.Combine(outputDirectory, baseFileName + DefaultExtension);
        StringBuilder builder = new();
        AppendHeader(builder, report);
        AppendScores(builder, report);
        AppendFindings(builder, report);
        AppendCollectors(builder, report);
        AppendDataSections(builder, report);
        await File.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        return new ReportWriteResult(Format, path);
    }

    private static void AppendHeader(StringBuilder builder, InspectionReport report)
    {
        builder.AppendLine("# LaptopInspector — отчёт");
        builder.AppendLine();
        builder.AppendLine($"- Дата: `{report.Metadata.CreatedAt:O}`");
        builder.AppendLine($"- Версия: `{Escape(report.Metadata.GeneratorVersion)}`");
        builder.AppendLine($"- Schema: `{Escape(report.SchemaVersion)}`");
        builder.AppendLine($"- Компьютер: `{Escape(report.Metadata.ComputerName ?? "нет данных")}`");
        builder.AppendLine($"- ОС: `{Escape(report.Metadata.OperatingSystem ?? "нет данных")}`");
        builder.AppendLine($"- Режим администратора: `{report.Metadata.IsAdministrator}`");
        builder.AppendLine($"- Анонимизация: `{report.Metadata.Anonymized}`");
        builder.AppendLine($"- Длительность: `{report.Metadata.DurationMs} мс`");
        builder.AppendLine($"- SHA-256: `{Escape(report.Metadata.ReportHashSha256 ?? "см. JSON")}`");
        builder.AppendLine();
    }

    private static void AppendScores(StringBuilder builder, InspectionReport report)
    {
        builder.AppendLine("## Dashboard");
        builder.AppendLine();
        builder.AppendLine("| Метрика | Значение |");
        builder.AppendLine("|---|---:|");
        builder.AppendLine($"| Hardware Health | {report.Summary.HardwareHealthScore}/100 |");
        builder.AppendLine($"| Security | {report.Summary.SecurityScore}/100 |");
        builder.AppendLine($"| Stability | {report.Summary.StabilityScore}/100 |");
        builder.AppendLine($"| Overall | {report.Summary.OverallScore}/100 |");
        builder.AppendLine();
        builder.AppendLine($"**Вердикт:** {Escape(report.Summary.Verdict)}");
        builder.AppendLine();
        if (report.Summary.PenaltiesByCategory.Count > 0)
        {
            builder.AppendLine("### Penalties");
            builder.AppendLine();
            builder.AppendLine("| Bucket | Penalty |");
            builder.AppendLine("|---|---:|");
            foreach (KeyValuePair<string, int> penalty in report.Summary.PenaltiesByCategory.OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                builder.AppendLine($"| {Escape(penalty.Key)} | {penalty.Value} |");
            }

            builder.AppendLine();
        }
    }

    private static void AppendFindings(StringBuilder builder, InspectionReport report)
    {
        builder.AppendLine("## Проблемы и рекомендации");
        builder.AppendLine();
        Dictionary<Severity, int> counts = Enum.GetValues<Severity>().ToDictionary(static severity => severity, severity => report.Findings.Count(finding => finding.Severity == severity));
        builder.AppendLine("| Severity | Count |");
        builder.AppendLine("|---|---:|");
        foreach (Severity severity in new[] { Severity.Critical, Severity.High, Severity.Medium, Severity.Low, Severity.Info })
        {
            builder.AppendLine($"| `{severity}` | {counts[severity]} |");
        }

        builder.AppendLine();
        if (report.Findings.Count == 0)
        {
            builder.AppendLine("Находки отсутствуют.");
            builder.AppendLine();
            return;
        }

        foreach (Finding finding in report.Findings)
        {
            builder.AppendLine($"### {Escape(finding.RuleId)} — {Escape(finding.Title)}");
            builder.AppendLine();
            builder.AppendLine($"- Severity: `{finding.Severity}`");
            builder.AppendLine($"- Категория: {Escape(finding.Category)}");
            builder.AppendLine($"- Детали: {Escape(finding.Details)}");
            builder.AppendLine($"- Рекомендация: {Escape(finding.Recommendation)}");
            builder.AppendLine($"- Evidence: `{Escape(finding.Evidence)}`");
            builder.AppendLine();
        }
    }

    private static void AppendCollectors(StringBuilder builder, InspectionReport report)
    {
        builder.AppendLine("## Журнал сборщиков");
        builder.AppendLine();
        builder.AppendLine("| ID | Статус | Длительность | Ошибки |");
        builder.AppendLine("|---|---|---:|---|");
        foreach (CollectorResult collector in report.Collectors.OrderBy(static item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            string errors = collector.SkipReason ?? string.Join("; ", collector.Errors);
            builder.AppendLine($"| `{Escape(collector.Id)}` | `{collector.Status}` | {collector.Duration.TotalMilliseconds:F0} мс | {Escape(errors)} |");
        }

        builder.AppendLine();
    }

    private static void AppendDataSections(StringBuilder builder, InspectionReport report)
    {
        builder.AppendLine("## Разделы данных");
        builder.AppendLine();
        builder.AppendLine("| Раздел | Тип | Кратко |");
        builder.AppendLine("|---|---|---|");
        foreach (KeyValuePair<string, System.Text.Json.Nodes.JsonNode?> section in report.Data.OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            string type = section.Value switch
            {
                System.Text.Json.Nodes.JsonObject => "object",
                System.Text.Json.Nodes.JsonArray => "array",
                null => "null",
                _ => "value"
            };
            string summary = section.Value switch
            {
                System.Text.Json.Nodes.JsonObject obj => obj.Count + " полей",
                System.Text.Json.Nodes.JsonArray array => array.Count + " элементов",
                null => "нет данных",
                _ => ReportDataExplorer.Text(section.Value)
            };
            builder.AppendLine($"| `{Escape(section.Key)}` | {type} | {Escape(summary)} |");
        }

        builder.AppendLine();
    }

    private static string Escape(string value)
    {
        return value.Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");
    }
}
