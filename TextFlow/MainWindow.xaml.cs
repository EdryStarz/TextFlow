using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using TextFlow.Models;
using TextFlow.Native;
using TextFlow.Services;
using TextFlow.ViewModels;

namespace TextFlow;

public partial class MainWindow : Window
{
    private readonly SnippetStore _store;

    public MainWindow(SnippetStore store)
    {
        _store = store;
        ViewModel = new MainViewModel(store.Snippets);
        DataContext = ViewModel;
        InitializeComponent();
        SourceInitialized += (_, _) => NativeMethods.EnableDarkTitleBar(new WindowInteropHelper(this).Handle);
        Deactivated += (_, _) =>
        {
            var foreground = NativeMethods.GetForegroundWindow();
            if (foreground != IntPtr.Zero) LastExternalWindow = foreground;
        };
        SetStatus("Создайте сниппет или импортируйте QuickTextPaste.ini.");
    }

    public MainViewModel ViewModel { get; }
    public IntPtr LastExternalWindow { get; private set; }
    public event EventHandler? SnippetsChanged;
    public event EventHandler? SearchRequested;

    public void SetStatus(string text) => Dispatcher.InvokeAsync(() => ViewModel.Status = text);

    private void New_Click(object sender, RoutedEventArgs e)
    {
        CreateNewSnippet();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        SaveAll();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Selected is not null) DeleteSnippet(ViewModel.Selected, askForConfirmation: true);
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите QuickTextPaste.ini",
            Filter = "QuickTextPaste.ini|*.ini|Все файлы|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var import = new QuickTextPasteImporter().Import(dialog.FileName, ViewModel.Snippets);
            ViewModel.AddRange(import.Snippets);
            SaveAll();
            SetStatus(import.Warnings.Count == 0
                ? $"Импортировано: {import.Snippets.Count}. Проверьте hotkey, занятые Windows."
                : $"Импортировано: {import.Snippets.Count}. Предупреждений: {import.Warnings.Count}. {import.Warnings[0]}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Не удалось импортировать", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Search_Click(object sender, RoutedEventArgs e) => SearchRequested?.Invoke(this, EventArgs.Empty);

    private void TextKind_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Selected is not null) ViewModel.Selected.Kind = SnippetKind.Text;
    }

    private void LaunchKind_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Selected is not null) ViewModel.Selected.Kind = SnippetKind.Launch;
    }

    private void HotkeySelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { SelectedItem: HotkeyOption option } || ViewModel.Selected is null)
            return;

        ViewModel.Selected.Hotkey = option.Value;
        SnippetsChanged?.Invoke(this, EventArgs.Empty);
        SetStatus(option.IsOccupied
            ? $"{option.Value} уже назначена: {option.OccupiedBy}. По сочетанию откроется список фраз."
            : string.IsNullOrEmpty(option.Value)
                ? "Горячая клавиша снята."
                : $"Назначено: {option.Value}. Нажмите «Сохранить изменения», чтобы записать на диск.");
    }

    public void EditSnippet(Snippet snippet)
    {
        ViewModel.Selected = snippet;
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Dispatcher.InvokeAsync(() =>
        {
            ContentEditor.Focus();
            ContentEditor.SelectAll();
        });
    }

    public void CreateNewSnippet()
    {
        ViewModel.AddNew();
        SaveAll();
        SetStatus("Новый сниппет добавлен. Укажите название, текст и hotkey.");
        if (!IsVisible) Show();
        Activate();
        Dispatcher.InvokeAsync(() =>
        {
            TitleEditor.Focus();
            TitleEditor.SelectAll();
        });
    }

    public void DeleteSnippet(Snippet snippet, bool askForConfirmation = false)
    {
        ViewModel.Selected = snippet;
        if (askForConfirmation)
        {
            var answer = MessageBox.Show($"Удалить «{snippet.Name}»?", "TextFlow", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
        }
        ViewModel.RemoveSelected();
        SaveAll();
        SetStatus("Сниппет удалён.");
    }

    private void SaveAll()
    {
        ViewModel.SortByName();
        _store.Save(ViewModel.Snippets);
        SnippetsChanged?.Invoke(this, EventArgs.Empty);
    }
}
