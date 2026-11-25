using System.Text;
using LaptopInspector.Core.Models;

namespace LaptopInspector.Reporting;

public static class ReportNameBuilder
{
    public static string BuildBaseName(InspectionReport report)
    {
        string device = string.IsNullOrWhiteSpace(report.Metadata.ComputerName)
            ? "Device"
            : report.Metadata.ComputerName;
        string timestamp = report.Metadata.CreatedAt.ToLocalTime().ToString("yyyyMMdd_HHmmss");
        return "LaptopInspector_" + Sanitize(device) + "_" + timestamp;
    }

    private static string Sanitize(string value)
    {
        StringBuilder builder = new(value.Length);
        foreach (char character in value)
        {
            if (char.IsLetterOrDigit(character) || character is '-' or '_')
            {
                builder.Append(character);
            }
            else
            {
                builder.Append('_');
            }
        }

        string result = builder.ToString().Trim('_');
        return result.Length == 0 ? "Device" : result;
    }
}
