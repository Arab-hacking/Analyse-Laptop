namespace LaptopInspector.Core.Models;

public enum CliCommandKind
{
    Scan,
    Quick,
    Diff,
    ListCollectors,
    SelfTest,
    Help
}

public sealed record ScanOptions
{
    public string OutputDirectory { get; init; } = AppContext.BaseDirectory;

    public IReadOnlyList<string> Formats { get; init; } = new[] { "html", "json", "txt" };

    public IReadOnlyList<string> Include { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Exclude { get; init; } = Array.Empty<string>();

    public int Days { get; init; } = 30;

    public bool Benchmark { get; init; }

    public bool Energy { get; init; }

    public bool Anonymize { get; init; }

    public bool History { get; init; }

    public bool DeepDisk { get; init; }

    public bool Online { get; init; }

    public bool Raw { get; init; }

    public string Language { get; init; } = "ru";

    public TimeSpan GlobalTimeout { get; init; } = TimeSpan.FromMinutes(5);

    public int Parallelism { get; init; } = Math.Max(1, Environment.ProcessorCount);

    public bool NoElevate { get; init; }

    public bool Verbose { get; init; }

    public bool OpenReport { get; init; }

    public bool QuickMode { get; init; }
}

public sealed record DiffOptions
{
    public string OldReportPath { get; init; } = string.Empty;

    public string NewReportPath { get; init; } = string.Empty;

    public string OutputPath { get; init; } = "diff.html";
}
