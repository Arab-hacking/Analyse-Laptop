using System.Diagnostics;
using LaptopInspector.Cli.CommandLine;
using LaptopInspector.Core.Abstractions;
using LaptopInspector.Core.Models;
using LaptopInspector.Core.Services;
using LaptopInspector.Reporting;
using Microsoft.Extensions.Logging;

namespace LaptopInspector.Cli;

public sealed class CliApplication
{
    private readonly CollectionScheduler _scheduler;
    private readonly ReportFactory _reportFactory;
    private readonly ReportWriterService _reportWriterService;
    private readonly ReportDiffService _reportDiffService;
    private readonly IEnumerable<ICollector> _collectors;
    private readonly IPrivilegeDetector _privilegeDetector;
    private readonly ILogger<CliApplication> _logger;

    public CliApplication(
        CollectionScheduler scheduler,
        ReportFactory reportFactory,
        ReportWriterService reportWriterService,
        ReportDiffService reportDiffService,
        IEnumerable<ICollector> collectors,
        IPrivilegeDetector privilegeDetector,
        ILogger<CliApplication> logger)
    {
        _scheduler = scheduler;
        _reportFactory = reportFactory;
        _reportWriterService = reportWriterService;
        _reportDiffService = reportDiffService;
        _collectors = collectors;
        _privilegeDetector = privilegeDetector;
        _logger = logger;
    }

    public async Task<int> ExecuteAsync(ParsedCommand command, CancellationToken cancellationToken)
    {
        if (command.HelpRequested || command.Kind == CliCommandKind.Help)
        {
            Console.WriteLine(CommandLineParser.GetHelp());
            return 0;
        }

        return command.Kind switch
        {
            CliCommandKind.Scan or CliCommandKind.Quick => await RunScanAsync(command.ScanOptions, cancellationToken).ConfigureAwait(false),
            CliCommandKind.Diff => await RunDiffAsync(command.DiffOptions, cancellationToken).ConfigureAwait(false),
            CliCommandKind.ListCollectors => ListCollectors(),
            CliCommandKind.SelfTest => SelfTest(),
            _ => 64
        };
    }

    private async Task<int> RunScanAsync(ScanOptions options, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.OutputDirectory);
        bool isAdministrator = _privilegeDetector.IsAdministrator();
        if (!isAdministrator && !options.NoElevate && !options.QuickMode && OperatingSystem.IsWindows())
        {
            Console.WriteLine("Внимание: программа запущена без прав администратора. Недоступные разделы будут помечены как Skipped.");
            Console.WriteLine("Для полного режима перезапустите консоль от имени администратора или используйте будущий GUI/UAC-перезапуск.");
        }

        Console.WriteLine(options.QuickMode ? "Запуск быстрого анализа..." : "Запуск полного анализа каркаса...");
        CollectionRunResult runResult = await _scheduler.RunAsync(options, cancellationToken).ConfigureAwait(false);
        InspectionReport report = _reportFactory.Create(runResult);
        IReadOnlyList<ReportWriteResult> writtenReports = await _reportWriterService
            .WriteAsync(report, options.OutputDirectory, options.Formats, cancellationToken)
            .ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine("Готовые отчёты:");
        foreach (ReportWriteResult result in writtenReports)
        {
            Console.WriteLine($"  {result.Format}: {result.Path}");
        }

        if (options.OpenReport)
        {
            TryOpenHtml(writtenReports);
        }

        bool hasCriticalFinding = report.Findings.Any(static finding => finding.Severity == Severity.Critical);
        bool hasPartialError = report.Collectors.Any(static collector => collector.Status is CollectorStatus.Partial or CollectorStatus.Failed or CollectorStatus.TimedOut);
        if (hasCriticalFinding)
        {
            return 2;
        }

        return hasPartialError ? 1 : 0;
    }

    private async Task<int> RunDiffAsync(DiffOptions? options, CancellationToken cancellationToken)
    {
        if (options is null)
        {
            Console.Error.WriteLine("Не переданы параметры diff.");
            return 64;
        }

        string path = await _reportDiffService
            .WriteDiffAsync(options.OldReportPath, options.NewReportPath, options.OutputPath, cancellationToken)
            .ConfigureAwait(false);
        Console.WriteLine("Diff-отчёт сохранён: " + path);
        return 0;
    }

    private int ListCollectors()
    {
        Console.WriteLine($"{"ID",-28} {"Категория",-14} {"Приоритет",-10} {"Admin",-5} Таймаут Зависимости");
        Console.WriteLine(new string('-', 96));
        foreach (ICollector collector in _collectors.OrderBy(static item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            string dependencies = collector.DependsOn.Count == 0 ? "-" : string.Join(",", collector.DependsOn);
            Console.WriteLine($"{collector.Id,-28} {collector.Category,-14} {collector.Priority,-10} {collector.RequiresAdmin,-5} {collector.Timeout} {dependencies}");
        }

        return 0;
    }

    private int SelfTest()
    {
        int collectorCount = _collectors.Count();
        bool adminProbe = _privilegeDetector.IsAdministrator();
        _logger.LogInformation("Selftest: collectors={CollectorCount}, admin={IsAdministrator}", collectorCount, adminProbe);

        if (collectorCount == 0)
        {
            Console.Error.WriteLine("Selftest failed: не зарегистрированы сборщики.");
            return 1;
        }

        Console.WriteLine("Selftest passed: DI, сборщики, планировщик и отчётные сервисы доступны.");
        Console.WriteLine("Зарегистрировано сборщиков: " + collectorCount);
        return 0;
    }

    private static void TryOpenHtml(IReadOnlyList<ReportWriteResult> reports)
    {
        ReportWriteResult? html = reports.FirstOrDefault(static report => string.Equals(report.Format, "html", StringComparison.OrdinalIgnoreCase));
        if (html is null)
        {
            return;
        }

        try
        {
            ProcessStartInfo startInfo = new(html.Path)
            {
                UseShellExecute = true
            };
            Process.Start(startInfo);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Не удалось открыть HTML-отчёт: " + exception.Message);
        }
    }
}
