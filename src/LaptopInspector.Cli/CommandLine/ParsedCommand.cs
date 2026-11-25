using LaptopInspector.Core.Models;

namespace LaptopInspector.Cli.CommandLine;

public sealed record ParsedCommand(
    CliCommandKind Kind,
    ScanOptions ScanOptions,
    DiffOptions? DiffOptions,
    bool HelpRequested);

public sealed class CommandLineException : Exception
{
    public CommandLineException(string message)
        : base(message)
    {
    }

    public int ExitCode => 64;
}
