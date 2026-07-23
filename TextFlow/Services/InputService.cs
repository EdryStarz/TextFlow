using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using TextFlow.Native;

namespace TextFlow.Services;

/// <summary>
/// Performs one indivisible paste transaction. Concurrent calls are serialized so one
/// request cannot replace the clipboard between another request's write and Ctrl+V.
/// </summary>
public sealed class InputService
{
    private readonly SemaphoreSlim _pasteGate = new(1, 1);
    private readonly ITextClipboard _clipboard;
    private readonly ITargetActivator _targetActivator;
    private readonly IPasteShortcutSender _shortcutSender;

    public InputService()
        : this(new WpfTextClipboard(), new Win32TargetActivator(), new Win32PasteShortcutSender())
    {
    }

    public InputService(
        ITextClipboard clipboard,
        ITargetActivator targetActivator,
        IPasteShortcutSender shortcutSender)
    {
        _clipboard = clipboard;
        _targetActivator = targetActivator;
        _shortcutSender = shortcutSender;
    }

    public async Task PasteTemplateAsync(string template, ActiveTarget target)
    {
        await _pasteGate.WaitAsync();
        try
        {
            var clipboardText = _clipboard.GetTextOrEmpty();
            PasteTextCore(TemplateExpander.Expand(template, clipboardText), target);
        }
        finally
        {
            _pasteGate.Release();
        }
    }

    public async Task PasteTextAsync(string text, ActiveTarget target)
    {
        await _pasteGate.WaitAsync();
        try
        {
            PasteTextCore(text, target);
        }
        finally
        {
            _pasteGate.Release();
        }
    }

    private void PasteTextCore(string text, ActiveTarget target)
    {
        _clipboard.SetText(text);

        if (target.TopLevelWindow != IntPtr.Zero && !_targetActivator.Activate(target))
            throw new InvalidOperationException("Windows не вернула фокус в исходное окно ввода.");

        _shortcutSender.SendPasteShortcut();

        // Deliberately keep the inserted text on the clipboard. Restoring the previous
        // value before the target has consumed WM_PASTE is an unavoidable cross-process
        // race (especially in Chromium) and was the cause of intermittent empty pastes.
    }
}

public interface ITextClipboard
{
    string GetTextOrEmpty();
    void SetText(string text);
}

public interface ITargetActivator
{
    bool Activate(ActiveTarget target);
}

public interface IPasteShortcutSender
{
    void SendPasteShortcut();
}

internal sealed class WpfTextClipboard : ITextClipboard
{
    public string GetTextOrEmpty() => Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;

    public void SetText(string text) => Clipboard.SetText(text, TextDataFormat.UnicodeText);
}

internal sealed class Win32TargetActivator : ITargetActivator
{
    public bool Activate(ActiveTarget target)
    {
        if (NativeMethods.GetForegroundWindow() == target.TopLevelWindow)
            return true;

        return NativeMethods.SetForegroundWindow(target.TopLevelWindow)
            && NativeMethods.GetForegroundWindow() == target.TopLevelWindow;
    }
}

internal sealed class Win32PasteShortcutSender : IPasteShortcutSender
{
    private static readonly ushort[] ModifierKeys =
    [
        NativeMethods.VkLeftShift,
        NativeMethods.VkRightShift,
        NativeMethods.VkLeftControl,
        NativeMethods.VkRightControl,
        NativeMethods.VkLeftAlt,
        NativeMethods.VkRightAlt,
        NativeMethods.VkLeftWin,
        NativeMethods.VkRightWin
    ];

    public void SendPasteShortcut()
    {
        var pressedModifiers = ModifierKeys
            .Where(key => (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0)
            .ToArray();
        var inputs = CreatePasteInputs(pressedModifiers);
        var sent = NativeMethods.SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<NativeMethods.INPUT>());

        if (sent != inputs.Length)
        {
            var error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                error == 0
                    ? "Windows заблокировала эмуляцию клавиатуры (возможна разница уровней доступа)."
                    : new Win32Exception(error).Message);
        }
    }

    internal static NativeMethods.INPUT[] CreatePasteInputs(IReadOnlyCollection<ushort>? pressedModifiers = null)
    {
        // A single SendInput call preserves ordering and prevents user/system input from
        // being interleaved between modifier normalization, Ctrl+V and restoration.
        // This matters for direct global hotkeys, whose Alt/Shift/Win may still be down.
        pressedModifiers ??= [];
        var inputs = new List<NativeMethods.INPUT>(pressedModifiers.Count * 2 + 4);
        inputs.AddRange(pressedModifiers.Select(key => KeyInput(key, keyUp: true)));
        inputs.Add(KeyInput(NativeMethods.VkControl, keyUp: false));
        inputs.Add(KeyInput(NativeMethods.VkV, keyUp: false));
        inputs.Add(KeyInput(NativeMethods.VkV, keyUp: true));
        inputs.Add(KeyInput(NativeMethods.VkControl, keyUp: true));
        inputs.AddRange(pressedModifiers.Select(key => KeyInput(key, keyUp: false)));
        return inputs.ToArray();
    }

    private static NativeMethods.INPUT KeyInput(ushort key, bool keyUp) => new()
    {
        type = NativeMethods.InputKeyboard,
        U = new NativeMethods.InputUnion
        {
            ki = new NativeMethods.KEYBDINPUT
            {
                wVk = key,
                dwFlags = keyUp ? NativeMethods.KeyeventfKeyup : 0
            }
        }
    };
}
