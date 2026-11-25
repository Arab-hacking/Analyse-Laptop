using System.Collections.Concurrent;
using LaptopInspector.Core.Models;

namespace LaptopInspector.Core.Services;

public sealed class CollectionContext
{
    private readonly ConcurrentDictionary<string, object> _items = new(StringComparer.OrdinalIgnoreCase);

    public CollectionContext(ScanOptions options, bool isAdministrator)
    {
        Options = options;
        IsAdministrator = isAdministrator;
        StartedAt = DateTimeOffset.UtcNow;
        RunId = Guid.NewGuid().ToString("N");
        AnonymizationSalt = Guid.NewGuid().ToString("N");
    }

    public ScanOptions Options { get; }

    public bool IsAdministrator { get; }

    public DateTimeOffset StartedAt { get; }

    public string RunId { get; }

    public string AnonymizationSalt { get; }

    public void Set<T>(string key, T value)
        where T : notnull
    {
        _items[key] = value;
    }

    public bool TryGet<T>(string key, out T? value)
    {
        if (_items.TryGetValue(key, out object? raw) && raw is T typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }
}
