using LaptopInspector.Reporting.Writers;
using Microsoft.Extensions.DependencyInjection;

namespace LaptopInspector.Reporting;

public static class ReportingServiceCollectionExtensions
{
    public static IServiceCollection AddLaptopInspectorReporting(this IServiceCollection services)
    {
        services.AddSingleton<ReportFactory>();
        services.AddSingleton<ReportWriterService>();
        services.AddSingleton<ReportDiffService>();
        services.AddSingleton<IReportWriter, JsonReportWriter>();
        services.AddSingleton<IReportWriter, HtmlReportWriter>();
        services.AddSingleton<IReportWriter, TxtReportWriter>();
        services.AddSingleton<IReportWriter, MarkdownReportWriter>();
        services.AddSingleton<IReportWriter, CsvReportWriter>();
        return services;
    }
}
