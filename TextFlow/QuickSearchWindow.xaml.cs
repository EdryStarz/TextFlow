using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using TextFlow.Models;
using TextFlow.Native;
using TextFlow.Services;
using TextFlow.ViewModels;

namespace TextFlow;

public partial class QuickSearchWindow : Window, INotifyPropertyChanged
{
    private readonly MainViewModel _source;
    private readonly Func<Snippet, ActiveTarget, Task> _execute;
    private readonly Action<Snippet> _edit;
    private readonly Action _create;
    private readonly Action<Snippet> _delete;
    private ActiveTarget _target;
    private Snippet? _selectedResult;
    private IReadOnlyList<Snippet>? _scope;
    private bool _preserveSourceFocus;
    private bool _isExecuting;

    public QuickSearchWindow(
        MainViewModel source,
        Func<Snippet, ActiveTarget, Task> execute,
        Action<Snippet> edit,
        Action create,
        Action<Snippet> delete)
    {
        _source = source;
        _execute = execute;
        _edit = edit;
        _create = create;
        _delete = delete;
        Results = [];
        DataContext = this;
        InitializeComponent();
        _source.Snippets.CollectionChanged += SourceChanged;
        Closing += (_, e) => { e.Cancel = true; Hide(); };
        SourceInitialized += (_, _) => UpdateActivationStyle();
        Deactivated += (_, _) =>
        {
            if (IsVisible) Hide();
        };
    }

    public ObservableCollection<Snippet> Results { get; }
    public Snippet? SelectedResult
    {
        get => _selectedResult;
        set { _selectedResult = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedResult))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;

    public void ShowForTarget(
        ActiveTarget target,
        IReadOnlyList<Snippet>? scope = null,
        string? scopeDescription = null,
        bool preserveSourceFocus = false)
    {
        _target = target;
        _scope = scope;
        _preserveSourceFocus = preserveSourceFocus;
        ShowActivated = !preserveSourceFocus;
        SearchBox.IsReadOnly = preserveSourceFocus;
        SearchBox.IsHitTestVisible = !preserveSourceFocus;
        Placeholder.Text = preserveSourceFocus
            ? $"{scopeDescription} — кликните фразу для вставки"
            : scopeDescription is null ? "Поиск всех фраз…" : $"{scopeDescription} — поиск…";
        UpdateActivationStyle();
        SearchBox.Clear();
        RefreshResults();
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + Math.Max(60, (area.Height - Height) * 0.28);
        Show();
        if (!preserveSourceFocus)
        {
            Activate();
            SearchBox.Focus();
        }
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        Placeholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        RefreshResults();
    }

    private void RefreshResults()
    {
        var query = SearchBox?.Text?.Trim() ?? string.Empty;
        var candidates = _scope ?? _source.Snippets;
        var matches = candidates
            .Where(s => string.IsNullOrWhiteSpace(query)
                || s.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || s.Content.Contains(query, StringComparison.OrdinalIgnoreCase)
                || s.Hotkey.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(80)
            .ToList();
        Results.Clear();
        foreach (var match in matches) Results.Add(match);
        SelectedResult = Results.FirstOrDefault();
    }

    private async Task ExecuteSelectedAsync()
    {
        var selected = SelectedResult;
        if (selected is null) return;
        await ExecuteSnippetAsync(selected);
    }

    private async Task ExecuteSnippetAsync(Snippet snippet)
    {
        if (_isExecuting) return;

        _isExecuting = true;
        Hide();
        try
        {
            await _execute(snippet, _target);
        }
        finally
        {
            _isExecuting = false;
        }
    }

    private async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Hide(); e.Handled = true; return; }
        if (e.Key == Key.Enter) { await ExecuteSelectedAsync(); e.Handled = true; }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N)
        {
            Hide();
            _create();
            e.Handled = true;
            return;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.E)
        {
            if (SelectedResult is not null)
            {
                var snippet = SelectedResult;
                Hide();
                _edit(snippet);
            }
            e.Handled = true;
            return;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Delete)
        {
            if (SelectedResult is not null)
            {
                _delete(SelectedResult);
                RefreshResults();
            }
            e.Handled = true;
        }
    }

    private async void ResultList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source || FindParent<ListBoxItem>(source)?.DataContext is not Snippet snippet)
            return;

        e.Handled = true;
        await ExecuteSnippetAsync(snippet);
    }

    private void SourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (IsVisible) RefreshResults();
    }

    private static T? FindParent<T>(DependencyObject source) where T : DependencyObject
    {
        for (DependencyObject? current = source; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is T result) return result;
        return null;
    }

    private void UpdateActivationStyle()
    {
        var handle = new WindowInteropHelper(this).Handle;
        NativeMethods.SetNoActivate(handle, _preserveSourceFocus);
    }
}
