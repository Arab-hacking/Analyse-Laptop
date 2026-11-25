using System.Text;
using LaptopInspector.Cli;
using LaptopInspector.Cli.CommandLine;
using LaptopInspector.Collectors;
using LaptopInspector.Core;
using LaptopInspector.Reporting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        CommandLineParser parser = new();
        ParsedCommand parsedCommand;
        try
        {
            parsedCommand = parser.Parse(args);
        }
        catch (CommandLineException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(CommandLineParser.GetHelp());
            return exception.ExitCode;
        }

        if (parsedCommand.HelpRequested)
        {
            Console.WriteLine(CommandLineParser.GetHelp());
            return 0;
        }

        string logDirectory = ResolveLogDirectory(parsedCommand);
        Directory.CreateDirectory(logDirectory);
        ConfigureSerilog(logDirectory, parsedCommand.ScanOptions.Verbose);

        try
        {
            ServiceCollection services = new();
            services.AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.AddSerilog(Log.Logger, dispose: true);
            });
            services.AddLaptopInspectorCore();
            services.AddLaptopInspectorCollectors();
            services.AddLaptopInspectorReporting();
            services.AddSingleton<CliApplication>();

            await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
            CliApplication application = provider.GetRequiredService<CliApplication>();
            return await application.ExecuteAsync(parsedCommand, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Фатальная ошибка LaptopInspector.");
            Console.Error.WriteLine("Фатальная ошибка: " + exception.Message);
            return 3;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static string ResolveLogDirectory(ParsedCommand command)
    {
        if (command.Kind == LaptopInspector.Core.Models.CliCommandKind.Diff && command.DiffOptions is not null)
        {
            return Path.GetDirectoryName(command.DiffOptions.OutputPath) ?? Directory.GetCurrentDirectory();
        }

        return command.ScanOptions.OutputDirectory;
    }

    private static void ConfigureSerilog(string outputDirectory, bool verbose)
    {
        string logPath = Path.Combine(outputDirectory, "LaptopInspector.log");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(verbose ? LogEventLevel.Debug : LogEventLevel.Information)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Infinite,
                shared: true,
                retainedFileCountLimit: 3,
                outputTemplate: "[{Timestamp:O} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }
}
