using System.Text;
using System.Text.Json;
using LaptopInspector.Core.Models;

namespace LaptopInspector.Reporting.Writers;

public sealed class JsonReportWriter : IReportWriter
{
    public string Format => "json";

    public string DefaultExtension => ".json";

    public async Task<ReportWriteResult> WriteAsync(
        InspectionReport report,
        string outputDirectory,
        string baseFileName,
        CancellationToken cancellationToken)
    {
        JsonSerializerOptions options = ReportSerialization.CreateOptions();
        // Фабрика уже посчитала hash; для отчётов, собранных в обход неё, считаем здесь.
        string sha256 = report.Metadata.ReportHashSha256 ?? ReportFactory.ComputeReportHash(report);
        InspectionReport finalReport = report with
        {
            Metadata = report.Metadata with { ReportHashSha256 = sha256 }
        };

        string path = Path.Combine(outputDirectory, baseFileName + DefaultExtension);
        string json = JsonSerializer.Serialize(finalReport, options);
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        return new ReportWriteResult(Format, path);
    }
}
