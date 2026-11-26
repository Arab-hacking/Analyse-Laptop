using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using LaptopInspector.Core.Models;

namespace LaptopInspector.Reporting.Writers;

public sealed class HtmlReportWriter : IReportWriter
{
    public string Format => "html";

    public string DefaultExtension => ".html";

    public async Task<ReportWriteResult> WriteAsync(
        InspectionReport report,
        string outputDirectory,
        string baseFileName,
        CancellationToken cancellationToken)
    {
        string path = Path.Combine(outputDirectory, baseFileName + DefaultExtension);
        StringBuilder builder = new();
        builder.AppendLine("<!doctype html>");
        builder.AppendLine($"<html lang=\"{Escape(report.Metadata.Language)}\">");
        builder.AppendLine("<head>");
        builder.AppendLine("<meta charset=\"utf-8\">");
        builder.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        builder.AppendLine("<meta name=\"generator\" content=\"LaptopInspector\">");
        builder.AppendLine("<title>LaptopInspector — отчёт</title>");
        AppendStyle(builder);
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        AppendNavigation(builder, report);
        builder.AppendLine("<main>");
        AppendHero(builder, report);
        AppendNotice(builder);
        AppendDashboard(builder, report);
        AppendComponents(builder, report);
        AppendFindings(builder, report);
        AppendCollectors(builder, report);
        AppendDataSections(builder, report);
        AppendRawData(builder, report);
        AppendFooter(builder, report);
        builder.AppendLine("</main>");
        AppendScript(builder);
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        await File.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        return new ReportWriteResult(Format, path);
    }

    private static void AppendNavigation(StringBuilder builder, InspectionReport report)
    {
        builder.AppendLine("<aside class=\"nav\">");
        builder.AppendLine("<div class=\"brand\"><strong>LaptopInspector</strong><span>offline report</span></div>");
        builder.AppendLine("<a href=\"#dashboard\">Dashboard</a>");
        builder.AppendLine("<a href=\"#components\">Компоненты</a>");
        builder.AppendLine("<a href=\"#findings\">Проблемы</a>");
        builder.AppendLine("<a href=\"#collectors\">Журнал сбора</a>");
        builder.AppendLine("<a href=\"#sections\">Разделы данных</a>");
        builder.AppendLine("<a href=\"#raw\">Raw JSON</a>");
        builder.AppendLine("<button id=\"theme\" type=\"button\">Сменить тему</button>");
        builder.AppendLine("<div class=\"mini\">Overall<br><b>" + report.Summary.OverallScore.ToString(CultureInfo.InvariantCulture) + "/100</b></div>");
        builder.AppendLine("</aside>");
    }

    private static void AppendHero(StringBuilder builder, InspectionReport report)
    {
        builder.AppendLine("<header class=\"hero\">");
        builder.AppendLine("<div>");
        builder.AppendLine("<h1>Отчёт анализа ноутбука</h1>");
        builder.AppendLine($"<p>{Escape(report.Metadata.CreatedAt.ToLocalTime().ToString("f", CultureInfo.CurrentCulture))} · {Escape(report.Metadata.GeneratorVersion)} · {Escape(report.Metadata.OperatingSystem ?? "нет данных")}</p>");
        builder.AppendLine("</div>");
        builder.AppendLine($"<span class=\"verdict score-{ScoreBand(report.Summary.OverallScore)}\">{Escape(report.Summary.Verdict)}</span>");
        builder.AppendLine("</header>");
    }

    private static void AppendNotice(StringBuilder builder)
    {
        builder.AppendLine("<section class=\"notice\">Отчёт создан в режиме read-only. Он может содержать сведения о железе, драйверах, ПО, сети, безопасности, журналах событий и здоровье компонентов. Пароли, Wi‑Fi ключи, BitLocker recovery keys, cookies, история браузера и содержимое пользовательских файлов не собираются.</section>");
    }

    private static void AppendDashboard(StringBuilder builder, InspectionReport report)
    {
        Dictionary<Severity, int> severityCounts = Enum.GetValues<Severity>().ToDictionary(static severity => severity, severity => report.Findings.Count(finding => finding.Severity == severity));
        builder.AppendLine("<section id=\"dashboard\" class=\"panel\">");
        builder.AppendLine("<div class=\"section-title\"><h2>Dashboard</h2><input id=\"search\" placeholder=\"Поиск по отчёту\" autocomplete=\"off\"></div>");
        builder.AppendLine("<div class=\"cards scores\">");
        AppendScoreCard(builder, "Hardware", report.Summary.HardwareHealthScore);
        AppendScoreCard(builder, "Security", report.Summary.SecurityScore);
        AppendScoreCard(builder, "Stability", report.Summary.StabilityScore);
        AppendScoreCard(builder, "Overall", report.Summary.OverallScore);
        builder.AppendLine("</div>");
        builder.AppendLine("<div class=\"severity-strip\">");
        foreach (Severity severity in new[] { Severity.Critical, Severity.High, Severity.Medium, Severity.Low, Severity.Info })
        {
            builder.AppendLine($"<button class=\"chip {SeverityClass(severity)}\" data-severity=\"{Escape(severity.ToString())}\">{Escape(severity.ToString())}: {severityCounts[severity]}</button>");
        }

        builder.AppendLine("<button class=\"chip\" data-severity=\"all\">Все</button>");
        builder.AppendLine("</div>");
        builder.AppendLine("<div class=\"meta-grid\">");
        AppendMeta(builder, "Компьютер", report.Metadata.ComputerName ?? "нет данных");
        AppendMeta(builder, "Администратор", report.Metadata.IsAdministrator ? "да" : "нет");
        AppendMeta(builder, "Анонимизация", report.Metadata.Anonymized ? "да" : "нет");
        AppendMeta(builder, "Длительность", report.Metadata.DurationMs.ToString("N0", CultureInfo.InvariantCulture) + " мс");
        AppendMeta(builder, "Форматы", string.Join(", ", report.Metadata.RequestedFormats));
        AppendMeta(builder, "SHA-256", report.Metadata.ReportHashSha256 ?? "см. JSON-отчёт");
        builder.AppendLine("</div>");
        builder.AppendLine("</section>");
    }

    private static void AppendScoreCard(StringBuilder builder, string title, int score)
    {
        double circumference = 2 * Math.PI * 45;
        // SVG-атрибуты всегда форматируются инвариантно: в ru-RU десятичный разделитель —
        // запятая, из-за чего атрибут stroke-dasharray в отчёте становился невалидным.
        string dashArray = circumference.ToString("F2", CultureInfo.InvariantCulture);
        string offset = (circumference * (100 - score) / 100).ToString("F2", CultureInfo.InvariantCulture);
        builder.AppendLine($"<article class=\"card score-card score-{ScoreBand(score)}\">");
        builder.AppendLine($"<svg viewBox=\"0 0 120 120\" aria-label=\"{Escape(title)} {score}\">");
        builder.AppendLine("<circle class=\"gauge-bg\" cx=\"60\" cy=\"60\" r=\"45\"></circle>");
        builder.AppendLine($"<circle class=\"gauge\" cx=\"60\" cy=\"60\" r=\"45\" stroke-dasharray=\"{dashArray}\" stroke-dashoffset=\"{offset}\"></circle>");
        builder.AppendLine($"<text x=\"60\" y=\"66\" text-anchor=\"middle\">{score}</text>");
        builder.AppendLine("</svg>");
        builder.AppendLine($"<h3>{Escape(title)}</h3>");
        builder.AppendLine("</article>");
    }

    private static void AppendComponents(StringBuilder builder, InspectionReport report)
    {
        JsonObject data = report.Data;
        builder.AppendLine("<section id=\"components\" class=\"panel searchable\">");
        builder.AppendLine("<h2>Ключевые компоненты</h2>");
        builder.AppendLine("<div class=\"component-grid\">");
        AppendComponent(builder, "CPU", ReportDataExplorer.Text(ReportDataExplorer.Select(data, "hardware.processor", "processors", "0", "Name")), "Ядра/потоки: " + ReportDataExplorer.Text(ReportDataExplorer.Select(data, "hardware.processor", "processors", "0", "NumberOfCores")) + "/" + ReportDataExplorer.Text(ReportDataExplorer.Select(data, "hardware.processor", "processors", "0", "NumberOfLogicalProcessors")));
        AppendComponent(builder, "RAM", ReportDataExplorer.FormatKilobytes(ReportDataExplorer.Number(ReportDataExplorer.Select(data, "hardware.memory", "operatingSystemMemory", "TotalVisibleMemorySize"))), "Модулей: " + ReportDataExplorer.Count(data, "hardware.memory", "physicalMemoryModules").ToString(CultureInfo.InvariantCulture));
        AppendComponent(builder, "Storage", ReportDataExplorer.Count(data, "native.storage", "physicalDrives").ToString(CultureInfo.InvariantCulture) + " дисков", "Max NVMe used: " + MaxNumber(data, new[] { "native.storage", "physicalDrives", "*", "nvmeHealth", "percentageUsed" }));
        AppendComponent(builder, "Battery", ReportDataExplorer.Count(data, "native.battery", "batteries").ToString(CultureInfo.InvariantCulture) + " батарей", "Max wear: " + MaxNumber(data, new[] { "native.battery", "batteries", "*", "information", "wearPercent" }) + "%");
        AppendComponent(builder, "GPU/Display", ReportDataExplorer.Text(ReportDataExplorer.Select(data, "native.display", "dxgiAdapters", "0", "description")), "Outputs: " + ReportDataExplorer.Count(data, "native.display", "dxgiAdapters", "0", "outputs").ToString(CultureInfo.InvariantCulture));
        AppendComponent(builder, "OS", ReportDataExplorer.Text(ReportDataExplorer.Select(data, "os.windows", "operatingSystem", "Caption")), "Build: " + ReportDataExplorer.Text(ReportDataExplorer.Select(data, "os.windows", "operatingSystem", "BuildNumber")));
        AppendComponent(builder, "Network", ReportDataExplorer.Count(data, "network.overview", "adapters").ToString(CultureInfo.InvariantCulture) + " адаптеров", "Wi‑Fi interfaces: " + ReportDataExplorer.Count(data, "native.wlan", "interfaces").ToString(CultureInfo.InvariantCulture));
        AppendComponent(builder, "Security", report.Summary.SecurityScore + "/100", "Findings: " + report.Findings.Count(static item => item.Category.Contains("Security", StringComparison.OrdinalIgnoreCase)).ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("</div>");
        builder.AppendLine("</section>");
    }

    private static void AppendComponent(StringBuilder builder, string title, string value, string detail)
    {
        builder.AppendLine("<article class=\"component\">");
        builder.AppendLine($"<span>{Escape(title)}</span>");
        builder.AppendLine($"<strong>{Escape(value)}</strong>");
        builder.AppendLine($"<small>{Escape(detail)}</small>");
        builder.AppendLine("</article>");
    }

    private static void AppendFindings(StringBuilder builder, InspectionReport report)
    {
        builder.AppendLine("<section id=\"findings\" class=\"panel searchable\">");
        builder.AppendLine("<h2>Проблемы и рекомендации</h2>");
        if (report.Findings.Count == 0)
        {
            builder.AppendLine("<p class=\"empty\">Находки отсутствуют.</p>");
        }
        else
        {
            foreach (Finding finding in report.Findings)
            {
                string severity = finding.Severity.ToString();
                builder.AppendLine($"<article class=\"finding {SeverityClass(finding.Severity)}\" data-severity=\"{Escape(severity)}\">");
                builder.AppendLine($"<h3>{Escape(finding.Title)} <span>{Escape(severity)}</span></h3>");
                builder.AppendLine($"<p>{Escape(finding.Details)}</p>");
                builder.AppendLine($"<p><strong>Рекомендация:</strong> {Escape(finding.Recommendation)}</p>");
                builder.AppendLine($"<code>{Escape(finding.RuleId)} · {Escape(finding.Category)} · {Escape(finding.Evidence)}</code>");
                builder.AppendLine("</article>");
            }
        }

        builder.AppendLine("</section>");
    }

    private static void AppendCollectors(StringBuilder builder, InspectionReport report)
    {
        builder.AppendLine("<section id=\"collectors\" class=\"panel searchable\">");
        builder.AppendLine("<h2>Журнал сбора</h2>");
        builder.AppendLine("<table class=\"sortable\"><thead><tr><th>ID</th><th>Категория</th><th>Статус</th><th>Длительность</th><th>Ошибки/причина</th></tr></thead><tbody>");
        foreach (CollectorResult collector in report.Collectors.OrderBy(static item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            string note = collector.SkipReason ?? string.Join("; ", collector.Errors);
            builder.AppendLine("<tr>");
            builder.AppendLine($"<td><code>{Escape(collector.Id)}</code></td>");
            builder.AppendLine($"<td>{Escape(CategoryFromCollector(collector.Id))}</td>");
            builder.AppendLine($"<td><span class=\"status status-{Escape(collector.Status.ToString().ToLowerInvariant())}\">{Escape(collector.Status.ToString())}</span></td>");
            builder.AppendLine($"<td data-sort=\"{collector.Duration.TotalMilliseconds:F0}\">{collector.Duration.TotalMilliseconds:F0} мс</td>");
            builder.AppendLine($"<td>{Escape(note)}</td>");
            builder.AppendLine("</tr>");
        }

        builder.AppendLine("</tbody></table>");
        builder.AppendLine("</section>");
    }

    private static void AppendDataSections(StringBuilder builder, InspectionReport report)
    {
        builder.AppendLine("<section id=\"sections\" class=\"panel searchable\">");
        builder.AppendLine("<h2>Разделы данных</h2>");
        foreach (KeyValuePair<string, JsonNode?> section in report.Data.OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (section.Key.Equals("health.rules", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            builder.AppendLine("<details class=\"data-section\">");
            builder.AppendLine($"<summary><code>{Escape(section.Key)}</code> <span>{Escape(NodeSummary(section.Value))}</span></summary>");
            AppendNodePreview(builder, section.Value);
            builder.AppendLine("</details>");
        }

        builder.AppendLine("</section>");
    }

    private static void AppendNodePreview(StringBuilder builder, JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            IReadOnlyList<KeyValuePair<string, string>> scalars = ReportDataExplorer.FlattenScalars(obj).Take(30).ToArray();
            if (scalars.Count > 0)
            {
                builder.AppendLine("<table class=\"kv\"><tbody>");
                foreach (KeyValuePair<string, string> item in scalars)
                {
                    builder.AppendLine($"<tr><th>{Escape(item.Key)}</th><td>{Escape(item.Value)}</td></tr>");
                }

                builder.AppendLine("</tbody></table>");
            }
        }
        else if (node is JsonArray array)
        {
            builder.AppendLine($"<p>Элементов: {array.Count}</p>");
        }

        builder.AppendLine("<details><summary>JSON раздела</summary>");
        builder.AppendLine($"<pre>{Escape(node?.ToJsonString(ReportSerialization.CreateOptions()) ?? "null")}</pre>");
        builder.AppendLine("</details>");
    }

    private static void AppendRawData(StringBuilder builder, InspectionReport report)
    {
        string json = report.Data.ToJsonString(ReportSerialization.CreateOptions());
        builder.AppendLine("<section id=\"raw\" class=\"panel searchable\">");
        builder.AppendLine("<h2>Сырые данные</h2>");
        builder.AppendLine("<details><summary>Показать полный JSON data</summary>");
        builder.AppendLine($"<pre>{Escape(json)}</pre>");
        builder.AppendLine("</details>");
        builder.AppendLine("</section>");
    }

    private static void AppendFooter(StringBuilder builder, InspectionReport report)
    {
        builder.AppendLine("<footer class=\"footer\">");
        builder.AppendLine($"Сгенерировано {Escape(report.Metadata.Generator)} {Escape(report.Metadata.GeneratorVersion)}. Schema: {Escape(report.SchemaVersion)}. SHA-256: {Escape(report.Metadata.ReportHashSha256 ?? "см. JSON")}");
        builder.AppendLine("</footer>");
    }

    private static void AppendMeta(StringBuilder builder, string name, string value)
    {
        builder.AppendLine("<div class=\"meta\">");
        builder.AppendLine($"<span>{Escape(name)}</span>");
        builder.AppendLine($"<strong>{Escape(value)}</strong>");
        builder.AppendLine("</div>");
    }

    private static string MaxNumber(JsonObject data, IReadOnlyList<string> path)
    {
        double max = ReportDataExplorer.SelectAll(data, path)
            .Select(ReportDataExplorer.Number)
            .Where(static item => item.HasValue)
            .Select(static item => item.GetValueOrDefault())
            .DefaultIfEmpty(double.NaN)
            .Max();
        return double.IsNaN(max) ? "нет данных" : max.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string NodeSummary(JsonNode? node)
    {
        return node switch
        {
            JsonObject obj => obj.Count.ToString(CultureInfo.InvariantCulture) + " полей",
            JsonArray array => array.Count.ToString(CultureInfo.InvariantCulture) + " элементов",
            null => "null",
            _ => ReportDataExplorer.Text(node)
        };
    }

    private static string CategoryFromCollector(string id)
    {
        int dot = id.IndexOf('.', StringComparison.Ordinal);
        return dot > 0 ? id[..dot] : id;
    }

    private static string ScoreBand(int score)
    {
        return score >= 90 ? "excellent" : score >= 75 ? "good" : score >= 60 ? "fair" : score >= 40 ? "attention" : "critical";
    }

    private static string SeverityClass(Severity severity)
    {
        return severity.ToString().ToLowerInvariant();
    }

    private static string Escape(string? value)
    {
        return WebUtility.HtmlEncode(value ?? string.Empty);
    }

    private static void AppendStyle(StringBuilder builder)
    {
        builder.AppendLine("<style>");
        builder.AppendLine("""
:root{color-scheme:light;--bg:#f6f8fb;--panel:#fff;--text:#172033;--muted:#687385;--brand:#2563eb;--ok:#16a34a;--warn:#f59e0b;--bad:#dc2626;--border:#dbe3ef;--shadow:0 14px 34px rgba(15,23,42,.08);--code:#f1f5f9}
:root.dark{color-scheme:dark;--bg:#0f172a;--panel:#111827;--text:#e5e7eb;--muted:#94a3b8;--brand:#60a5fa;--ok:#22c55e;--warn:#fbbf24;--bad:#f87171;--border:#263244;--shadow:none;--code:#0b1220}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);font:15px/1.55 system-ui,-apple-system,Segoe UI,sans-serif}.nav{position:fixed;inset:0 auto 0 0;width:250px;padding:22px;background:var(--panel);border-right:1px solid var(--border);display:flex;flex-direction:column;gap:10px;overflow:auto}.brand{margin-bottom:10px}.brand strong{font-size:21px;display:block}.brand span,.mini{color:var(--muted);font-size:12px}.mini{border:1px solid var(--border);border-radius:12px;padding:10px;margin-top:8px}.mini b{font-size:22px;color:var(--brand)}.nav a,.nav button{color:var(--text);text-decoration:none;border:1px solid var(--border);background:transparent;border-radius:10px;padding:10px;text-align:left;cursor:pointer}main{margin-left:250px;padding:28px;max-width:1450px}.hero{display:flex;justify-content:space-between;align-items:center;gap:16px;margin-bottom:18px}.hero h1{margin:0;font-size:36px}.hero p{color:var(--muted);margin:4px 0}.verdict{border-radius:999px;padding:10px 16px;font-weight:800;color:#fff}.notice,.panel{background:var(--panel);border:1px solid var(--border);border-radius:18px;box-shadow:var(--shadow);padding:20px;margin:18px 0}.section-title{display:flex;justify-content:space-between;gap:16px;align-items:center}.section-title input{min-width:310px;border:1px solid var(--border);border-radius:12px;background:var(--bg);color:var(--text);padding:10px 12px}.cards{display:grid;grid-template-columns:repeat(4,minmax(150px,1fr));gap:16px}.card{text-align:center;border:1px solid var(--border);border-radius:16px;padding:16px;background:linear-gradient(180deg,rgba(37,99,235,.08),transparent)}.card svg{width:120px;height:120px}.gauge-bg{fill:none;stroke:var(--border);stroke-width:10}.gauge{fill:none;stroke:var(--brand);stroke-linecap:round;stroke-width:10;transform:rotate(-90deg);transform-origin:50% 50%}.score-critical .gauge,.score-critical.verdict{stroke:var(--bad);background:var(--bad)}.score-attention .gauge,.score-attention.verdict{stroke:var(--warn);background:var(--warn)}.score-good .gauge,.score-excellent .gauge,.score-good.verdict,.score-excellent.verdict{stroke:var(--ok);background:var(--ok)}.card text{font-size:28px;font-weight:800;fill:var(--text)}.meta-grid,.component-grid{display:grid;grid-template-columns:repeat(3,minmax(190px,1fr));gap:12px;margin-top:16px}.component,.meta{border:1px solid var(--border);border-radius:12px;padding:12px;min-width:0}.component span,.meta span{display:block;color:var(--muted);font-size:13px}.component strong,.meta strong{display:block;word-break:break-word}.component small{color:var(--muted)}.severity-strip{display:flex;flex-wrap:wrap;gap:8px;margin-top:14px}.chip{border:1px solid var(--border);border-radius:999px;background:var(--bg);color:var(--text);padding:7px 10px;cursor:pointer}.chip.critical,.finding.critical{border-color:var(--bad)}.chip.high,.finding.high{border-color:#ef4444}.chip.medium,.finding.medium{border-color:var(--warn)}.chip.low,.finding.low{border-color:var(--brand)}.finding{border-left:6px solid var(--brand);background:rgba(37,99,235,.06);border-radius:12px;padding:14px;margin:12px 0}.finding h3{display:flex;justify-content:space-between;gap:12px;margin:0 0 6px}.finding.critical,.finding.high{background:rgba(220,38,38,.08);border-left-color:var(--bad)}.finding.medium{background:rgba(245,158,11,.08);border-left-color:var(--warn)}table{width:100%;border-collapse:collapse}th,td{border-bottom:1px solid var(--border);padding:10px;text-align:left;vertical-align:top}th{color:var(--muted);font-size:13px;cursor:pointer}.kv th{width:32%;cursor:default}.status{border-radius:999px;padding:4px 8px;background:var(--border);display:inline-block}.status-success{background:rgba(22,163,74,.18);color:var(--ok)}.status-failed,.status-timedout{background:rgba(220,38,38,.18);color:var(--bad)}.status-partial,.status-skipped{background:rgba(245,158,11,.18);color:var(--warn)}code,pre{background:var(--code);border-radius:8px}code{padding:2px 5px}pre{white-space:pre-wrap;word-break:break-word;border:1px solid var(--border);padding:14px;max-height:540px;overflow:auto}.data-section{border:1px solid var(--border);border-radius:12px;padding:10px;margin:10px 0}.data-section summary{cursor:pointer}.data-section summary span{float:right;color:var(--muted)}.empty,.footer{color:var(--muted)}.footer{padding:20px;text-align:center}.hidden{display:none!important}@media (max-width:950px){.nav{position:static;width:auto}.nav a,.nav button{display:inline-block}main{margin:0;padding:16px}.cards,.meta-grid,.component-grid{grid-template-columns:1fr}.hero{align-items:flex-start;flex-direction:column}.section-title{flex-direction:column;align-items:stretch}.section-title input{min-width:0}}@media print{.nav,#search,#theme,.severity-strip{display:none}main{margin:0}.panel,.notice{box-shadow:none;break-inside:avoid}pre{max-height:none}}
""");
        builder.AppendLine("</style>");
    }

    private static void AppendScript(StringBuilder builder)
    {
        builder.AppendLine("<script>");
        builder.AppendLine("""
const root=document.documentElement;document.getElementById('theme').addEventListener('click',()=>root.classList.toggle('dark'));
const search=document.getElementById('search');search.addEventListener('input',()=>{const q=search.value.toLowerCase();document.querySelectorAll('.searchable').forEach(s=>{s.classList.toggle('hidden',q&& !s.innerText.toLowerCase().includes(q));});});
document.querySelectorAll('[data-severity]').forEach(btn=>btn.addEventListener('click',()=>{const sev=btn.dataset.severity;document.querySelectorAll('.finding').forEach(f=>f.classList.toggle('hidden',sev!=='all'&&f.dataset.severity!==sev));}));
document.querySelectorAll('table.sortable th').forEach((th,i)=>th.addEventListener('click',()=>{const table=th.closest('table');const rows=[...table.tBodies[0].rows];const asc=th.dataset.asc!=='true';rows.sort((a,b)=>{const av=a.cells[i].dataset.sort||a.cells[i].innerText;const bv=b.cells[i].dataset.sort||b.cells[i].innerText;const an=Number(av),bn=Number(bv);return (isNaN(an)||isNaN(bn)?av.localeCompare(bv):an-bn)*(asc?1:-1);});rows.forEach(r=>table.tBodies[0].appendChild(r));th.dataset.asc=String(asc);}));
""");
        builder.AppendLine("</script>");
    }
}
