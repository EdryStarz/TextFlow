using System.IO;
using System.Collections.ObjectModel;
using System.Text.Json;
using TextFlow.Models;

namespace TextFlow.Services;

/// <summary>Stores all user data next to the executable, making the app genuinely portable.</summary>
public sealed class SnippetStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath = Path.Combine(AppContext.BaseDirectory, "data", "snippets.json");

    public AppSettings Settings { get; private set; } = new();
    public ObservableCollection<Snippet> Snippets { get; } = [];

    public void Load()
    {
        if (!File.Exists(_filePath)) return;

        try
        {
            var database = JsonSerializer.Deserialize<SnippetDatabase>(File.ReadAllText(_filePath), JsonOptions);
            if (database is null) return;
            Settings = database.Settings ?? new AppSettings();
            foreach (var snippet in database.Snippets ?? []) Snippets.Add(snippet);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Не удалось прочитать {_filePath}. Проверьте JSON-файл.", ex);
        }
    }

    public void Save(IEnumerable<Snippet> snippets)
    {
        var database = new SnippetDatabase { Settings = Settings, Snippets = snippets.ToList() };
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var temporary = _filePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(database, JsonOptions));
        File.Move(temporary, _filePath, true);
    }
}
