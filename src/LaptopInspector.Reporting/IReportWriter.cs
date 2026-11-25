using LaptopInspector.Core.Models;

namespace LaptopInspector.Reporting;

public interface IReportWriter
{
    string Format { get; }

    string DefaultExtension { get; }

    Task<ReportWriteResult> WriteAsync(
        InspectionReport report,
        string outputDirectory,
        string baseFileName,
        CancellationToken cancellationToken);
}

public sealed record ReportWriteResult(string Format, string Path);
