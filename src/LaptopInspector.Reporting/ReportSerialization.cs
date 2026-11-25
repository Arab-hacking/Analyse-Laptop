using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using LaptopInspector.Core.Models;

namespace LaptopInspector.Reporting;

public static class ReportSerialization
{
    public static JsonSerializerOptions CreateOptions(bool indented = true)
    {
        JsonSerializerOptions options = new()
        {
            WriteIndented = indented,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter<CollectorPriority>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<CollectorStatus>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new JsonStringEnumConverter<Severity>(JsonNamingPolicy.CamelCase));
        return options;
    }
}
