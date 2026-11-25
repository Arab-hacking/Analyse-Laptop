using System.Text.RegularExpressions;

namespace LaptopInspector.Core.Privacy;

public static partial class SensitiveValueRedactor
{
    public static string RedactCommandLine(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return string.Empty;
        }

        string result = AuthorizationHeaderRegex().Replace(commandLine, "$1<redacted>");
        result = BearerTokenRegex().Replace(result, "$1<redacted>");
        result = SecretAssignmentRegex().Replace(result, "$1<redacted>");
        return result;
    }

    [GeneratedRegex("(?i)((?:password|passwd|pwd|token|secret|api[-_]?key|--key|/key|authorization)\\s*[:=]\\s*)([^\\s;]+)", RegexOptions.CultureInvariant)]
    private static partial Regex SecretAssignmentRegex();

    [GeneratedRegex("(?i)(Authorization\\s*:\\s*(?:Bearer|Basic)\\s+)[^\\s;]+", RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationHeaderRegex();

    [GeneratedRegex("(?i)(bearer\\s+)[A-Za-z0-9._~+/=-]+", RegexOptions.CultureInvariant)]
    private static partial Regex BearerTokenRegex();
}
