using System.Text;
using LaptopInspector.Core.Models;

namespace LaptopInspector.Reporting.Writers;

public sealed class TxtReportWriter : IReportWriter
{
    public string Format => "txt";

    public string DefaultExtension => ".txt";

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
        AppendDataInventory(builder, report);
        builder.AppendLine();
        builder.AppendLine("Формула оценки: bucketScore = max(0, 100 - penalties); overall = hardware*0.45 + security*0.35 + stability*0.20");
        builder.AppendLine($"SHA-256 JSON-представления без поля hash: {report.Metadata.ReportHashSha256 ?? "будет рассчитан в JSON-отчёте"}");
        await File.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        return new ReportWriteResult(Format, path);
    }

    private static void AppendHeader(StringBuilder builder, InspectionReport report)
    {
        builder.AppendLine("LaptopInspector — отчёт анализа ноутбука");
        builder.AppendLine(new string('=', 120));
        builder.AppendLine($"Дата UTC              : {report.Metadata.CreatedAt:O}");
        builder.AppendLine($"Версия генератора     : {report.Metadata.GeneratorVersion}");
        builder.AppendLine($"Schema version        : {report.SchemaVersion}");
        builder.AppendLine($"Компьютер             : {report.Metadata.ComputerName ?? "нет данных"}");
        builder.AppendLine($"ОС                    : {report.Metadata.OperatingSystem ?? "нет данных"}");
        builder.AppendLine($"Администратор         : {Bool(report.Metadata.IsAdministrator)}");
        builder.AppendLine($"Анонимизация          : {Bool(report.Metadata.Anonymized)}");
        builder.AppendLine($"Длительность, мс      : {report.Metadata.DurationMs:N0}");
        builder.AppendLine($"Форматы               : {string.Join(", ", report.Metadata.RequestedFormats)}");
    }

    private static void AppendScores(StringBuilder builder, InspectionReport report)
    {
        Section(builder, "ИТОГОВЫЕ ОЦЕНКИ");
        builder.AppendLine($"Hardware Health Score : {report.Summary.HardwareHealthScore,3}/100");
        builder.AppendLine($"Security Score        : {report.Summary.SecurityScore,3}/100");
        builder.AppendLine($"Stability Score       : {report.Summary.StabilityScore,3}/100");
        builder.AppendLine($"Overall Score         : {report.Summary.OverallScore,3}/100");
        builder.AppendLine($"Вердикт               : {report.Summary.Verdict}");
        if (report.Summary.PenaltiesByCategory.Count > 0)
        {
            builder.AppendLine("Penalties             : " + string.Join(", ", report.Summary.PenaltiesByCategory.Select(static item => item.Key + "=" + item.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        }
    }

    private static void AppendFindings(StringBuilder builder, InspectionReport report)
    {
        Section(builder, "ПРОБЛЕМЫ И РЕКОМЕНДАЦИИ");
        Dictionary<Severity, int> counts = Enum.GetValues<Severity>().ToDictionary(static severity => severity, severity => report.Findings.Count(finding => finding.Severity == severity));
        builder.AppendLine("Severity summary      : " + string.Join(", ", new[] { Severity.Critical, Severity.High, Severity.Medium, Severity.Low, Severity.Info }.Select(severity => severity + "=" + counts[severity].ToString(System.Globalization.CultureInfo.InvariantCulture))));
        builder.AppendLine();

        if (report.Findings.Count == 0)
        {
            builder.AppendLine("Находки отсутствуют.");
            return;
        }

        int index = 1;
        foreach (Finding finding in report.Findings)
        {
            builder.AppendLine($"{index,3}. [{finding.Severity}] {finding.RuleId} — {finding.Title}");
            builder.AppendLine(Wrap("     Категория: ", finding.Category));
            builder.AppendLine(Wrap("     Детали: ", finding.Details));
            builder.AppendLine(Wrap("     Рекомендация: ", finding.Recommendation));
            builder.AppendLine(Wrap("     Evidence: ", finding.Evidence));
            builder.AppendLine();
            index++;
        }
    }

    private static void AppendCollectors(StringBuilder builder, InspectionReport report)
    {
        Section(builder, "ЖУРНАЛ СБОРЩИКОВ");
        builder.AppendLine($"{"ID",-36} {"Статус",-10} {"мс",8} Причина/ошибка");
        builder.AppendLine(new string('-', 120));
        foreach (CollectorResult collector in report.Collectors.OrderBy(static item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            string note = collector.SkipReason ?? string.Join("; ", collector.Errors);
            builder.AppendLine($"{Trim(collector.Id, 36),-36} {collector.Status,-10} {collector.Duration.TotalMilliseconds,8:F0} {Trim(note, 58)}");
        }
    }

    private static void AppendDataInventory(StringBuilder builder, InspectionReport report)
    {
        Section(builder, "ИНВЕНТАРЬ РАЗДЕЛОВ DATA");
        builder.AppendLine($"{"Раздел",-42} {"Тип",-12} Кратко");
        builder.AppendLine(new string('-', 120));
        foreach (KeyValuePair<string, System.Text.Json.Nodes.JsonNode?> section in report.Data.OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            string type = section.Value switch
            {
                System.Text.Json.Nodes.JsonObject => "object",
                System.Text.Json.Nodes.JsonArray => "array",
                null => "null",
                _ => "value"
            };
            builder.AppendLine($"{Trim(section.Key, 42),-42} {type,-12} {Trim(Summary(section.Value), 58)}");
        }
    }

    private static string Summary(System.Text.Json.Nodes.JsonNode? node)
    {
        return node switch
        {
            System.Text.Json.Nodes.JsonObject obj => obj.Count + " полей",
            System.Text.Json.Nodes.JsonArray array => array.Count + " элементов",
            null => "нет данных",
            _ => ReportDataExplorer.Text(node)
        };
    }

    private static void Section(StringBuilder builder, string title)
    {
        builder.AppendLine();
        builder.AppendLine(title);
        builder.AppendLine(new string('=', Math.Min(120, Math.Max(20, title.Length))));
    }

    private static string Wrap(string prefix, string value)
    {
        string normalized = value.ReplaceLineEndings(" ");
        return prefix + normalized;
    }

    private static string Trim(string value, int length)
    {
        string normalized = value.ReplaceLineEndings(" ");
        return normalized.Length <= length ? normalized : normalized[..Math.Max(0, length - 1)] + "…";
    }

    private static string Bool(bool value)
    {
        return value ? "да" : "нет";
    }
}
