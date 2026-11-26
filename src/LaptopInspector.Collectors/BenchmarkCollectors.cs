using System.Diagnostics;
using System.Text.Json.Nodes;
using LaptopInspector.Core.Abstractions;
using LaptopInspector.Core.Models;
using LaptopInspector.Core.Services;

namespace LaptopInspector.Collectors;

public sealed class SyntheticBenchmarkCollector : ICollector
{
    private const int MemoryBufferBytes = 64 * 1024 * 1024;
    private const int DiskBenchmarkBytes = 128 * 1024 * 1024;

    public string Id => "benchmark.synthetic";

    public string Category => "Performance";

    public CollectorPriority Priority => CollectorPriority.Background;

    public bool RequiresAdmin => false;

    public TimeSpan Timeout => TimeSpan.FromMinutes(10);

    public IReadOnlyList<string> DependsOn => Array.Empty<string>();

    public async Task<CollectorResult> CollectAsync(CollectionContext context, CancellationToken cancellationToken)
    {
        if (!context.Options.Benchmark)
        {
            return CollectorResult.Skipped(Id, TimeSpan.Zero, "Бенчмарки отключены. Используйте --benchmark.");
        }

        Stopwatch total = Stopwatch.StartNew();
        JsonObject data = new()
        {
            ["source"] = "managed synthetic benchmarks",
            ["warning"] = "Результаты являются ориентировочными и зависят от питания, температуры и фоновой нагрузки.",
            ["cpuSingleThread"] = RunCpuBenchmark(threadCount: 1, TimeSpan.FromSeconds(1), cancellationToken),
            ["cpuMultiThread"] = await RunCpuMultiThreadAsync(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false),
            ["memory"] = RunMemoryBenchmark(cancellationToken),
            ["disk"] = await RunOptionalDiskBenchmarkAsync(context, cancellationToken).ConfigureAwait(false)
        };
        total.Stop();
        return CollectorResult.Success(Id, total.Elapsed, data);
    }

    private static JsonObject RunCpuBenchmark(int threadCount, TimeSpan duration, CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        ulong checksum = 1469598103934665603UL;
        long iterations = 0;
        while (stopwatch.Elapsed < duration)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int index = 0; index < 1024; index++)
            {
                checksum ^= (ulong)(index + iterations + threadCount);
                checksum *= 1099511628211UL;
                checksum = (checksum << 7) | (checksum >> 57);
            }

            iterations += 1024;
        }

        stopwatch.Stop();
        double iterationsPerSecond = iterations / Math.Max(0.001, stopwatch.Elapsed.TotalSeconds);
        return new JsonObject
        {
            ["threads"] = threadCount,
            ["durationMs"] = stopwatch.Elapsed.TotalMilliseconds,
            ["iterations"] = iterations,
            ["iterationsPerSecond"] = iterationsPerSecond,
            ["score"] = Math.Round(iterationsPerSecond / 1_000_000.0, 3),
            ["checksum"] = checksum.ToString("x16", System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    private static async Task<JsonObject> RunCpuMultiThreadAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        int threads = Math.Max(1, Environment.ProcessorCount);
        Task<JsonObject>[] tasks = Enumerable.Range(0, threads)
            .Select(_ => Task.Run(() => RunCpuBenchmark(threads, duration, cancellationToken), cancellationToken))
            .ToArray();
        JsonObject[] results = await Task.WhenAll(tasks).ConfigureAwait(false);
        double totalIps = results.Sum(result => result["iterationsPerSecond"]?.GetValue<double>() ?? 0.0);
        return new JsonObject
        {
            ["threads"] = threads,
            ["totalIterationsPerSecond"] = totalIps,
            ["score"] = Math.Round(totalIps / 1_000_000.0, 3),
            ["workers"] = new JsonArray(results.Select(result => result.DeepClone()).ToArray())
        };
    }

    private static JsonObject RunMemoryBenchmark(CancellationToken cancellationToken)
    {
        byte[] source = new byte[MemoryBufferBytes];
        byte[] destination = new byte[MemoryBufferBytes];
        for (int index = 0; index < source.Length; index += 4096)
        {
            source[index] = (byte)(index & 0xFF);
        }

        Stopwatch copyWatch = Stopwatch.StartNew();
        const int passes = 8;
        for (int pass = 0; pass < passes; pass++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Buffer.BlockCopy(source, 0, destination, 0, source.Length);
        }

        copyWatch.Stop();
        double bytesCopied = (double)MemoryBufferBytes * passes;
        double gibPerSecond = bytesCopied / Math.Max(0.001, copyWatch.Elapsed.TotalSeconds) / 1024.0 / 1024.0 / 1024.0;

        int[] chase = Enumerable.Range(0, 1_000_000).Select(index => (index * 167 + 13) % 1_000_000).ToArray();
        int cursor = 0;
        Stopwatch latencyWatch = Stopwatch.StartNew();
        for (int index = 0; index < 8_000_000; index++)
        {
            cursor = chase[cursor];
        }

        latencyWatch.Stop();
        return new JsonObject
        {
            ["copyBytes"] = bytesCopied,
            ["copyDurationMs"] = copyWatch.Elapsed.TotalMilliseconds,
            ["copyGibPerSecond"] = Math.Round(gibPerSecond, 3),
            ["pointerChaseSteps"] = 8_000_000,
            ["pointerChaseDurationMs"] = latencyWatch.Elapsed.TotalMilliseconds,
            ["pointerChaseNanosecondsPerStep"] = Math.Round(latencyWatch.Elapsed.TotalMilliseconds * 1_000_000.0 / 8_000_000.0, 3),
            ["checksum"] = cursor
        };
    }

    private static async Task<JsonObject> RunOptionalDiskBenchmarkAsync(CollectionContext context, CancellationToken cancellationToken)
    {
        string confirmed = Environment.GetEnvironmentVariable("LAPTOPINSPECTOR_CONFIRM_DISK_BENCHMARK") ?? string.Empty;
        if (!string.Equals(confirmed, "1", StringComparison.Ordinal))
        {
            return new JsonObject
            {
                ["status"] = "skipped",
                ["reason"] = "Дисковый benchmark пишет временный файл и требует подтверждения: установите LAPTOPINSPECTOR_CONFIRM_DISK_BENCHMARK=1 вместе с --benchmark."
            };
        }

        Directory.CreateDirectory(context.Options.OutputDirectory);
        long availableBytes = -1;
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(context.Options.OutputDirectory));
            if (!string.IsNullOrEmpty(root))
            {
                availableBytes = new DriveInfo(root).AvailableFreeSpace;
            }
        }
        catch (ArgumentException)
        {
            // UNC/непривязанный к тому путь: DriveInfo недоступен, проверку места пропускаем.
            availableBytes = -1;
        }
        catch (IOException)
        {
            availableBytes = -1;
        }
        catch (UnauthorizedAccessException)
        {
            availableBytes = -1;
        }

        if (availableBytes >= 0 && availableBytes < DiskBenchmarkBytes * 3L)
        {
            return new JsonObject
            {
                ["status"] = "skipped",
                ["reason"] = "Недостаточно свободного места для безопасного временного benchmark-файла.",
                ["availableBytes"] = availableBytes
            };
        }

        string path = Path.Combine(context.Options.OutputDirectory, "LaptopInspector_disk_benchmark.tmp");
        byte[] buffer = new byte[1024 * 1024];
        new Random(42).NextBytes(buffer);
        Stopwatch writeWatch = Stopwatch.StartNew();
        try
        {
            await using (FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length, FileOptions.SequentialScan | FileOptions.WriteThrough | FileOptions.DeleteOnClose))
            {
                int written = 0;
                while (written < DiskBenchmarkBytes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await stream.WriteAsync(buffer.AsMemory(0, Math.Min(buffer.Length, DiskBenchmarkBytes - written)), cancellationToken).ConfigureAwait(false);
                    written += Math.Min(buffer.Length, DiskBenchmarkBytes - written);
                }

                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            writeWatch.Stop();
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        double mibPerSecond = DiskBenchmarkBytes / Math.Max(0.001, writeWatch.Elapsed.TotalSeconds) / 1024.0 / 1024.0;
        return new JsonObject
        {
            ["status"] = "completed",
            ["fileSizeBytes"] = DiskBenchmarkBytes,
            ["writeDurationMs"] = writeWatch.Elapsed.TotalMilliseconds,
            ["sequentialWriteMibPerSecond"] = Math.Round(mibPerSecond, 3),
            ["temporaryFileDeleted"] = !File.Exists(path)
        };
    }
}

public sealed class PowerEnergyCollector : ICollector
{
    public string Id => "power.energy";

    public string Category => "Power";

    public CollectorPriority Priority => CollectorPriority.Background;

    public bool RequiresAdmin => false;

    public TimeSpan Timeout => TimeSpan.FromSeconds(95);

    public IReadOnlyList<string> DependsOn => Array.Empty<string>();

    public async Task<CollectorResult> CollectAsync(CollectionContext context, CancellationToken cancellationToken)
    {
        if (!context.Options.Energy)
        {
            return CollectorResult.Skipped(Id, TimeSpan.Zero, "powercfg /energy отключён. Используйте --energy.");
        }

        if (!OperatingSystem.IsWindows())
        {
            return CollectorResult.Skipped(Id, TimeSpan.Zero, "powercfg /energy доступен только на Windows.");
        }

        Directory.CreateDirectory(context.Options.OutputDirectory);
        string outputPath = Path.Combine(context.Options.OutputDirectory, "powercfg_energy.xml");
        CommandExecutionResult result = await CommandRunner.RunAsync(
            "powercfg.exe",
            new[] { "/energy", "/duration", "60", "/output", outputPath, "/xml" },
            Timeout,
            cancellationToken).ConfigureAwait(false);

        JsonObject data = new()
        {
            ["source"] = "powercfg /energy /xml",
            ["outputPath"] = outputPath,
            ["exitCode"] = result.ExitCode,
            ["timedOut"] = result.TimedOut,
            ["durationMs"] = result.Duration.TotalMilliseconds,
            ["standardError"] = result.StandardError,
            ["standardOutput"] = result.StandardOutput,
            ["fileExists"] = File.Exists(outputPath)
        };

        if (File.Exists(outputPath))
        {
            string xml = await File.ReadAllTextAsync(outputPath, cancellationToken).ConfigureAwait(false);
            data["xmlLength"] = xml.Length;
            data["errorCount"] = CountOrdinal(xml, "<Error>");
            data["warningCount"] = CountOrdinal(xml, "<Warning>");
            data["informationalCount"] = CountOrdinal(xml, "<Informational>");
        }

        return result.ExitCode == 0 || File.Exists(outputPath)
            ? CollectorResult.Success(Id, result.Duration, data)
            : CollectorResult.Partial(Id, result.Duration, data, Array.Empty<Finding>(), new[] { result.StandardError });
    }

    private static int CountOrdinal(string text, string needle)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}

public sealed class DeepDiskCollector : ICollector
{
    public string Id => "storage.deepDisk";

    public string Category => "Storage";

    public CollectorPriority Priority => CollectorPriority.Background;

    public bool RequiresAdmin => false;

    public TimeSpan Timeout => TimeSpan.FromMinutes(3);

    public IReadOnlyList<string> DependsOn => Array.Empty<string>();

    public Task<CollectorResult> CollectAsync(CollectionContext context, CancellationToken cancellationToken)
    {
        if (!context.Options.DeepDisk)
        {
            return Task.FromResult(CollectorResult.Skipped(Id, TimeSpan.Zero, "Deep disk отключён. Используйте --deep-disk."));
        }

        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(CollectorResult.Skipped(Id, TimeSpan.Zero, "Deep disk collector ориентирован на системный диск Windows."));
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string root = Path.GetPathRoot(windows) ?? "C:\\";
        List<JsonObject> rows = new();
        foreach (string directory in Directory.EnumerateDirectories(root).Take(256))
        {
            cancellationToken.ThrowIfCancellationRequested();
            long size = SafeDirectorySize(directory, stopwatch, TimeSpan.FromMinutes(2), cancellationToken);
            rows.Add(new JsonObject
            {
                ["path"] = directory,
                ["sizeBytes"] = size,
                ["sizeHuman"] = FormatBytes(size)
            });
            if (stopwatch.Elapsed > TimeSpan.FromMinutes(2))
            {
                break;
            }
        }

        JsonArray top = new(rows.OrderByDescending(row => row["sizeBytes"]?.GetValue<long>() ?? 0).Take(20).Select(row => row.DeepClone()).ToArray());
        JsonObject data = new()
        {
            ["source"] = "System.IO directory enumeration",
            ["root"] = root,
            ["partial"] = stopwatch.Elapsed > TimeSpan.FromMinutes(2),
            ["topDirectories"] = top
        };
        stopwatch.Stop();
        return Task.FromResult(CollectorResult.Success(Id, stopwatch.Elapsed, data));
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "Б", "КиБ", "МиБ", "ГиБ", "ТиБ", "ПиБ" };
        double value = bytes;
        int unitIndex = 0;
        while (value >= 1024.0 && unitIndex + 1 < units.Length)
        {
            value /= 1024.0;
            unitIndex++;
        }

        return value.ToString(unitIndex == 0 ? "0" : "0.##", System.Globalization.CultureInfo.InvariantCulture) + " " + units[unitIndex];
    }

    private static long SafeDirectorySize(string directory, Stopwatch stopwatch, TimeSpan budget, CancellationToken cancellationToken)
    {
        long total = 0;
        Stack<string> stack = new();
        stack.Push(directory);
        while (stack.Count > 0 && stopwatch.Elapsed < budget)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string current = stack.Pop();
            try
            {
                foreach (string file in Directory.EnumerateFiles(current))
                {
                    try
                    {
                        total += new FileInfo(file).Length;
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }

                foreach (string child in Directory.EnumerateDirectories(current))
                {
                    stack.Push(child);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return total;
    }
}

public sealed class OptionalDriverProbeCollector : WindowsPowerShellCollectorBase
{
    public override string Id => "optional.driverProbe";

    public override string Category => "Optional";

    public override CollectorPriority Priority => CollectorPriority.Low;

    public override TimeSpan Timeout => TimeSpan.FromSeconds(15);

    protected override string BuildScript(CollectionContext context)
    {
        return """
$names = @('PawnIO','WinRing0','WinRing0_1_2_0','InpOutx64','RwDrv','OpenLibSys')
$services = foreach ($name in $names) {
  Get-CimInstance Win32_SystemDriver -Filter "Name='$name'" -ErrorAction SilentlyContinue | Select-Object Name,DisplayName,State,Started,StartMode,PathName,ServiceType
}
$devices = Get-CimInstance Win32_PnPEntity -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'PawnIO|WinRing|OpenLibSys|InpOut|RWEverything' -or $_.PNPDeviceID -match 'PawnIO|WinRing|OpenLibSys|InpOut' } | Select-Object Name,PNPDeviceID,Manufacturer,Status,ConfigManagerErrorCode
[ordered]@{
  source = 'CIM Win32_SystemDriver + Win32_PnPEntity'
  policy = 'Опциональные ring-0 драйверы не устанавливаются и не открываются; выполняется только read-only probe наличия.'
  knownDriverServices = $services
  matchingDevices = $devices
}
""";
    }
}
