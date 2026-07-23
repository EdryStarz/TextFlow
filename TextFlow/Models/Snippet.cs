using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace TextFlow.Models;

public enum SnippetKind
{
    Text,
    Launch
}

public sealed class Snippet : INotifyPropertyChanged
{
    private Guid _id = Guid.NewGuid();
    private string _name = "Новый сниппет";
    private SnippetKind _kind = SnippetKind.Text;
    private string _content = string.Empty;
    private string _hotkey = string.Empty;
    private DateTime _createdUtc = DateTime.UtcNow;

    public Guid Id { get => _id; set => Set(ref _id, value); }
    public string Name { get => _name; set => Set(ref _name, value); }
    public SnippetKind Kind
    {
        get => _kind;
        set
        {
            if (!Set(ref _kind, value)) return;
            OnPropertyChanged(nameof(KindLabel));
        }
    }
    public string Content
    {
        get => _content;
        set
        {
            if (!Set(ref _content, value)) return;
            OnPropertyChanged(nameof(Preview));
        }
    }
    public string Hotkey { get => _hotkey; set => Set(ref _hotkey, value); }
    public DateTime CreatedUtc { get => _createdUtc; set => Set(ref _createdUtc, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    [JsonIgnore]
    public string KindLabel => Kind == SnippetKind.Text ? "Текст" : "Запуск";

    [JsonIgnore]
    public string Preview
    {
        get
        {
            var value = Content.Replace("\r", " ").Replace("\n", " ").Trim();
            return value.Length <= 90 ? value : value[..87] + "…";
        }
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class AppSettings
{
    public string SpotlightHotkey { get; set; } = "Alt+Space";
}

public sealed class SnippetDatabase
{
    public AppSettings Settings { get; set; } = new();
    public List<Snippet> Snippets { get; set; } = [];
}
