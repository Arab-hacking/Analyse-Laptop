using System.Text.Json.Nodes;
using LaptopInspector.Core.Privacy;
using LaptopInspector.Core.Services;

namespace LaptopInspector.Collectors;

internal static class JsonPrivacyScrubber
{
    private static readonly string[] SensitiveNameFragments =
    {
        "serial",
        "uuid",
        "mac",
        "bssid",
        "ssid",
        "username",
        "userName",
        "identifyingNumber",
        "uniqueId",
        "instanceId",
        "pnpDeviceId",
        "devicePath",
        "productKey"
    };

    public static JsonNode Scrub(JsonNode node, CollectionContext context)
    {
        if (!context.Options.Anonymize)
        {
            return node;
        }

        ScrubNode(node, context.AnonymizationSalt, propertyName: null, forceMask: false);
        return node;
    }

    private static void ScrubNode(JsonNode? node, string salt, string? propertyName, bool forceMask)
    {
        if (node is null)
        {
            return;
        }

        bool maskCurrent = forceMask || IsSensitiveName(propertyName);
        if (node is JsonObject obj)
        {
            foreach (KeyValuePair<string, JsonNode?> property in obj.ToArray())
            {
                bool maskChild = maskCurrent || IsSensitiveName(property.Key);
                if (property.Value is JsonValue value && maskChild)
                {
                    obj[property.Key] = MaskValue(value, salt, property.Key);
                }
                else if (property.Value is JsonArray array && maskChild)
                {
                    MaskArray(array, salt, property.Key);
                }
                else
                {
                    ScrubNode(property.Value, salt, property.Key, maskChild);
                }
            }
        }
        else if (node is JsonArray array)
        {
            if (maskCurrent)
            {
                MaskArray(array, salt, propertyName ?? "value");
            }
            else
            {
                foreach (JsonNode? item in array)
                {
                    ScrubNode(item, salt, propertyName, forceMask: false);
                }
            }
        }
    }

    private static void MaskArray(JsonArray array, string salt, string propertyName)
    {
        for (int index = 0; index < array.Count; index++)
        {
            JsonNode? item = array[index];
            if (item is JsonValue value)
            {
                array[index] = MaskValue(value, salt, propertyName);
            }
            else
            {
                ScrubNode(item, salt, propertyName, forceMask: true);
            }
        }
    }

    private static JsonNode MaskValue(JsonValue value, string salt, string propertyName)
    {
        string raw;
        try
        {
            raw = value.GetValue<string>() ?? string.Empty;
        }
        catch (InvalidOperationException)
        {
            raw = value.ToJsonString();
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return JsonValue.Create(string.Empty)!;
        }

        return JsonValue.Create(PrivacyMasker.MaskIdentifier(raw, salt, NormalizePrefix(propertyName)))!;
    }

    private static bool IsSensitiveName(string? propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return false;
        }

        return SensitiveNameFragments.Any(fragment => propertyName.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizePrefix(string propertyName)
    {
        string prefix = new(propertyName.Where(char.IsLetterOrDigit).Take(24).ToArray());
        return prefix.Length == 0 ? "masked" : prefix.ToLowerInvariant();
    }
}
