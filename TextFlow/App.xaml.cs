using System.IO;
using System.Windows;
using TextFlow.Models;
using TextFlow.Native;
using TextFlow.Services;

namespace TextFlow;

public partial class App : Application
{
    private SnippetStore? _store;
    private HotkeyService? _hotkeys;
    private InputService? _input;
    private QuickSearchWindow? _searchWindow;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _store = new SnippetStore();
        _store.Load();
        var importStatus = ImportQuickTextPasteFromCommandLine();
        _input = new InputService();

        _mainWindow = new MainWindow(_store);
        ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose;
        _mainWindow.SnippetsChanged += (_, _) => RegisterHotkeys();
        _mainWindow.SearchRequested += (_, _) => ShowSearch(new ActiveTarget(_mainWindow.LastExternalWindow, IntPtr.Zero));
        _mainWindow.Show();

        _searchWindow = new QuickSearchWindow(
            _mainWindow.ViewModel,
            ExecuteSnippetAsync,
            snippet => _mainWindow.EditSnippet(snippet),
            () => _mainWindow.CreateNewSnippet(),
            snippet => _mainWindow.DeleteSnippet(snippet));
        _hotkeys = new HotkeyService();
        _hotkeys.Attach(_mainWindow);
        RegisterHotkeys();
        if (importStatus is not null) _mainWindow.SetStatus(importStatus);
    }

    private string? ImportQuickTextPasteFromCommandLine()
    {
        var args = Environment.GetCommandLineArgs();
        var optionIndex = Array.FindIndex(args, argument =>
            string.Equals(argument, "--import-qtp", StringComparison.OrdinalIgnoreCase));
        if (optionIndex < 0 || optionIndex == args.Length - 1) return null;

        var filePath = args[optionIndex + 1];
        if (!File.Exists(filePath)) return $"Файл импорта не найден: {filePath}";

        try
        {
            var imported = new QuickTextPasteImporter().Import(filePath, _store!.Snippets);
            foreach (var snippet in imported.Snippets) _store.Snippets.Add(snippet);
            _store.Save(_store.Snippets);
            return imported.Warnings.Count == 0
                ? $"Импортировано из QuickTextPaste: {imported.Snippets.Count}."
                : $"Импортировано: {imported.Snippets.Count}. Предупреждений: {imported.Warnings.Count}. {imported.Warnings[0]}";
        }
        catch (Exception ex)
        {
            return $"Импорт QuickTextPaste не выполнен: {ex.Message}";
        }
    }

    private void RegisterHotkeys()
    {
        if (_hotkeys is null || _store is null || _mainWindow is null)
            return;

        var result = _hotkeys.Rebuild(
            _mainWindow.ViewModel.Snippets,
            _store.Settings.SpotlightHotkey,
            ShowSearch,
            ShowSnippetList,
            (snippet, target) => _ = ExecuteSnippetAsync(snippet, target));

        // Alt+Space is often reserved by Windows for the system menu. Keep it when it
        // works, but transparently fall back to a reliable global shortcut otherwise.
        if (!result.SearchRegistered && string.Equals(_store.Settings.SpotlightHotkey, "Alt+Space", StringComparison.OrdinalIgnoreCase))
        {
            _store.Settings.SpotlightHotkey = "Ctrl+Alt+Space";
            result = _hotkeys.Rebuild(
                _mainWindow.ViewModel.Snippets,
                _store.Settings.SpotlightHotkey,
                ShowSearch,
                ShowSnippetList,
                (snippet, target) => _ = ExecuteSnippetAsync(snippet, target));
            _store.Save(_mainWindow.ViewModel.Snippets);
        }

        _mainWindow.ViewModel.SearchHotkey = _store.Settings.SpotlightHotkey;

        _mainWindow.SetStatus(result.Failures.Count == 0
            ? $"Готово: активно сочетаний — {result.RegisteredCount}."
            : $"Не зарегистрировано сочетаний: {result.Failures.Count}. " + string.Join(" · ", result.Failures.Take(2)));
    }

    private void ShowSearch(ActiveTarget target)
    {
        _searchWindow?.ShowForTarget(target);
    }

    private void ShowSnippetList(IReadOnlyList<Snippet> snippets, ActiveTarget target)
    {
        var hotkey = snippets.FirstOrDefault()?.Hotkey ?? string.Empty;
        _searchWindow?.ShowForTarget(target, snippets, $"{hotkey}: {snippets.Count} фраз");
    }

    private async Task ExecuteSnippetAsync(Snippet snippet, ActiveTarget target)
    {
        try
        {
            if (snippet.Kind == SnippetKind.Launch)
            {
                LaunchService.Launch(snippet.Content);
                return;
            }

            await _input!.PasteTemplateAsync(snippet.Content, target);
        }
        catch (Exception ex)
        {
            _mainWindow?.SetStatus($"Не удалось выполнить «{snippet.Name}»: {ex.Message}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeys?.Dispose();
        _searchWindow?.Close();
        base.OnExit(e);
    }
}
