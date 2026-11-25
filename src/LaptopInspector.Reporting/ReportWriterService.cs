using LaptopInspector.Core.Models;
using Microsoft.Extensions.Logging;

namespace LaptopInspector.Reporting;

public sealed class ReportWriterService
{
    private readonly IReadOnlyDictionary<string, IReportWriter> _writers;
    private readonly ILogger<ReportWriterService> _logger;

    public ReportWriterService(IEnumerable<IReportWriter> writers, ILogger<ReportWriterService> logger)
    {
        _writers = writers.ToDictionary(static writer => writer.Format, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    public async Task<IReadOnlyList<ReportWriteResult>> WriteAsync(
        InspectionReport report,
        string outputDirectory,
        IReadOnlyList<string> formats,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        string baseFileName = ReportNameBuilder.BuildBaseName(report);
        List<ReportWriteResult> results = new();

        foreach (string format in formats.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!_writers.TryGetValue(format, out IReportWriter? writer))
            {
                throw new InvalidOperationException("Неизвестный формат отчёта: " + format);
            }

            ReportWriteResult result = await writer
                .WriteAsync(report, outputDirectory, baseFileName, cancellationToken)
                .ConfigureAwait(false);
            _logger.LogInformation("Отчёт {Format} сохранён: {Path}", result.Format, result.Path);
            results.Add(result);
        }

        return results;
    }
}
