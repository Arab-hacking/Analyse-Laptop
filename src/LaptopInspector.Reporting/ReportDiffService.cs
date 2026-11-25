using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace LaptopInspector.Reporting;

public sealed class ReportDiffService
{
    public async Task<string> WriteDiffAsync(
        string oldReportPath,
        string newReportPath,
        string outputPath,
        CancellationToken cancellationToken)
    {
        JsonNode? oldNode = JsonNode.Parse(await File.ReadAllTextAsync(oldReportPath, cancellationToken).ConfigureAwait(false));
        JsonNode? newNode = JsonNode.Parse(await File.ReadAllTextAsync(newReportPath, cancellationToken).ConfigureAwait(false));
        if (oldNode is null || newNode is null)
        {
            throw new InvalidOperationException("Один из JSON-отчётов пуст или некорректен.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? Directory.GetCurrentDirectory());
        StringBuilder builder = new();
        builder.AppendLine("<!doctype html><html lang=\"ru\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>LaptopInspector diff</title>");
        builder.AppendLine("<style>body{font:15px/1.55 system-ui,sans-serif;margin:32px;max-width:1250px;color:#172033}table{border-collapse:collapse;width:100%;margin:12px 0 28px}th,td{border:1px solid #dbe3ef;padding:8px;text-align:left;vertical-align:top}th{background:#f6f8fb}.changed{background:#fff7ed}.bad{background:#fee2e2}.ok{background:#dcfce7}.muted{color:#64748b}code{background:#f1f5f9;padding:2px 5px;border-radius:6px}h2{margin-top:34px}</style>");
        builder.AppendLine("</head><body>");
        builder.AppendLine("<h1>LaptopInspector diff</h1>");
        builder.AppendLine("<p class=\"muted\">Сравнение двух JSON-отчётов: оценки, батарея, накопители, ПО, устройства, события, настройки безопасности и находки.</p>");
        AppendMetadataDiff(builder, oldNode, newNode);
        AppendScoreDiff(builder, oldNode, newNode);
        AppendHealthMetricDiff(builder, oldNode, newNode);
        AppendSetDiff(builder, "Новые и удалённые программы", "Программа", GetStringSet(oldNode, "data", "software.inventory", "installedPrograms", "*", "name"), GetStringSet(newNode, "data", "software.inventory", "installedPrograms", "*", "name"));
        AppendSetDiff(builder, "Новые и удалённые драйверы/устройства", "Устройство", GetStringSet(oldNode, "data", "native.deviceTree", "devices", "*", "instanceId"), GetStringSet(newNode, "data", "native.deviceTree", "devices", "*", "instanceId"));
        AppendEventDiff(builder, oldNode, newNode);
        AppendSecurityDiff(builder, oldNode, newNode);
        AppendCollectorDiff(builder, oldNode, newNode);
        AppendFindingDiff(builder, oldNode, newNode);
        builder.AppendLine("</body></html>");
        await File.WriteAllTextAsync(outputPath, builder.ToString(), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        return outputPath;
    }

    private static void AppendMetadataDiff(StringBuilder builder, JsonNode oldNode, JsonNode newNode)
    {
        builder.AppendLine("<h2>Метаданные</h2><table><thead><tr><th>Поле</th><th>Старый</th><th>Новый</th></tr></thead><tbody>");
        foreach (string field in new[] { "createdAt", "generatorVersion", "computerName", "operatingSystem", "isAdministrator", "anonymized" })
        {
            AppendValueRow(builder, field, ReadPath(oldNode, "metadata", field), ReadPath(newNode, "metadata", field));
        }

        builder.AppendLine("</tbody></table>");
    }

    private static void AppendScoreDiff(StringBuilder builder, JsonNode oldNode, JsonNode newNode)
    {
        builder.AppendLine("<h2>Оценки</h2><table><thead><tr><th>Метрика</th><th>Старый</th><th>Новый</th><th>Δ</th></tr></thead><tbody>");
        foreach (string metric in new[] { "hardwareHealthScore", "securityScore", "stabilityScore", "overallScore" })
        {
            double oldValue = ReadNumber(oldNode, "summary", metric) ?? 0;
            double newValue = ReadNumber(newNode, "summary", metric) ?? 0;
            AppendDeltaRow(builder, metric, oldValue, newValue, higherIsBetter: true);
        }

        AppendValueRow(builder, "verdict", ReadPath(oldNode, "summary", "verdict"), ReadPath(newNode, "summary", "verdict"));
        builder.AppendLine("</tbody></table>");
    }

    private static void AppendHealthMetricDiff(StringBuilder builder, JsonNode oldNode, JsonNode newNode)
    {
        builder.AppendLine("<h2>Здоровье компонентов</h2><table><thead><tr><th>Метрика</th><th>Старый</th><th>Новый</th><th>Δ</th></tr></thead><tbody>");
        AppendDeltaRow(builder, "Макс. износ батареи, %", MaxNumber(oldNode, "data", "native.battery", "batteries", "*", "information", "wearPercent"), MaxNumber(newNode, "data", "native.battery", "batteries", "*", "information", "wearPercent"), higherIsBetter: false);
        AppendDeltaRow(builder, "Макс. NVMe Percentage Used, %", MaxNumber(oldNode, "data", "native.storage", "physicalDrives", "*", "nvmeHealth", "percentageUsed"), MaxNumber(newNode, "data", "native.storage", "physicalDrives", "*", "nvmeHealth", "percentageUsed"), higherIsBetter: false);
        AppendDeltaRow(builder, "Сумма NVMe Data Units Written", SumNumber(oldNode, "data", "native.storage", "physicalDrives", "*", "nvmeHealth", "dataUnitsWritten", "raw"), SumNumber(newNode, "data", "native.storage", "physicalDrives", "*", "nvmeHealth", "dataUnitsWritten", "raw"), higherIsBetter: false);
        AppendDeltaRow(builder, "NVMe Media/Data Integrity Errors", SumNumber(oldNode, "data", "native.storage", "physicalDrives", "*", "nvmeHealth", "mediaAndDataIntegrityErrors", "raw"), SumNumber(newNode, "data", "native.storage", "physicalDrives", "*", "nvmeHealth", "mediaAndDataIntegrityErrors", "raw"), higherIsBetter: false);
        builder.AppendLine("</tbody></table>");
    }

    private static void AppendEventDiff(StringBuilder builder, JsonNode oldNode, JsonNode newNode)
    {
        builder.AppendLine("<h2>События стабильности</h2><table><thead><tr><th>Группа</th><th>Старый</th><th>Новый</th><th>Δ</th></tr></thead><tbody>");
        foreach ((string Label, string Path) item in new[]
        {
            ("BSOD BugCheck", "bugChecks"),
            ("WHEA", "wheaLogger"),
            ("Kernel-Power 41", "kernelPower41"),
            ("Disk/NTFS/stornvme", "diskAndFileSystemErrors"),
            ("Display TDR 4101", "displayTdr4101"),
            ("Application crash groups", "applicationCrashGroups")
        })
        {
            AppendDeltaRow(builder, item.Label, Count(oldNode, "data", "stability.events", item.Path), Count(newNode, "data", "stability.events", item.Path), higherIsBetter: false);
        }

        builder.AppendLine("</tbody></table>");
    }

    private static void AppendSecurityDiff(StringBuilder builder, JsonNode oldNode, JsonNode newNode)
    {
        builder.AppendLine("<h2>Настройки безопасности</h2><table><thead><tr><th>Поле</th><th>Старый</th><th>Новый</th></tr></thead><tbody>");
        AppendValueRow(builder, "Secure Boot", Text(ReportDataExplorer.Select(oldNode, "data", "native.firmware", "secureBoot", "enabled")), Text(ReportDataExplorer.Select(newNode, "data", "native.firmware", "secureBoot", "enabled")));
        AppendValueRow(builder, "Defender Real-time", Text(ReportDataExplorer.Select(oldNode, "data", "security.overview", "defender", "RealTimeProtectionEnabled")), Text(ReportDataExplorer.Select(newNode, "data", "security.overview", "defender", "RealTimeProtectionEnabled")));
        AppendValueRow(builder, "Defender signatures out of date", Text(ReportDataExplorer.Select(oldNode, "data", "security.overview", "defender", "DefenderSignaturesOutOfDate")), Text(ReportDataExplorer.Select(newNode, "data", "security.overview", "defender", "DefenderSignaturesOutOfDate")));
        AppendValueRow(builder, "RDP fDenyTSConnections", Text(ReportDataExplorer.Select(oldNode, "data", "security.overview", "rdp", "terminalServer", "fDenyTSConnections")), Text(ReportDataExplorer.Select(newNode, "data", "security.overview", "rdp", "terminalServer", "fDenyTSConnections")));
        AppendValueRow(builder, "SMB1 state", Text(ReportDataExplorer.Select(oldNode, "data", "security.overview", "smb1", "State")), Text(ReportDataExplorer.Select(newNode, "data", "security.overview", "smb1", "State")));
        builder.AppendLine("</tbody></table>");
    }

    private static void AppendSetDiff(StringBuilder builder, string title, string itemName, IReadOnlyList<string> oldItems, IReadOnlyList<string> newItems)
    {
        HashSet<string> oldSet = new(oldItems, StringComparer.OrdinalIgnoreCase);
        HashSet<string> newSet = new(newItems, StringComparer.OrdinalIgnoreCase);
        string[] added = newSet.Except(oldSet, StringComparer.OrdinalIgnoreCase).OrderBy(static item => item, StringComparer.OrdinalIgnoreCase).Take(200).ToArray();
        string[] removed = oldSet.Except(newSet, StringComparer.OrdinalIgnoreCase).OrderBy(static item => item, StringComparer.OrdinalIgnoreCase).Take(200).ToArray();
        builder.AppendLine($"<h2>{Escape(title)}</h2>");
        builder.AppendLine("<table><thead><tr><th>Тип</th><th>" + Escape(itemName) + "</th></tr></thead><tbody>");
        foreach (string item in added)
        {
            builder.AppendLine($"<tr class=\"changed\"><td>Добавлено</td><td>{Escape(item)}</td></tr>");
        }

        foreach (string item in removed)
        {
            builder.AppendLine($"<tr class=\"bad\"><td>Удалено</td><td>{Escape(item)}</td></tr>");
        }

        if (added.Length == 0 && removed.Length == 0)
        {
            builder.AppendLine("<tr><td colspan=\"2\" class=\"muted\">Изменений не обнаружено или данные отсутствуют.</td></tr>");
        }

        builder.AppendLine("</tbody></table>");
    }

    private static void AppendCollectorDiff(StringBuilder builder, JsonNode oldNode, JsonNode newNode)
    {
        Dictionary<string, string> oldCollectors = ReadCollectors(oldNode);
        Dictionary<string, string> newCollectors = ReadCollectors(newNode);
        string[] allIds = oldCollectors.Keys.Concat(newCollectors.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static id => id, StringComparer.OrdinalIgnoreCase).ToArray();
        builder.AppendLine("<h2>Сборщики</h2><table><thead><tr><th>ID</th><th>Старый статус</th><th>Новый статус</th></tr></thead><tbody>");
        foreach (string id in allIds)
        {
            oldCollectors.TryGetValue(id, out string? oldStatus);
            newCollectors.TryGetValue(id, out string? newStatus);
            string css = string.Equals(oldStatus, newStatus, StringComparison.OrdinalIgnoreCase) ? string.Empty : " class=\"changed\"";
            builder.AppendLine($"<tr{css}><td><code>{Escape(id)}</code></td><td>{Escape(oldStatus ?? "нет")}</td><td>{Escape(newStatus ?? "нет")}</td></tr>");
        }

        builder.AppendLine("</tbody></table>");
    }

    private static void AppendFindingDiff(StringBuilder builder, JsonNode oldNode, JsonNode newNode)
    {
        IReadOnlyList<string> oldFindings = GetStringSet(oldNode, "findings", "*", "ruleId");
        IReadOnlyList<string> newFindings = GetStringSet(newNode, "findings", "*", "ruleId");
        builder.AppendLine("<h2>Находки</h2>");
        builder.AppendLine("<table><thead><tr><th>Показатель</th><th>Старый</th><th>Новый</th><th>Δ</th></tr></thead><tbody>");
        AppendDeltaRow(builder, "Количество находок", oldFindings.Count, newFindings.Count, higherIsBetter: false);
        foreach (string severity in new[] { "critical", "high", "medium", "low", "info" })
        {
            AppendDeltaRow(builder, "Findings severity " + severity, CountSeverity(oldNode, severity), CountSeverity(newNode, severity), higherIsBetter: false);
        }

        builder.AppendLine("</tbody></table>");
        AppendSetDiff(builder, "Новые и закрытые RuleId", "RuleId", oldFindings, newFindings);
    }

    private static void AppendValueRow(StringBuilder builder, string label, string oldValue, string newValue)
    {
        string css = string.Equals(oldValue, newValue, StringComparison.OrdinalIgnoreCase) ? string.Empty : " class=\"changed\"";
        builder.AppendLine($"<tr{css}><td><code>{Escape(label)}</code></td><td>{Escape(oldValue)}</td><td>{Escape(newValue)}</td></tr>");
    }

    private static void AppendDeltaRow(StringBuilder builder, string label, double oldValue, double newValue, bool higherIsBetter)
    {
        double delta = newValue - oldValue;
        string css = Math.Abs(delta) < 0.001 ? string.Empty : ((higherIsBetter && delta > 0) || (!higherIsBetter && delta < 0) ? " class=\"ok\"" : " class=\"bad\"");
        builder.AppendLine($"<tr{css}><td><code>{Escape(label)}</code></td><td>{oldValue:0.###}</td><td>{newValue:0.###}</td><td>{delta:+0.###;-0.###;0}</td></tr>");
    }

    private static Dictionary<string, string> ReadCollectors(JsonNode node)
    {
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        if (node["collectors"] is not JsonArray collectors)
        {
            return result;
        }

        foreach (JsonNode? item in collectors)
        {
            string? id = item?["id"]?.GetValue<string>();
            string? status = item?["status"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(id))
            {
                result[id] = status ?? "unknown";
            }
        }

        return result;
    }

    private static IReadOnlyList<string> GetStringSet(JsonNode root, params string[] path)
    {
        return ReportDataExplorer.StringSet(root, path);
    }

    private static double Count(JsonNode root, params string[] path)
    {
        JsonNode? node = ReportDataExplorer.Select(root, path);
        return node is JsonArray array ? array.Count : 0;
    }

    private static double CountSeverity(JsonNode root, string severity)
    {
        return ReportDataExplorer.SelectAll(root, new[] { "findings", "*", "severity" })
            .Select(Text)
            .Count(item => string.Equals(item, severity, StringComparison.OrdinalIgnoreCase));
    }

    private static double MaxNumber(JsonNode root, params string[] path)
    {
        double[] values = ReportDataExplorer.SelectAll(root, path)
            .Select(ReportDataExplorer.Number)
            .Where(static item => item.HasValue)
            .Select(static item => item.GetValueOrDefault())
            .ToArray();
        return values.Length == 0 ? 0 : values.Max();
    }

    private static double SumNumber(JsonNode root, params string[] path)
    {
        return ReportDataExplorer.SelectAll(root, path)
            .Select(ReportDataExplorer.Number)
            .Where(static item => item.HasValue)
            .Select(static item => item.GetValueOrDefault())
            .Sum();
    }

    private static string ReadPath(JsonNode node, string first, string second)
    {
        return Text(ReportDataExplorer.Select(node, first, second));
    }

    private static double? ReadNumber(JsonNode node, string first, string second)
    {
        return ReportDataExplorer.Number(ReportDataExplorer.Select(node, first, second));
    }

    private static string Text(JsonNode? node)
    {
        return ReportDataExplorer.Text(node, "нет данных");
    }

    private static string Escape(string value)
    {
        return WebUtility.HtmlEncode(value);
    }
}
