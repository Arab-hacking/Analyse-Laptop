using System.Globalization;
using System.Text.Json.Nodes;

namespace LaptopInspector.Reporting;

public static class ReportDataExplorer
{
    public static JsonNode? Select(JsonNode? root, params string[] path)
    {
        JsonNode? current = root;
        foreach (string segment in path)
        {
            if (current is null)
            {
                return null;
            }

            if (current is JsonObject obj)
            {
                current = TryGet(obj, segment, out JsonNode? child) ? child : null;
                continue;
            }

            if (current is JsonArray array)
            {
                if (segment == "*")
                {
                    current = array.FirstOrDefault();
                    continue;
                }

                if (int.TryParse(segment, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) && index >= 0 && index < array.Count)
                {
                    current = array[index];
                    continue;
                }

                current = array.FirstOrDefault(item => item is JsonObject objectItem && TryGet(objectItem, segment, out _));
                if (current is JsonObject foundObj && TryGet(foundObj, segment, out JsonNode? foundChild))
                {
                    current = foundChild;
                }
            }
        }

        return current;
    }

    public static IReadOnlyList<JsonNode?> SelectAll(JsonNode? root, IReadOnlyList<string> path)
    {
        List<JsonNode?> result = new();
        SelectAllCore(root, path, 0, result);
        return result;
    }

    public static string Text(JsonNode? node, string fallback = "нет данных")
    {
        if (node is null)
        {
            return fallback;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue(out string? stringValue))
            {
                return string.IsNullOrWhiteSpace(stringValue) ? fallback : stringValue;
            }

            if (value.TryGetValue(out bool boolValue))
            {
                return boolValue ? "да" : "нет";
            }

            if (value.TryGetValue(out long longValue))
            {
                return longValue.ToString("N0", CultureInfo.InvariantCulture);
            }

            if (value.TryGetValue(out double doubleValue))
            {
                return doubleValue.ToString("0.###", CultureInfo.InvariantCulture);
            }
        }

        if (node is JsonArray array)
        {
            return array.Count.ToString(CultureInfo.InvariantCulture);
        }

        if (node is JsonObject obj)
        {
            return obj.Count.ToString(CultureInfo.InvariantCulture) + " полей";
        }

        return node.ToJsonString();
    }

    public static double? Number(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue(out double doubleValue))
            {
                return doubleValue;
            }

            if (value.TryGetValue(out long longValue))
            {
                return longValue;
            }

            if (value.TryGetValue(out string? text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    public static IReadOnlyList<string> StringSet(JsonNode? root, IReadOnlyList<string> path)
    {
        return SelectAll(root, path)
            .Select(item => Text(item, string.Empty))
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static int Count(JsonNode? root, params string[] path)
    {
        JsonNode? node = Select(root, path);
        return node switch
        {
            JsonArray array => array.Count,
            JsonObject obj => obj.Count,
            null => 0,
            _ => 1
        };
    }

    public static string FormatBytes(double? bytes)
    {
        if (!bytes.HasValue)
        {
            return "нет данных";
        }

        string[] units = { "Б", "КиБ", "МиБ", "ГиБ", "ТиБ", "ПиБ" };
        double value = bytes.Value;
        int unitIndex = 0;
        while (value >= 1024.0 && unitIndex + 1 < units.Length)
        {
            value /= 1024.0;
            unitIndex++;
        }

        return value.ToString(unitIndex == 0 ? "0" : "0.##", CultureInfo.InvariantCulture) + " " + units[unitIndex];
    }

    public static string FormatKilobytes(double? kilobytes)
    {
        return kilobytes.HasValue ? FormatBytes(kilobytes.Value * 1024.0) : "нет данных";
    }

    public static bool TryGet(JsonObject obj, string propertyName, out JsonNode? value)
    {
        if (obj.TryGetPropertyValue(propertyName, out value))
        {
            return true;
        }

        foreach (KeyValuePair<string, JsonNode?> property in obj)
        {
            if (string.Equals(property.Key, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    public static IEnumerable<KeyValuePair<string, string>> FlattenScalars(JsonNode? node, string prefix = "")
    {
        if (node is null)
        {
            yield break;
        }

        if (node is JsonValue)
        {
            yield return new KeyValuePair<string, string>(prefix, Text(node, string.Empty));
            yield break;
        }

        if (node is JsonObject obj)
        {
            foreach (KeyValuePair<string, JsonNode?> property in obj)
            {
                string nextPrefix = string.IsNullOrEmpty(prefix) ? property.Key : prefix + "." + property.Key;
                foreach (KeyValuePair<string, string> item in FlattenScalars(property.Value, nextPrefix))
                {
                    yield return item;
                }
            }

            yield break;
        }

        if (node is JsonArray array)
        {
            for (int index = 0; index < array.Count; index++)
            {
                string nextPrefix = prefix + "[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                foreach (KeyValuePair<string, string> item in FlattenScalars(array[index], nextPrefix))
                {
                    yield return item;
                }
            }
        }
    }

    private static void SelectAllCore(JsonNode? node, IReadOnlyList<string> path, int index, List<JsonNode?> result)
    {
        if (index >= path.Count)
        {
            result.Add(node);
            return;
        }

        if (node is null)
        {
            return;
        }

        string segment = path[index];
        if (segment == "*")
        {
            if (node is JsonArray array)
            {
                foreach (JsonNode? item in array)
                {
                    SelectAllCore(item, path, index + 1, result);
                }
            }
            else if (node is JsonObject obj)
            {
                foreach (KeyValuePair<string, JsonNode?> item in obj)
                {
                    SelectAllCore(item.Value, path, index + 1, result);
                }
            }

            return;
        }

        if (node is JsonObject objectNode && TryGet(objectNode, segment, out JsonNode? child))
        {
            SelectAllCore(child, path, index + 1, result);
            return;
        }

        if (node is JsonArray arrayNode)
        {
            if (int.TryParse(segment, NumberStyles.Integer, CultureInfo.InvariantCulture, out int arrayIndex) && arrayIndex >= 0 && arrayIndex < arrayNode.Count)
            {
                SelectAllCore(arrayNode[arrayIndex], path, index + 1, result);
            }
            else
            {
                foreach (JsonNode? item in arrayNode)
                {
                    SelectAllCore(item, path, index, result);
                }
            }
        }
    }
}
