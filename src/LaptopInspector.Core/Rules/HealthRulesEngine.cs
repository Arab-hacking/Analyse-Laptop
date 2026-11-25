using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using LaptopInspector.Core.Models;
using Microsoft.Extensions.Logging;

namespace LaptopInspector.Core.Rules;

public sealed class HealthRulesEngine
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();
    private readonly ILogger<HealthRulesEngine> _logger;
    private readonly Lazy<RuleFile> _rules;

    public HealthRulesEngine(ILogger<HealthRulesEngine> logger)
    {
        _logger = logger;
        _rules = new Lazy<RuleFile>(LoadRules, isThreadSafe: true);
    }

    public HealthEvaluation Evaluate(
        JsonObject data,
        IReadOnlyList<CollectorResult> collectors,
        IReadOnlyList<Finding> existingFindings)
    {
        RuleFile ruleFile = _rules.Value;
        List<Finding> ruleFindings = new();
        Dictionary<string, int> penalties = CreatePenaltyBuckets();

        foreach (HealthRuleDefinition rule in ruleFile.Rules.Where(static item => item.Enabled))
        {
            RuleMatch match;
            try
            {
                match = EvaluateRule(rule, data, collectors);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Ошибка оценки правила {RuleId}.", rule.Id);
                continue;
            }

            if (!match.Matched)
            {
                continue;
            }

            ruleFindings.Add(new Finding(
                rule.Id,
                rule.Severity,
                rule.Category,
                rule.Title,
                Render(rule.Details, match),
                Render(rule.Recommendation, match),
                match.Evidence));

            string bucket = NormalizeBucket(rule.ScoreBucket);
            penalties[bucket] += rule.Penalty ?? DefaultPenalty(rule.Severity);
        }

        foreach (Finding finding in existingFindings)
        {
            string bucket = ResolveBucketFromCategory(finding.Category);
            penalties[bucket] += DefaultPenalty(finding.Severity);
        }

        Finding[] findings = existingFindings
            .Concat(ruleFindings)
            .OrderByDescending(static finding => finding.Severity)
            .ThenBy(static finding => finding.RuleId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        ReportSummary summary = BuildSummary(ruleFile.ScoreWeights, penalties);
        return new HealthEvaluation(findings, summary, penalties, ruleFile.LoadErrors);
    }

    private RuleFile LoadRules()
    {
        List<string> loadErrors = new();
        string[] candidatePaths =
        {
            Path.Combine(AppContext.BaseDirectory, HealthRuleCatalog.RulesFileName),
            Path.Combine(Directory.GetCurrentDirectory(), HealthRuleCatalog.RulesFileName),
            Path.Combine(Directory.GetCurrentDirectory(), "config", HealthRuleCatalog.RulesFileName)
        };

        foreach (string path in candidatePaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                string json = File.ReadAllText(path);
                RuleFile? loaded = JsonSerializer.Deserialize<RuleFile>(json, SerializerOptions);
                if (loaded is not null && loaded.Rules.Count > 0)
                {
                    _logger.LogInformation("Health rules loaded from {Path}: {Count}", path, loaded.Rules.Count);
                    return loaded with { LoadErrors = loadErrors };
                }

                loadErrors.Add("Файл правил пуст или не содержит rules: " + path);
            }
            catch (Exception exception)
            {
                loadErrors.Add("Не удалось загрузить rules.json из " + path + ": " + exception.Message);
            }
        }

        try
        {
            RuleFile? fallback = JsonSerializer.Deserialize<RuleFile>(HealthRuleCatalog.DefaultRulesJson, SerializerOptions);
            if (fallback is not null)
            {
                if (loadErrors.Count > 0)
                {
                    _logger.LogWarning("Используются встроенные health rules после ошибок загрузки: {Errors}", string.Join("; ", loadErrors));
                }

                return fallback with { LoadErrors = loadErrors };
            }
        }
        catch (Exception exception)
        {
            loadErrors.Add("Встроенный каталог health rules повреждён: " + exception.Message);
        }

        return new RuleFile
        {
            SchemaVersion = "1.0",
            ScoreWeights = DefaultWeights(),
            Rules = Array.Empty<HealthRuleDefinition>(),
            LoadErrors = loadErrors
        };
    }

    private static RuleMatch EvaluateRule(HealthRuleDefinition rule, JsonObject data, IReadOnlyList<CollectorResult> collectors)
    {
        HealthRuleCondition condition = rule.Condition;
        if (string.Equals(condition.Source, "collector", StringComparison.OrdinalIgnoreCase))
        {
            return EvaluateCollectorRule(condition, collectors);
        }

        List<SelectedNode> selected = Select(data, condition.Path).Where(static item => item.Node is not null).ToList();
        string op = condition.Operator.Trim();
        return op switch
        {
            "exists" => Match(selected.Count > 0, selected.Count, selected.Count > 0 ? selected[0].Path : EvidencePath(condition.Path)),
            "isNullOrEmpty" => Match(IsNullOrEmpty(selected), selected.Count, EvidencePath(condition.Path)),
            "equals" => EvaluateEquals(selected, condition),
            "notEquals" => EvaluateNotEquals(selected, condition),
            "greaterThan" => EvaluateAnyGreaterThan(selected, condition),
            "anyGreaterThan" => EvaluateAnyGreaterThan(selected, condition),
            "anyLessThan" => EvaluateAnyLessThan(selected, condition),
            "containsText" => EvaluateContainsText(selected, condition, any: false),
            "anyContainsText" => EvaluateContainsText(selected, condition, any: true),
            "anyEquals" => EvaluateAnyEquals(selected, condition),
            "anyIn" => EvaluateAnyIn(selected, condition),
            "countEquals" => Match(selected.Count == (int)(condition.Number ?? 0) && ((int)(condition.Number ?? 0) != 0 || PathContainerExists(data, condition.Path)), selected.Count, EvidencePath(condition.Path)),
            "countGreaterOrEqual" => Match(selected.Count >= (int)(condition.Number ?? 0), selected.Count, EvidencePath(condition.Path)),
            "countLessThan" => Match(selected.Count < (int)(condition.Number ?? 0), selected.Count, EvidencePath(condition.Path)),
            "countTextGreaterOrEqual" => EvaluateCountText(selected, condition),
            "anyEqualsWithSiblingGreaterThan" => EvaluateSiblingThreshold(selected, condition),
            _ => RuleMatch.No
        };
    }

    private static RuleMatch EvaluateCollectorRule(HealthRuleCondition condition, IReadOnlyList<CollectorResult> collectors)
    {
        string pattern = string.IsNullOrWhiteSpace(condition.CollectorIdPattern) ? "*" : condition.CollectorIdPattern;
        HashSet<string> statuses = new(condition.Statuses, StringComparer.OrdinalIgnoreCase);
        CollectorResult[] matched = collectors
            .Where(collector => Matches(pattern, collector.Id))
            .Where(collector => statuses.Count == 0 || statuses.Contains(collector.Status.ToString()))
            .ToArray();

        if (matched.Length == 0)
        {
            return RuleMatch.No;
        }

        string value = string.Join(", ", matched.Select(static item => item.Id + "=" + item.Status));
        return new RuleMatch(true, value, "collectors");
    }

    private static RuleMatch EvaluateEquals(List<SelectedNode> selected, HealthRuleCondition condition)
    {
        foreach (SelectedNode item in selected)
        {
            if (ValueEquals(item.Node, condition))
            {
                return new RuleMatch(true, ValueToString(item.Node), item.Path);
            }
        }

        return RuleMatch.No;
    }

    private static RuleMatch EvaluateNotEquals(List<SelectedNode> selected, HealthRuleCondition condition)
    {
        foreach (SelectedNode item in selected)
        {
            if (!ValueEquals(item.Node, condition))
            {
                return new RuleMatch(true, ValueToString(item.Node), item.Path);
            }
        }

        return RuleMatch.No;
    }

    private static RuleMatch EvaluateAnyEquals(List<SelectedNode> selected, HealthRuleCondition condition)
    {
        return EvaluateEquals(selected, condition);
    }

    private static RuleMatch EvaluateAnyIn(List<SelectedNode> selected, HealthRuleCondition condition)
    {
        HashSet<string> allowed = new(condition.Values, StringComparer.OrdinalIgnoreCase);
        foreach (SelectedNode item in selected)
        {
            string value = ValueToString(item.Node);
            if (allowed.Contains(value))
            {
                return new RuleMatch(true, value, item.Path);
            }
        }

        return RuleMatch.No;
    }

    private static RuleMatch EvaluateAnyGreaterThan(List<SelectedNode> selected, HealthRuleCondition condition)
    {
        double threshold = condition.Number ?? 0;
        SelectedNode? best = null;
        double bestValue = double.MinValue;
        foreach (SelectedNode item in selected)
        {
            if (TryNumber(item.Node, out double value) && value > threshold && value > bestValue)
            {
                best = item;
                bestValue = value;
            }
        }

        return best is null ? RuleMatch.No : new RuleMatch(true, FormatNumber(bestValue), best.Value.Path);
    }

    private static RuleMatch EvaluateAnyLessThan(List<SelectedNode> selected, HealthRuleCondition condition)
    {
        double threshold = condition.Number ?? 0;
        SelectedNode? best = null;
        double bestValue = double.MaxValue;
        foreach (SelectedNode item in selected)
        {
            if (TryNumber(item.Node, out double value) && value < threshold && value < bestValue)
            {
                best = item;
                bestValue = value;
            }
        }

        return best is null ? RuleMatch.No : new RuleMatch(true, FormatNumber(bestValue), best.Value.Path);
    }

    private static RuleMatch EvaluateContainsText(List<SelectedNode> selected, HealthRuleCondition condition, bool any)
    {
        string needle = condition.Text ?? string.Empty;
        if (needle.Length == 0)
        {
            return RuleMatch.No;
        }

        if (!any && selected.Count == 0)
        {
            return RuleMatch.No;
        }

        foreach (SelectedNode item in selected)
        {
            string value = ValueToString(item.Node);
            if (value.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return new RuleMatch(true, value, item.Path);
            }
        }

        return RuleMatch.No;
    }

    private static RuleMatch EvaluateCountText(List<SelectedNode> selected, HealthRuleCondition condition)
    {
        string needle = condition.Text ?? string.Empty;
        int threshold = (int)(condition.Number ?? 0);
        int count = selected.Count(item => ValueToString(item.Node).Contains(needle, StringComparison.OrdinalIgnoreCase));
        return Match(count >= threshold, count, EvidencePath(condition.Path));
    }

    private static RuleMatch EvaluateSiblingThreshold(List<SelectedNode> selected, HealthRuleCondition condition)
    {
        double expectedId = condition.Number ?? 0;
        (string siblingName, double siblingThreshold) = ParseSiblingThreshold(condition.Text);
        foreach (SelectedNode item in selected)
        {
            if (!TryNumber(item.Node, out double idValue) || Math.Abs(idValue - expectedId) > 0.001)
            {
                continue;
            }

            if (item.Parent is not JsonObject parent || !TryGetProperty(parent, siblingName, out JsonNode? sibling))
            {
                continue;
            }

            if (TryNumber(sibling, out double siblingValue) && siblingValue > siblingThreshold)
            {
                return new RuleMatch(true, FormatNumber(siblingValue), item.Path + "/../" + siblingName);
            }
        }

        return RuleMatch.No;
    }

    private static (string PropertyName, double Threshold) ParseSiblingThreshold(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ("rawValue", 0);
        }

        string[] parts = text.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double threshold))
        {
            return (parts[0], 0);
        }

        return (parts[0], threshold);
    }

    private static bool ValueEquals(JsonNode? node, HealthRuleCondition condition)
    {
        if (condition.Boolean.HasValue)
        {
            return TryBoolean(node, out bool value) && value == condition.Boolean.Value;
        }

        if (condition.Number.HasValue)
        {
            return TryNumber(node, out double value) && Math.Abs(value - condition.Number.Value) < 0.001;
        }

        if (condition.Text is not null)
        {
            return string.Equals(ValueToString(node), condition.Text, StringComparison.OrdinalIgnoreCase);
        }

        return node is null;
    }

    private static bool IsNullOrEmpty(List<SelectedNode> selected)
    {
        if (selected.Count == 0)
        {
            return true;
        }

        foreach (SelectedNode item in selected)
        {
            if (item.Node is null)
            {
                continue;
            }

            if (item.Node is JsonArray array && array.Count == 0)
            {
                continue;
            }

            if (item.Node is JsonObject obj && obj.Count == 0)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(ValueToString(item.Node)))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static IEnumerable<SelectedNode> Select(JsonNode root, IReadOnlyList<string> path)
    {
        return SelectCore(root, path, 0, parent: null, pathText: "$.");
    }

    private static bool PathContainerExists(JsonNode root, IReadOnlyList<string> path)
    {
        if (path.Count == 0)
        {
            return true;
        }

        int wildcardIndex = -1;
        for (int index = 0; index < path.Count; index++)
        {
            if (path[index] == "*")
            {
                wildcardIndex = index;
                break;
            }
        }

        int lastIndex = wildcardIndex >= 0 ? wildcardIndex : path.Count;
        JsonNode? current = root;
        for (int index = 0; index < lastIndex; index++)
        {
            if (current is JsonObject obj && TryGetProperty(obj, path[index], out JsonNode? child))
            {
                current = child;
                continue;
            }

            if (current is JsonArray array && int.TryParse(path[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out int arrayIndex) && arrayIndex >= 0 && arrayIndex < array.Count)
            {
                current = array[arrayIndex];
                continue;
            }

            return false;
        }

        return current is not null;
    }

    private static IEnumerable<SelectedNode> SelectCore(JsonNode? node, IReadOnlyList<string> path, int index, JsonNode? parent, string pathText)
    {
        if (index >= path.Count)
        {
            yield return new SelectedNode(node, parent, pathText.TrimEnd('.'));
            yield break;
        }

        if (node is null)
        {
            yield break;
        }

        string segment = path[index];
        if (segment == "*")
        {
            if (node is JsonArray array)
            {
                for (int itemIndex = 0; itemIndex < array.Count; itemIndex++)
                {
                    foreach (SelectedNode selected in SelectCore(array[itemIndex], path, index + 1, node, pathText + "[" + itemIndex.ToString(CultureInfo.InvariantCulture) + "]."))
                    {
                        yield return selected;
                    }
                }
            }
            else if (node is JsonObject obj)
            {
                foreach (KeyValuePair<string, JsonNode?> property in obj)
                {
                    foreach (SelectedNode selected in SelectCore(property.Value, path, index + 1, node, pathText + property.Key + "."))
                    {
                        yield return selected;
                    }
                }
            }

            yield break;
        }

        if (node is JsonArray arrayNode)
        {
            if (int.TryParse(segment, NumberStyles.Integer, CultureInfo.InvariantCulture, out int numericIndex) && numericIndex >= 0 && numericIndex < arrayNode.Count)
            {
                foreach (SelectedNode selected in SelectCore(arrayNode[numericIndex], path, index + 1, node, pathText + "[" + numericIndex.ToString(CultureInfo.InvariantCulture) + "]."))
                {
                    yield return selected;
                }
            }
            else
            {
                for (int itemIndex = 0; itemIndex < arrayNode.Count; itemIndex++)
                {
                    foreach (SelectedNode selected in SelectCore(arrayNode[itemIndex], path, index, node, pathText + "[" + itemIndex.ToString(CultureInfo.InvariantCulture) + "]."))
                    {
                        yield return selected;
                    }
                }
            }

            yield break;
        }

        if (node is JsonObject objectNode && TryGetProperty(objectNode, segment, out JsonNode? child))
        {
            foreach (SelectedNode selected in SelectCore(child, path, index + 1, node, pathText + segment + "."))
            {
                yield return selected;
            }
        }
    }

    private static bool TryGetProperty(JsonObject obj, string propertyName, out JsonNode? value)
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

    private static bool TryNumber(JsonNode? node, out double value)
    {
        value = 0;
        if (node is null)
        {
            return false;
        }

        if (node is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue(out double doubleValue))
            {
                value = doubleValue;
                return true;
            }

            if (jsonValue.TryGetValue(out long longValue))
            {
                value = longValue;
                return true;
            }

            if (jsonValue.TryGetValue(out int intValue))
            {
                value = intValue;
                return true;
            }

            if (jsonValue.TryGetValue(out string? stringValue) && double.TryParse(stringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            {
                value = parsed;
                return true;
            }
        }

        return double.TryParse(node.ToJsonString().Trim('"'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryBoolean(JsonNode? node, out bool value)
    {
        value = false;
        if (node is null)
        {
            return false;
        }

        if (node is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue(out bool boolValue))
            {
                value = boolValue;
                return true;
            }

            if (jsonValue.TryGetValue(out string? stringValue) && bool.TryParse(stringValue, out bool parsed))
            {
                value = parsed;
                return true;
            }

            if (jsonValue.TryGetValue(out int intValue))
            {
                value = intValue != 0;
                return true;
            }
        }

        return false;
    }

    private static string ValueToString(JsonNode? node)
    {
        if (node is null)
        {
            return string.Empty;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue(out string? stringValue))
            {
                return stringValue ?? string.Empty;
            }

            if (value.TryGetValue(out bool boolValue))
            {
                return boolValue.ToString(CultureInfo.InvariantCulture);
            }

            if (value.TryGetValue(out double doubleValue))
            {
                return FormatNumber(doubleValue);
            }
        }

        return node.ToJsonString();
    }

    private static string FormatNumber(double value)
    {
        return Math.Abs(value % 1) < 0.001
            ? value.ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static RuleMatch Match(bool matched, object value, string evidence)
    {
        return matched ? new RuleMatch(true, Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, evidence) : RuleMatch.No;
    }

    private static string EvidencePath(IReadOnlyList<string> path)
    {
        return path.Count == 0 ? "$" : "$.." + string.Join('.', path);
    }

    private static string Render(string template, RuleMatch match)
    {
        return template
            .Replace("{value}", match.Value, StringComparison.OrdinalIgnoreCase)
            .Replace("{path}", match.Evidence, StringComparison.OrdinalIgnoreCase);
    }

    private static ReportSummary BuildSummary(IReadOnlyDictionary<string, double> weights, IReadOnlyDictionary<string, int> penalties)
    {
        int hardware = ClampScore(100 - GetPenalty(penalties, "hardware"));
        int security = ClampScore(100 - GetPenalty(penalties, "security"));
        int stability = ClampScore(100 - GetPenalty(penalties, "stability"));
        double hardwareWeight = GetWeight(weights, "hardware", 0.45);
        double securityWeight = GetWeight(weights, "security", 0.35);
        double stabilityWeight = GetWeight(weights, "stability", 0.20);
        double totalWeight = hardwareWeight + securityWeight + stabilityWeight;
        if (totalWeight <= 0)
        {
            hardwareWeight = 0.45;
            securityWeight = 0.35;
            stabilityWeight = 0.20;
            totalWeight = 1.0;
        }

        int overall = ClampScore((int)Math.Round(((hardware * hardwareWeight) + (security * securityWeight) + (stability * stabilityWeight)) / totalWeight));
        return new ReportSummary
        {
            HardwareHealthScore = hardware,
            SecurityScore = security,
            StabilityScore = stability,
            OverallScore = overall,
            Verdict = Verdict(overall),
            PenaltiesByCategory = new Dictionary<string, int>(penalties, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static int GetPenalty(IReadOnlyDictionary<string, int> penalties, string key)
    {
        return penalties.TryGetValue(key, out int value) ? value : 0;
    }

    private static double GetWeight(IReadOnlyDictionary<string, double> weights, string key, double fallback)
    {
        return weights.TryGetValue(key, out double value) ? value : fallback;
    }

    private static Dictionary<string, int> CreatePenaltyBuckets()
    {
        return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["hardware"] = 0,
            ["security"] = 0,
            ["stability"] = 0
        };
    }

    private static string NormalizeBucket(string bucket)
    {
        if (bucket.Equals("security", StringComparison.OrdinalIgnoreCase))
        {
            return "security";
        }

        if (bucket.Equals("stability", StringComparison.OrdinalIgnoreCase))
        {
            return "stability";
        }

        return "hardware";
    }

    private static string ResolveBucketFromCategory(string category)
    {
        if (category.Contains("security", StringComparison.OrdinalIgnoreCase) || category.Contains("безопас", StringComparison.OrdinalIgnoreCase))
        {
            return "security";
        }

        if (category.Contains("stability", StringComparison.OrdinalIgnoreCase) || category.Contains("журнал", StringComparison.OrdinalIgnoreCase))
        {
            return "stability";
        }

        return "hardware";
    }

    private static int DefaultPenalty(Severity severity)
    {
        return severity switch
        {
            Severity.Info => 0,
            Severity.Low => 2,
            Severity.Medium => 7,
            Severity.High => 15,
            Severity.Critical => 30,
            _ => 0
        };
    }

    private static int ClampScore(int value)
    {
        return Math.Max(0, Math.Min(100, value));
    }

    private static string Verdict(int score)
    {
        return score switch
        {
            >= 90 => "Отличное",
            >= 75 => "Хорошее",
            >= 60 => "Удовлетворительное",
            >= 40 => "Требует внимания",
            _ => "Критическое"
        };
    }

    private static bool Matches(string pattern, string value)
    {
        if (pattern == "*" || string.Equals(pattern, value, StringComparison.OrdinalIgnoreCase))
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

        return true;
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        options.Converters.Add(new JsonStringEnumConverter<Severity>());
        return options;
    }

    private sealed record RuleFile
    {
        public string SchemaVersion { get; init; } = "1.0";

        public IReadOnlyDictionary<string, double> ScoreWeights { get; init; } = DefaultWeights();

        public IReadOnlyList<HealthRuleDefinition> Rules { get; init; } = Array.Empty<HealthRuleDefinition>();

        public IReadOnlyList<string> LoadErrors { get; init; } = Array.Empty<string>();
    }

    private readonly record struct RuleMatch(bool Matched, string Value, string Evidence)
    {
        public static RuleMatch No { get; } = new(false, string.Empty, string.Empty);
    }

    private readonly record struct SelectedNode(JsonNode? Node, JsonNode? Parent, string Path);

    private static IReadOnlyDictionary<string, double> DefaultWeights()
    {
        return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["hardware"] = 0.45,
            ["security"] = 0.35,
            ["stability"] = 0.20
        };
    }
}
