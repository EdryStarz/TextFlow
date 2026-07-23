using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using TextFlow.Models;
using TextFlow.Native;

namespace TextFlow.Services;

public sealed class HotkeyService : IDisposable
{
    private readonly Dictionary<int, Action<ActiveTarget>> _bindings = [];
    private HwndSource? _source;
    private IntPtr _windowHandle;
    private int _nextId = 100;

    public void Attach(Window window)
    {
        _windowHandle = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(_windowHandle)
                  ?? throw new InvalidOperationException("Не удалось создать окно-получатель hotkey сообщений.");
        _source.AddHook(WindowProcedure);
    }

    public HotkeyRegistrationReport Rebuild(
        IEnumerable<Snippet> snippets,
        string spotlightHotkey,
        Action<ActiveTarget> showSearch,
        Action<IReadOnlyList<Snippet>, ActiveTarget> showSnippetList,
        Action<Snippet, ActiveTarget> executeSnippet)
    {
        EnsureAttached();
        ClearBindings();
        var report = new HotkeyRegistrationReport();

        Register(spotlightHotkey, "поиск", showSearch, report, isSearch: true);

        // QuickTextPaste allows a single hotkey to own a menu of phrases. Register that
        // key once, then present the matching phrases in the palette instead of dropping
        // all but the first phrase as a Windows hotkey collision.
        var groups = snippets
            .Where(s => !string.IsNullOrWhiteSpace(s.Hotkey))
            .GroupBy(s => Normalize(s.Hotkey), StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var groupItems = group.ToList();
            var hotkey = groupItems[0].Hotkey;
            if (string.Equals(Normalize(hotkey), Normalize(spotlightHotkey), StringComparison.OrdinalIgnoreCase))
            {
                report.Failures.Add($"{hotkey}: конфликт с сочетанием поиска");
                continue;
            }

            if (groupItems.Count == 1)
            {
                var snippet = groupItems[0];
                Register(hotkey, snippet.Name, target => executeSnippet(snippet, target), report);
            }
            else
            {
                Register(hotkey, $"{hotkey}: меню из {groupItems.Count}", target => showSnippetList(groupItems, target), report);
            }
        }

        return report;
    }

    private void Register(string value, string label, Action<ActiveTarget> action, HotkeyRegistrationReport report, bool isSearch = false)
    {
        if (!HotkeyGesture.TryParse(value, out var gesture, out var reason))
        {
            report.Failures.Add($"{label}: {reason}");
            return;
        }

        var id = _nextId++;
        if (!NativeMethods.RegisterHotKey(_windowHandle, id, gesture.Modifiers | HotkeyModifiers.NoRepeat, gesture.VirtualKey))
        {
            var error = new Win32Exception().Message;
            report.Failures.Add($"{label} ({value}): уже занято или запрещено Windows ({error})");
            return;
        }

        _bindings[id] = action;
        report.RegisteredCount++;
        if (isSearch) report.SearchRegistered = true;
    }

    private IntPtr WindowProcedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != NativeMethods.WmHotkey || !_bindings.TryGetValue(wParam.ToInt32(), out var action))
            return IntPtr.Zero;

        // Save both the foreground top-level window and its focused child control before
        // the palette takes activation away from the source application.
        action(CaptureActiveTarget());
        handled = true;
        return IntPtr.Zero;
    }

    private void ClearBindings()
    {
        foreach (var id in _bindings.Keys) NativeMethods.UnregisterHotKey(_windowHandle, id);
        _bindings.Clear();
    }

    private static ActiveTarget CaptureActiveTarget()
    {
        var topLevelWindow = NativeMethods.GetForegroundWindow();
        if (topLevelWindow == IntPtr.Zero) return default;

        var threadId = NativeMethods.GetWindowThreadProcessId(topLevelWindow, out _);
        var info = new NativeMethods.GUITHREADINFO { cbSize = Marshal.SizeOf<NativeMethods.GUITHREADINFO>() };
        var focusWindow = threadId != 0 && NativeMethods.GetGUIThreadInfo(threadId, ref info)
            ? info.hwndFocus
            : IntPtr.Zero;
        return new ActiveTarget(topLevelWindow, focusWindow);
    }

    private void EnsureAttached()
    {
        if (_source is null) throw new InvalidOperationException("Сначала вызовите Attach(window).");
    }

    private static string Normalize(string value) => value
        .Replace("L-Win", "Win", StringComparison.OrdinalIgnoreCase)
        .Replace("R-Win", "Win", StringComparison.OrdinalIgnoreCase)
        .Replace("L-Ctrl", "Ctrl", StringComparison.OrdinalIgnoreCase)
        .Replace("R-Ctrl", "Ctrl", StringComparison.OrdinalIgnoreCase)
        .Replace("Control", "Ctrl", StringComparison.OrdinalIgnoreCase)
        .Replace(" ", string.Empty, StringComparison.Ordinal)
        .ToUpperInvariant();

    public void Dispose()
    {
        if (_source is not null) _source.RemoveHook(WindowProcedure);
        if (_windowHandle != IntPtr.Zero) ClearBindings();
        _source = null;
    }
}

public sealed class HotkeyRegistrationReport
{
    public int RegisteredCount { get; internal set; }
    public bool SearchRegistered { get; internal set; }
    public List<string> Failures { get; } = [];
}

public readonly record struct ActiveTarget(IntPtr TopLevelWindow, IntPtr FocusWindow);

public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, uint VirtualKey)
{
    public static bool TryParse(string raw, out HotkeyGesture gesture, out string reason)
    {
        gesture = default;
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(raw)) { reason = "пустое сочетание"; return false; }

        var parts = raw.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) { reason = "пустое сочетание"; return false; }

        HotkeyModifiers modifiers = HotkeyModifiers.None;
        string? keyName = null;
        foreach (var part in parts)
        {
            switch (part.Replace(" ", string.Empty).ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                case "L-CTRL":
                case "R-CTRL": modifiers |= HotkeyModifiers.Control; break;
                case "ALT":
                case "L-ALT":
                case "R-ALT": modifiers |= HotkeyModifiers.Alt; break;
                case "SHIFT":
                case "L-SHIFT":
                case "R-SHIFT": modifiers |= HotkeyModifiers.Shift; break;
                case "WIN":
                case "WINDOWS":
                case "L-WIN":
                case "R-WIN": modifiers |= HotkeyModifiers.Win; break;
                default:
                    if (keyName is not null) { reason = "указано больше одной основной клавиши"; return false; }
                    keyName = part;
                    break;
            }
        }

        if (keyName is null || (modifiers == HotkeyModifiers.None && !IsFunctionKey(keyName)))
        {
            reason = "нужна клавиша с Ctrl, Alt или Ctrl+Shift; без модификатора допустимы только F1–F12";
            return false;
        }

        if (!TryGetVirtualKey(keyName, out var vk)) { reason = $"неподдерживаемая клавиша «{keyName}»"; return false; }
        gesture = new HotkeyGesture(modifiers, vk);
        return true;
    }

    private static bool IsFunctionKey(string value) => value.Trim().ToUpperInvariant() switch
    {
        "F1" or "F2" or "F3" or "F4" or "F5" or "F6" or "F7" or "F8" or "F9" or "F10" or "F11" or "F12" => true,
        _ => false
    };

    private static bool TryGetVirtualKey(string raw, out uint vk)
    {
        var normalized = raw.Trim().Replace(" ", string.Empty).Replace("-", string.Empty);
        if (normalized.Length == 1 && char.IsLetterOrDigit(normalized[0]))
        {
            vk = char.ToUpperInvariant(normalized[0]);
            return true;
        }

        var aliases = new Dictionary<string, Key>(StringComparer.OrdinalIgnoreCase)
        {
            ["SPACE"] = Key.Space, ["TAB"] = Key.Tab, ["ENTER"] = Key.Enter,
            ["ESC"] = Key.Escape, ["ESCAPE"] = Key.Escape, ["UP"] = Key.Up,
            ["DOWN"] = Key.Down, ["LEFT"] = Key.Left, ["RIGHT"] = Key.Right,
            ["DELETE"] = Key.Delete, ["INSERT"] = Key.Insert, ["HOME"] = Key.Home,
            ["END"] = Key.End, ["PGUP"] = Key.PageUp, ["PGDN"] = Key.PageDown
        };
        if (aliases.TryGetValue(normalized, out var alias))
        {
            vk = (uint)KeyInterop.VirtualKeyFromKey(alias);
            return true;
        }

        if (Enum.TryParse<Key>(normalized, true, out var key))
        {
            vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            return vk != 0;
        }
        vk = 0;
        return false;
    }
}
