using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using TextFlow.Models;

namespace TextFlow.Services;

public sealed class ImportResult
{
    public List<Snippet> Snippets { get; } = [];
    public List<string> Warnings { get; } = [];
}

/// <summary>
/// Imports QuickTextPaste's documented [list_text] / [list_label] format.
/// It deliberately ignores program options and only migrates user commands.
/// </summary>
public sealed class QuickTextPasteImporter
{
    private const string EndMarker = "<E<N<D|";

    public ImportResult Import(string filePath, IEnumerable<Snippet> alreadyPresent)
    {
        var ini = ReadIni(filePath);
        var result = new ImportResult();
        if (!ini.TryGetValue("list_text", out var texts))
        {
            result.Warnings.Add("Не найдена секция [list_text]. Это не похоже на QuickTextPaste.ini.");
            return result;
        }

        ini.TryGetValue("list_label", out var labels);
        var existingNames = alreadyPresent.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var item in texts.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!item.Key.StartsWith("text_", StringComparison.OrdinalIgnoreCase)) continue;
            var decoded = RemoveEndMarker(item.Value);
            var separator = decoded.IndexOf('=');
            if (separator <= 0)
            {
                result.Warnings.Add($"{item.Key}: отсутствует hotkey-разделитель '='.");
                continue;
            }

            var qtpHotkey = decoded[..separator].Trim();
            var payload = decoded[(separator + 1)..];
            var kind = SnippetKind.Text;
            if (TryExtractRunCommand(payload, out var command))
            {
                kind = SnippetKind.Launch;
                payload = command;
            }
            else if (payload.StartsWith("charmap:", StringComparison.OrdinalIgnoreCase)
                     || payload.StartsWith("paste_slot:", StringComparison.OrdinalIgnoreCase)
                     || payload.StartsWith("copy_slot:", StringComparison.OrdinalIgnoreCase)
                     || payload.StartsWith("cpy:", StringComparison.OrdinalIgnoreCase))
            {
                result.Warnings.Add($"{item.Key}: специализированная команда QTP импортирована как текст и требует проверки.");
            }

            // QTP serializes line breaks as the two characters '\\' and 'n'. New entries
            // in TextFlow use real line breaks, so regular paths/commands are not rewritten later.
            if (kind == SnippetKind.Text)
                payload = payload.Replace("\\n", Environment.NewLine, StringComparison.Ordinal);

            var label = labels is not null && labels.TryGetValue(item.Key, out var foundLabel) ? CleanLabel(foundLabel) : string.Empty;
            var name = string.IsNullOrWhiteSpace(label) ? BuildName(payload, kind, result.Snippets.Count + 1) : label;
            name = MakeUnique(name, existingNames);
            existingNames.Add(name);

            result.Snippets.Add(new Snippet
            {
                Name = name,
                Kind = kind,
                Content = payload,
                Hotkey = ConvertHotkey(qtpHotkey)
            });
        }

        if (result.Snippets.Count == 0)
            result.Warnings.Add("В [list_text] не найдено ни одной корректной записи text_###.");
        return result;
    }

    private static Dictionary<string, Dictionary<string, string>> ReadIni(string filePath)
    {
        // StreamReader detects UTF-8/UTF-16 BOM. Legacy ANSI files fall back to the current Windows code page.
        using var reader = new StreamReader(filePath, Encoding.Default, detectEncodingFromByteOrderMarks: true);
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? current = null;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith(';') || trimmed.StartsWith('#')) continue;
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                var name = trimmed[1..^1].Trim();
                current = sections.TryGetValue(name, out var section)
                    ? section
                    : sections[name] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }
            var equals = line.IndexOf('=');
            if (current is not null && equals > 0)
                current[line[..equals].Trim()] = line[(equals + 1)..];
        }
        return sections;
    }

    private static string RemoveEndMarker(string text)
    {
        // The marker is not part of the command. Cutting at the first marker also handles
        // old QTP files that contain a second marker with whitespace between them.
        var marker = text.IndexOf(EndMarker, StringComparison.Ordinal);
        return marker >= 0 ? text[..marker] : text;
    }

    private static bool TryExtractRunCommand(string text, out string command)
    {
        var match = Regex.Match(text, "^run(?:x|a)?\\s*:(.*)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (match.Success)
        {
            command = Environment.ExpandEnvironmentVariables(match.Groups[1].Value.Trim());
            return true;
        }
        command = string.Empty;
        return false;
    }

    private static string ConvertHotkey(string qtpHotkey) => qtpHotkey
        .Replace("L-Win", "Win", StringComparison.OrdinalIgnoreCase)
        .Replace("R-Win", "Win", StringComparison.OrdinalIgnoreCase)
        .Replace("L-Ctrl", "Ctrl", StringComparison.OrdinalIgnoreCase)
        .Replace("R-Ctrl", "Ctrl", StringComparison.OrdinalIgnoreCase)
        .Replace("Control", "Ctrl", StringComparison.OrdinalIgnoreCase)
        .Trim();

    private static string CleanLabel(string label) => label.Replace("&&", "&").Replace("&", string.Empty).Trim();

    private static string BuildName(string payload, SnippetKind kind, int index)
    {
        var firstLine = payload.Split(["\r", "\n"], StringSplitOptions.None)[0].Trim();
        if (string.IsNullOrWhiteSpace(firstLine)) firstLine = kind == SnippetKind.Launch ? "Запуск" : "Текст";
        return firstLine.Length <= 48 ? firstLine : firstLine[..45] + "…";
    }

    private static string MakeUnique(string candidate, HashSet<string> existing)
    {
        if (!existing.Contains(candidate)) return candidate;
        var baseName = candidate;
        for (var number = 2; ; number++)
        {
            candidate = $"{baseName} ({number})";
            if (!existing.Contains(candidate)) return candidate;
        }
    }
}
