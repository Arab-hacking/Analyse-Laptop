using System.Collections.Concurrent;
using System.Diagnostics;
using LaptopInspector.Core.Abstractions;
using LaptopInspector.Core.Models;
using Microsoft.Extensions.Logging;

namespace LaptopInspector.Core.Services;

public sealed class CollectionScheduler
{
    private readonly IReadOnlyList<ICollector> _collectors;
    private readonly IPrivilegeDetector _privilegeDetector;
    private readonly ILogger<CollectionScheduler> _logger;

    public CollectionScheduler(
        IEnumerable<ICollector> collectors,
        IPrivilegeDetector privilegeDetector,
        ILogger<CollectionScheduler> logger)
    {
        _collectors = collectors
            .OrderBy(static collector => collector.Priority)
            .ThenBy(static collector => collector.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _privilegeDetector = privilegeDetector;
        _logger = logger;
    }

    public IReadOnlyList<ICollector> RegisteredCollectors => _collectors;

    public async Task<CollectionRunResult> RunAsync(ScanOptions options, CancellationToken cancellationToken)
    {
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        bool isAdministrator = _privilegeDetector.IsAdministrator();
        CollectionContext context = new(options, isAdministrator);
        IReadOnlyList<ICollector> selectedCollectors = SelectCollectors(options);
        ConcurrentDictionary<string, CollectorResult> completed = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ICollector> remaining = selectedCollectors.ToDictionary(
            static collector => collector.Id,
            StringComparer.OrdinalIgnoreCase);

        using CancellationTokenSource globalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (options.GlobalTimeout > TimeSpan.Zero)
        {
            globalCts.CancelAfter(options.GlobalTimeout);
        }

        _logger.LogInformation(
            "Запуск сбора: collectors={CollectorCount}, parallelism={Parallelism}, admin={IsAdmin}",
            remaining.Count,
            options.Parallelism,
            isAdministrator);

        try
        {
            while (remaining.Count > 0)
            {
                globalCts.Token.ThrowIfCancellationRequested();

                List<ICollector> ready = remaining.Values
                    .Where(collector => collector.DependsOn.All(completed.ContainsKey))
                    .OrderBy(static collector => collector.Priority)
                    .ThenBy(static collector => collector.Id, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (ready.Count == 0)
                {
                    MarkUnresolvableCollectors(remaining, completed);
                    break;
                }

                using SemaphoreSlim gate = new(Math.Max(1, options.Parallelism));
                Task<CollectorResult>[] tasks = ready
                    .Select(collector => RunWithGateAsync(collector, context, completed, gate, globalCts.Token))
                    .ToArray();

                CollectorResult[] levelResults = await Task.WhenAll(tasks).ConfigureAwait(false);
                foreach (CollectorResult result in levelResults)
                {
                    completed[result.Id] = result;
                    remaining.Remove(result.Id);
                }
            }
        }
        catch (OperationCanceledException) when (globalCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Глобальный таймаут сбора {Timeout} истёк.", options.GlobalTimeout);
            foreach (ICollector collector in remaining.Values)
            {
                completed[collector.Id] = CollectorResult.TimedOut(collector.Id, TimeSpan.Zero, options.GlobalTimeout);
            }
        }

        TimeSpan duration = DateTimeOffset.UtcNow - startedAt;
        CollectorResult[] orderedResults = completed.Values
            .OrderBy(static result => result.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new CollectionRunResult(context, orderedResults, duration);
    }

    private IReadOnlyList<ICollector> SelectCollectors(ScanOptions options)
    {
        IEnumerable<ICollector> query = _collectors;

        if (options.Include.Count > 0)
        {
            query = query.Where(collector => options.Include.Any(pattern => Matches(pattern, collector.Id)));
        }

        if (options.Exclude.Count > 0)
        {
            query = query.Where(collector => !options.Exclude.Any(pattern => Matches(pattern, collector.Id)));
        }

        if (options.QuickMode)
        {
            query = query.Where(static collector => collector.Priority <= CollectorPriority.Normal);
        }

        ICollector[] selected = query.ToArray();
        string[] duplicates = selected
            .GroupBy(static collector => collector.Id, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();

        if (duplicates.Length > 0)
        {
            throw new InvalidOperationException("Повторяющиеся идентификаторы сборщиков: " + string.Join(", ", duplicates));
        }

        return selected;
    }

    private static bool Matches(string pattern, string value)
    {
        if (string.Equals(pattern, value, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!pattern.Contains('*', StringComparison.Ordinal))
        {
            return false;
        }

        string[] parts = pattern.Split('*', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int position = 0;
        foreach (string part in parts)
        {
            int index = value.IndexOf(part, position, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return false;
            }

            position = index + part.Length;
        }

        return pattern.StartsWith('*') || value.StartsWith(parts.FirstOrDefault() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static void MarkUnresolvableCollectors(
        Dictionary<string, ICollector> remaining,
        ConcurrentDictionary<string, CollectorResult> completed)
    {
        foreach (ICollector collector in remaining.Values.ToArray())
        {
            string dependencies = collector.DependsOn.Count == 0
                ? "нет доступных зависимостей"
                : string.Join(", ", collector.DependsOn);
            completed[collector.Id] = CollectorResult.Failed(
                collector.Id,
                TimeSpan.Zero,
                "Не удалось разрешить зависимости сборщика: " + dependencies);
            remaining.Remove(collector.Id);
        }
    }

    private async Task<CollectorResult> RunWithGateAsync(
        ICollector collector,
        CollectionContext context,
        ConcurrentDictionary<string, CollectorResult> completed,
        SemaphoreSlim gate,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RunCollectorAsync(collector, context, completed, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<CollectorResult> RunCollectorAsync(
        ICollector collector,
        CollectionContext context,
        ConcurrentDictionary<string, CollectorResult> completed,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        if (collector.RequiresAdmin && !context.IsAdministrator)
        {
            string reason = "Требуются права администратора.";
            _logger.LogInformation("Сборщик {CollectorId} пропущен: {Reason}", collector.Id, reason);
            return CollectorResult.Skipped(collector.Id, stopwatch.Elapsed, reason);
        }

        string[] blockingDependencies = collector.DependsOn
            .Where(dependency => completed.TryGetValue(dependency, out CollectorResult? result) &&
                result.Status is not (CollectorStatus.Success or CollectorStatus.Partial))
            .ToArray();

        if (blockingDependencies.Length > 0)
        {
            string reason = "Зависимости завершились неуспешно: " + string.Join(", ", blockingDependencies);
            _logger.LogWarning("Сборщик {CollectorId} пропущен: {Reason}", collector.Id, reason);
            return CollectorResult.Skipped(collector.Id, stopwatch.Elapsed, reason);
        }

        using CancellationTokenSource collectorCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        TimeSpan timeout = collector.Timeout > TimeSpan.Zero ? collector.Timeout : TimeSpan.FromSeconds(30);
        Task<CollectorResult> collectTask;

        try
        {
            collectTask = collector.CollectAsync(context, collectorCts.Token);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            _logger.LogError(exception, "Сборщик {CollectorId} завершился синхронной ошибкой.", collector.Id);
            return CollectorResult.Failed(collector.Id, stopwatch.Elapsed, exception.Message);
        }

        Task delayTask = Task.Delay(timeout, cancellationToken);
        Task completedTask = await Task.WhenAny(collectTask, delayTask).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (completedTask == delayTask)
        {
            collectorCts.Cancel();
            _ = collectTask.ContinueWith(
                static task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
            stopwatch.Stop();
            _logger.LogWarning("Сборщик {CollectorId} превысил таймаут {Timeout}.", collector.Id, timeout);
            return CollectorResult.TimedOut(collector.Id, stopwatch.Elapsed, timeout);
        }

        try
        {
            CollectorResult result = await collectTask.ConfigureAwait(false);
            stopwatch.Stop();
            return result with { Duration = stopwatch.Elapsed };
        }
        catch (OperationCanceledException) when (collectorCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            _logger.LogWarning("Сборщик {CollectorId} отменён по таймауту {Timeout}.", collector.Id, timeout);
            return CollectorResult.TimedOut(collector.Id, stopwatch.Elapsed, timeout);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            _logger.LogError(exception, "Сборщик {CollectorId} завершился ошибкой.", collector.Id);
            return CollectorResult.Failed(collector.Id, stopwatch.Elapsed, exception.Message);
        }
    }
}

public sealed record CollectionRunResult(
    CollectionContext Context,
    IReadOnlyList<CollectorResult> Results,
    TimeSpan Duration);
