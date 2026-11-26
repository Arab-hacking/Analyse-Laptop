using System.Text.Json.Nodes;
using LaptopInspector.Core.Models;

namespace LaptopInspector.Collectors;

internal static class CollectorJsonUtilities
{
    public static JsonNode ParseOrWrap(string? json, string wrapperName = "rawOutput")
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new JsonObject
            {
                ["status"] = "empty",
                ["reason"] = "Команда не вернула данных."
            };
        }

        try
        {
            return JsonNode.Parse(json) ?? new JsonObject();
        }
        catch (Exception exception)
        {
            return new JsonObject
            {
                ["status"] = "parseFailed",
                ["parseError"] = exception.Message,
                [wrapperName] = json
            };
        }
    }

    public static bool HasStatus(JsonNode node, string expectedStatus)
    {
        return string.Equals(ReadString(node, "status"), expectedStatus, StringComparison.OrdinalIgnoreCase);
    }

    public static string? ReadString(JsonNode node, string propertyName)
    {
        if (node is JsonObject obj && obj.TryGetPropertyValue(propertyName, out JsonNode? value))
        {
            // GetValue<string>() бросает InvalidOperationException на не-строковых значениях,
            // а данные приходят из внешних источников (PowerShell/нативная DLL).
            if (value is JsonValue jsonValue && jsonValue.TryGetValue(out string? text))
            {
                return text;
            }
        }

        return null;
    }

    public static CollectorResult ResultFromStatus(
        string id,
        TimeSpan duration,
        JsonNode data,
        IReadOnlyList<Finding>? findings,
        IReadOnlyList<string>? errors,
        string? skipReason = null)
    {
        IReadOnlyList<Finding> safeFindings = findings ?? Array.Empty<Finding>();
        IReadOnlyList<string> safeErrors = errors ?? Array.Empty<string>();
        if (!string.IsNullOrWhiteSpace(skipReason))
        {
            return new CollectorResult(id, CollectorStatus.Skipped, duration, data, safeFindings, safeErrors, skipReason);
        }

        if (safeErrors.Count > 0)
        {
            return new CollectorResult(id, CollectorStatus.Partial, duration, data, safeFindings, safeErrors, null);
        }

        return CollectorResult.Success(id, duration, data, safeFindings);
    }
}
