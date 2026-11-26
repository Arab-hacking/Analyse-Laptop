using LaptopInspector.Core.Models;

namespace LaptopInspector.Cli.CommandLine;

public sealed class CommandLineParser
{
    public ParsedCommand Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return new ParsedCommand(CliCommandKind.Scan, new ScanOptions(), null, false);
        }

        if (IsHelp(args[0]))
        {
            return new ParsedCommand(CliCommandKind.Help, new ScanOptions(), null, true);
        }

        int index = 0;
        CliCommandKind kind = CliCommandKind.Scan;
        string first = args[0];
        if (!first.StartsWith('-'))
        {
            kind = ParseCommand(first);
            index = 1;
        }

        if (kind == CliCommandKind.Help || args.Skip(index).Any(IsHelp))
        {
            return new ParsedCommand(CliCommandKind.Help, new ScanOptions(), null, true);
        }

        return kind switch
        {
            CliCommandKind.Scan => ParseScan(args, index, quickMode: false),
            CliCommandKind.Quick => ParseScan(args, index, quickMode: true),
            CliCommandKind.Diff => ParseDiff(args, index),
            CliCommandKind.ListCollectors => new ParsedCommand(kind, new ScanOptions(), null, false),
            CliCommandKind.SelfTest => new ParsedCommand(kind, new ScanOptions(), null, false),
            _ => throw new CommandLineException("Неизвестная команда.")
        };
    }

    public static string GetHelp()
    {
        return """
LaptopInspector.exe [команда] [опции]

Команды:
  scan                 Полный сбор (по умолчанию)
  quick                Быстрый сбор (таймаут 30 секунд)
  diff <a> <b>         Сравнение двух JSON-отчётов
  list-collectors      Список модулей и их требования
  selftest             Самопроверка каркаса

Опции scan/quick:
  -o, --output <dir>         Каталог для отчётов
  -f, --format <list>        html,json,txt,md,csv
  --include <ids>            Только указанные сборщики или маски storage.*
  --exclude <ids>            Исключить сборщики
  --days <N>                 Глубина анализа журналов, дней
  --benchmark                Включить синтетические benchmark-тесты CPU/memory
  --energy                   Запустить powercfg /energy /xml (около 60 секунд)
  --anonymize                Маскировать идентификаторы
  --history                  История USB/Wi-Fi профилей (зарезервировано)
  --deep-disk                Анализ крупнейших папок системного диска
  --online                   Разрешить сетевые проверки (зарезервировано, по умолчанию выключено)
  --raw                      Включить raw-дампы (зарезервировано)
  --lang <ru|en>             Язык отчёта
  --timeout <sec>            Глобальный таймаут
  --parallel <N>             Степень параллелизма
  --no-elevate               Не предлагать повышение прав
  -v, --verbose              Подробный лог
  --open                     Открыть HTML-отчёт по завершении

Опции diff:
  --out <file>               HTML-файл diff (по умолчанию diff.html)
""";
    }

    private static ParsedCommand ParseScan(string[] args, int index, bool quickMode)
    {
        ScanOptions options = new()
        {
            QuickMode = quickMode,
            GlobalTimeout = quickMode ? TimeSpan.FromSeconds(30) : TimeSpan.FromMinutes(5)
        };

        List<string> formats = new(options.Formats);
        List<string> include = new(options.Include);
        List<string> exclude = new(options.Exclude);

        while (index < args.Length)
        {
            string token = args[index++];
            (string name, string? inlineValue) = SplitInlineValue(token);
            switch (name)
            {
                case "-o":
                case "--output":
                    options = options with { OutputDirectory = Path.GetFullPath(ReadValue(args, ref index, inlineValue, name)) };
                    break;
                case "-f":
                case "--format":
                    formats = SplitList(ReadValue(args, ref index, inlineValue, name)).Select(NormalizeFormat).ToList();
                    break;
                case "--include":
                    include.AddRange(SplitList(ReadValue(args, ref index, inlineValue, name)));
                    break;
                case "--exclude":
                    exclude.AddRange(SplitList(ReadValue(args, ref index, inlineValue, name)));
                    break;
                case "--days":
                    options = options with { Days = ParseInt(ReadValue(args, ref index, inlineValue, name), name, minimum: 0) };
                    break;
                case "--benchmark":
                    EnsureNoInlineValue(inlineValue, name);
                    options = options with { Benchmark = true };
                    break;
                case "--energy":
                    EnsureNoInlineValue(inlineValue, name);
                    options = options with { Energy = true };
                    break;
                case "--anonymize":
                    EnsureNoInlineValue(inlineValue, name);
                    options = options with { Anonymize = true };
                    break;
                case "--history":
                    EnsureNoInlineValue(inlineValue, name);
                    options = options with { History = true };
                    break;
                case "--deep-disk":
                    EnsureNoInlineValue(inlineValue, name);
                    options = options with { DeepDisk = true };
                    break;
                case "--online":
                    EnsureNoInlineValue(inlineValue, name);
                    options = options with { Online = true };
                    break;
                case "--raw":
                    EnsureNoInlineValue(inlineValue, name);
                    options = options with { Raw = true };
                    break;
                case "--lang":
                    options = options with { Language = ParseLanguage(ReadValue(args, ref index, inlineValue, name)) };
                    break;
                case "--timeout":
                    options = options with { GlobalTimeout = TimeSpan.FromSeconds(ParseInt(ReadValue(args, ref index, inlineValue, name), name, minimum: 1)) };
                    break;
                case "--parallel":
                    options = options with { Parallelism = ParseInt(ReadValue(args, ref index, inlineValue, name), name, minimum: 1) };
                    break;
                case "--no-elevate":
                    EnsureNoInlineValue(inlineValue, name);
                    options = options with { NoElevate = true };
                    break;
                case "-v":
                case "--verbose":
                    EnsureNoInlineValue(inlineValue, name);
                    options = options with { Verbose = true };
                    break;
                case "--open":
                    EnsureNoInlineValue(inlineValue, name);
                    options = options with { OpenReport = true };
                    break;
                default:
                    throw new CommandLineException("Неизвестная опция: " + token);
            }
        }

        options = options with
        {
            Formats = formats.Count == 0 ? new[] { "html", "json", "txt" } : formats.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Include = include.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Exclude = exclude.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };

        return new ParsedCommand(quickMode ? CliCommandKind.Quick : CliCommandKind.Scan, options, null, false);
    }

    private static ParsedCommand ParseDiff(string[] args, int index)
    {
        List<string> operands = new();
        string output = "diff.html";

        while (index < args.Length)
        {
            string token = args[index++];
            (string name, string? inlineValue) = SplitInlineValue(token);
            if (name is "--out" or "-o")
            {
                output = ReadValue(args, ref index, inlineValue, name);
                continue;
            }

            if (token.StartsWith('-'))
            {
                throw new CommandLineException("Неизвестная опция diff: " + token);
            }

            operands.Add(token);
        }

        if (operands.Count != 2)
        {
            throw new CommandLineException("Команда diff требует два JSON-отчёта: diff <old.json> <new.json>.");
        }

        DiffOptions diffOptions = new()
        {
            OldReportPath = Path.GetFullPath(operands[0]),
            NewReportPath = Path.GetFullPath(operands[1]),
            OutputPath = Path.GetFullPath(output)
        };

        return new ParsedCommand(CliCommandKind.Diff, new ScanOptions(), diffOptions, false);
    }

    private static CliCommandKind ParseCommand(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "scan" => CliCommandKind.Scan,
            "quick" => CliCommandKind.Quick,
            "diff" => CliCommandKind.Diff,
            "list-collectors" => CliCommandKind.ListCollectors,
            "selftest" => CliCommandKind.SelfTest,
            "help" => CliCommandKind.Help,
            _ => throw new CommandLineException("Неизвестная команда: " + value)
        };
    }

    private static bool IsHelp(string value)
    {
        return value is "-h" or "--help" or "/?";
    }

    private static (string Name, string? Value) SplitInlineValue(string token)
    {
        int equalsIndex = token.IndexOf('=', StringComparison.Ordinal);
        if (equalsIndex < 0)
        {
            return (token, null);
        }

        return (token[..equalsIndex], token[(equalsIndex + 1)..]);
    }

    private static string ReadValue(string[] args, ref int index, string? inlineValue, string optionName)
    {
        if (inlineValue is not null)
        {
            if (inlineValue.Length == 0)
            {
                throw new CommandLineException("Опция " + optionName + " требует непустое значение.");
            }

            return inlineValue;
        }

        if (index >= args.Length)
        {
            throw new CommandLineException("Опция " + optionName + " требует значение.");
        }

        return args[index++];
    }

    private static void EnsureNoInlineValue(string? inlineValue, string optionName)
    {
        if (inlineValue is not null)
        {
            throw new CommandLineException("Флаг " + optionName + " не принимает значение.");
        }
    }

    private static IReadOnlyList<string> SplitList(string value)
    {
        return value
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static item => item.Length > 0)
            .ToArray();
    }

    private static string NormalizeFormat(string format)
    {
        return format.ToLowerInvariant() switch
        {
            "markdown" => "md",
            "text" => "txt",
            "zip" => "csv",
            "html" or "json" or "txt" or "md" or "csv" => format.ToLowerInvariant(),
            _ => throw new CommandLineException("Неизвестный формат отчёта: " + format)
        };
    }

    private static int ParseInt(string value, string optionName, int minimum)
    {
        if (!int.TryParse(value, out int parsed) || parsed < minimum)
        {
            throw new CommandLineException($"Опция {optionName} ожидает целое число не меньше {minimum}.");
        }

        return parsed;
    }

    private static string ParseLanguage(string value)
    {
        string normalized = value.ToLowerInvariant();
        if (normalized is not ("ru" or "en"))
        {
            throw new CommandLineException("Поддерживаемые языки: ru, en.");
        }

        return normalized;
    }
}
