using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Data;
using TextFlow.Models;

namespace TextFlow.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private Snippet? _selected;
    private string _searchText = string.Empty;
    private string _status = string.Empty;
    private string _searchHotkey = "Alt+Space";

    public MainViewModel(IEnumerable<Snippet> snippets)
    {
        Snippets = new ObservableCollection<Snippet>(snippets.OrderBy(s => s.Name));
        CreateHotkeyOptions();
        Snippets.CollectionChanged += Snippets_CollectionChanged;
        foreach (var snippet in Snippets) SubscribeToSnippet(snippet);
        FilteredSnippets = CollectionViewSource.GetDefaultView(Snippets);
        FilteredSnippets.Filter = MatchesSearch;
        Selected = Snippets.FirstOrDefault();
        RefreshHotkeyOptions();
    }

    public ObservableCollection<Snippet> Snippets { get; }
    public ObservableCollection<HotkeyOption> HotkeyOptions { get; } = [];
    public ICollectionView FilteredSnippets { get; }
    public Array SnippetKinds { get; } = Enum.GetValues(typeof(SnippetKind));

    public Snippet? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value)) return;
            RefreshHotkeyOptions();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value)) return;
            FilteredSnippets.Refresh();
        }
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string SearchHotkey
    {
        get => _searchHotkey;
        set => SetProperty(ref _searchHotkey, value);
    }

    public Snippet AddNew()
    {
        var snippet = new Snippet { Name = "Новый сниппет" };
        Snippets.Add(snippet);
        Selected = snippet;
        return snippet;
    }

    public void RemoveSelected()
    {
        if (Selected is null) return;
        var index = Snippets.IndexOf(Selected);
        Snippets.Remove(Selected);
        Selected = Snippets.ElementAtOrDefault(Math.Max(0, index - 1));
    }

    public void AddRange(IEnumerable<Snippet> snippets)
    {
        foreach (var snippet in snippets) Snippets.Add(snippet);
        Selected = Snippets.LastOrDefault();
        FilteredSnippets.Refresh();
    }

    public void SortByName()
    {
        var selected = Selected;
        var ordered = Snippets.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        Snippets.Clear();
        foreach (var snippet in ordered) Snippets.Add(snippet);
        Selected = selected;
    }

    private bool MatchesSearch(object item)
    {
        if (item is not Snippet snippet) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;

        return snippet.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || snippet.Content.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || snippet.Hotkey.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    private void CreateHotkeyOptions()
    {
        HotkeyOptions.Add(new HotkeyOption(string.Empty, "Не назначено"));
        foreach (var letter in Enumerable.Range('A', 26).Select(value => (char)value))
            HotkeyOptions.Add(new HotkeyOption($"Ctrl+{letter}", $"Ctrl+{letter}"));
        foreach (var letter in Enumerable.Range('A', 26).Select(value => (char)value))
            HotkeyOptions.Add(new HotkeyOption($"Ctrl+Shift+{letter}", $"Ctrl+Shift+{letter}"));
        foreach (var letter in Enumerable.Range('A', 26).Select(value => (char)value))
            HotkeyOptions.Add(new HotkeyOption($"Alt+{letter}", $"Alt+{letter}"));
        foreach (var number in Enumerable.Range(1, 12))
            HotkeyOptions.Add(new HotkeyOption($"F{number}", $"F{number}"));
    }

    private void Snippets_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (Snippet snippet in e.OldItems) UnsubscribeFromSnippet(snippet);
        if (e.NewItems is not null)
            foreach (Snippet snippet in e.NewItems) SubscribeToSnippet(snippet);
        RefreshHotkeyOptions();
    }

    private void SubscribeToSnippet(Snippet snippet) => snippet.PropertyChanged += Snippet_PropertyChanged;
    private void UnsubscribeFromSnippet(Snippet snippet) => snippet.PropertyChanged -= Snippet_PropertyChanged;

    private void Snippet_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Snippet.Hotkey) or nameof(Snippet.Name))
            RefreshHotkeyOptions();
    }

    private void RefreshHotkeyOptions()
    {
        AddLegacyHotkeyOptions();
        foreach (var option in HotkeyOptions)
        {
            var owners = string.IsNullOrEmpty(option.Value)
                ? []
                : Snippets
                    .Where(snippet => !ReferenceEquals(snippet, Selected)
                        && string.Equals(snippet.Hotkey, option.Value, StringComparison.OrdinalIgnoreCase))
                    .Select(snippet => snippet.Name)
                    .ToArray();
            option.SetOccupiedBy(owners);
        }
    }

    private void AddLegacyHotkeyOptions()
    {
        foreach (var hotkey in Snippets
                     .Select(snippet => snippet.Hotkey.Trim())
                     .Where(hotkey => !string.IsNullOrEmpty(hotkey)
                         && !HotkeyOptions.Any(option => string.Equals(option.Value, hotkey, StringComparison.OrdinalIgnoreCase)))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            HotkeyOptions.Add(new HotkeyOption(hotkey, $"{hotkey} (импортировано)"));
        }
    }
}

public sealed class HotkeyOption : ObservableObject
{
    private string _occupiedBy = string.Empty;

    public HotkeyOption(string value, string label)
    {
        Value = value;
        Label = label;
    }

    public string Value { get; }
    public string Label { get; }
    public string OccupiedBy
    {
        get => _occupiedBy;
        private set
        {
            if (!SetProperty(ref _occupiedBy, value)) return;
            RaisePropertyChanged(nameof(Display));
            RaisePropertyChanged(nameof(IsOccupied));
        }
    }

    public bool IsOccupied => !string.IsNullOrEmpty(OccupiedBy);
    public string Display => IsOccupied ? $"{Label}  — занято: {OccupiedBy}" : Label;
    public void SetOccupiedBy(IEnumerable<string> names) => OccupiedBy = string.Join(", ", names.Take(2));
}
