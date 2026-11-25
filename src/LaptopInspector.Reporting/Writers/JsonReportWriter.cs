using System.Security.Cryptography;
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
        InspectionReport unsignedReport = report with
        {
            Metadata = report.Metadata with { ReportHashSha256 = null }
        };
        byte[] unsignedBytes = JsonSerializer.SerializeToUtf8Bytes(unsignedReport, options);
        string sha256 = Convert.ToHexString(SHA256.HashData(unsignedBytes)).ToLowerInvariant();
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
