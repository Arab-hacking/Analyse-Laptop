using System.Text.Json.Nodes;

namespace LaptopInspector.Core.Models;

public enum CollectorPriority
{
    Critical = 0,
    High = 10,
    Normal = 20,
    Low = 30,
    Background = 40
}

public enum CollectorStatus
{
    Success,
    Partial,
    Skipped,
    Failed,
    TimedOut
}

public enum Severity
{
    Info,
    Low,
    Medium,
    High,
    Critical
}

public sealed record Finding(
    string RuleId,
    Severity Severity,
    string Category,
    string Title,
    string Details,
    string Recommendation,
    string Evidence);

public sealed record CollectorResult(
    string Id,
    CollectorStatus Status,
    TimeSpan Duration,
    JsonNode Data,
    IReadOnlyList<Finding> Findings,
    IReadOnlyList<string> Errors,
    string? SkipReason)
{
    public static CollectorResult Success(
        string id,
        TimeSpan duration,
        JsonNode? data = null,
        IReadOnlyList<Finding>? findings = null)
    {
        return new CollectorResult(
            id,
            CollectorStatus.Success,
            duration,
            data ?? new JsonObject(),
            findings ?? Array.Empty<Finding>(),
            Array.Empty<string>(),
            null);
    }

    public static CollectorResult Partial(
        string id,
        TimeSpan duration,
        JsonNode? data,
        IReadOnlyList<Finding>? findings,
        IReadOnlyList<string> errors)
    {
        return new CollectorResult(
            id,
            CollectorStatus.Partial,
            duration,
            data ?? new JsonObject(),
            findings ?? Array.Empty<Finding>(),
            errors,
            null);
    }

    public static CollectorResult Skipped(string id, TimeSpan duration, string reason)
    {
        return new CollectorResult(
            id,
            CollectorStatus.Skipped,
            duration,
            new JsonObject { ["reason"] = reason },
            Array.Empty<Finding>(),
            Array.Empty<string>(),
            reason);
    }

    public static CollectorResult Failed(string id, TimeSpan duration, string error)
    {
        return new CollectorResult(
            id,
            CollectorStatus.Failed,
            duration,
            new JsonObject { ["error"] = error },
            Array.Empty<Finding>(),
            new[] { error },
            null);
    }

    public static CollectorResult TimedOut(string id, TimeSpan duration, TimeSpan timeout)
    {
        string message = $"Сборщик превысил таймаут {timeout}.";
        return new CollectorResult(
            id,
            CollectorStatus.TimedOut,
            duration,
            new JsonObject { ["timeoutMs"] = (long)timeout.TotalMilliseconds },
            Array.Empty<Finding>(),
            new[] { message },
            message);
    }
}
