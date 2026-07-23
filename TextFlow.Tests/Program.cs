using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using TextFlow.Native;
using TextFlow.Services;

namespace TextFlow.Tests;

internal static class Program
{
    private static readonly List<(string Name, Func<Task> Run)> Tests =
    [
        ("direct paste keeps clipboard and injects once", DirectPaste),
        ("template reads non-empty clipboard", TemplateWithClipboard),
        ("template handles empty clipboard", TemplateWithEmptyClipboard),
        ("focus change activates captured target before input", FocusChange),
        ("activation failure never injects", ActivationFailure),
        ("rapid concurrent calls do not overwrite each other", RapidConcurrentPaste),
        ("repeated calls neither skip nor duplicate", RepeatedCalls),
        ("native INPUT ABI and atomic shortcut are valid", NativeInputContract),
        ("function keys are accepted as global hotkeys", FunctionKeysAreAccepted)
    ];

    [STAThread]
    private static async Task<int> Main()
    {
        var failures = new List<string>();
        foreach (var test in Tests)
        {
            try
            {
                await test.Run();
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception ex)
            {
                failures.Add($"FAIL {test.Name}: {ex.Message}");
                Console.WriteLine(failures[^1]);
            }
        }

        Console.WriteLine($"{Tests.Count - failures.Count}/{Tests.Count} passed");
        return failures.Count == 0 ? 0 : 1;
    }

    private static async Task DirectPaste()
    {
        var fixture = new Fixture("old");
        await fixture.Service.PasteTextAsync("new", Target(11));

        Equal("new", fixture.Clipboard.Text);
        SequenceEqual(["activate:11", "inject:new"], fixture.Events);
        Equal(1, fixture.Sender.Count);
    }

    private static async Task TemplateWithClipboard()
    {
        var fixture = new Fixture("copied");
        await fixture.Service.PasteTemplateAsync("before %pptxt% after", Target(12));
        Equal("before copied after", fixture.Clipboard.Text);
        SequenceEqual(["before copied after"], fixture.Sender.Inserted);
    }

    private static async Task TemplateWithEmptyClipboard()
    {
        var fixture = new Fixture(string.Empty);
        await fixture.Service.PasteTemplateAsync("%pptxt%value", Target(13));
        Equal("value", fixture.Clipboard.Text);
        Equal(1, fixture.Sender.Count);
    }

    private static async Task FocusChange()
    {
        var fixture = new Fixture(string.Empty);
        await fixture.Service.PasteTextAsync("first", Target(21));
        await fixture.Service.PasteTextAsync("second", Target(22));

        SequenceEqual([21L, 22L], fixture.Activator.Targets);
        SequenceEqual(["activate:21", "inject:first", "activate:22", "inject:second"], fixture.Events);
    }

    private static async Task ActivationFailure()
    {
        var fixture = new Fixture(string.Empty) { Activator = { Succeeds = false } };
        await Throws<InvalidOperationException>(() => fixture.Service.PasteTextAsync("blocked", Target(31)));
        Equal(0, fixture.Sender.Count);
    }

    private static async Task RapidConcurrentPaste()
    {
        var fixture = new Fixture(string.Empty);
        fixture.Sender.BlockFirstCall = true;

        var first = Task.Run(() => fixture.Service.PasteTextAsync("one", Target(41)));
        True(fixture.Sender.FirstCallEntered.Wait(TimeSpan.FromSeconds(2)), "first call did not reach input sender");
        var second = Task.Run(() => fixture.Service.PasteTextAsync("two", Target(42)));

        // The second call must remain outside the transaction while the first is blocked.
        Equal(1, fixture.Clipboard.Writes.Count);
        fixture.Sender.ReleaseFirstCall.Set();
        await Task.WhenAll(first, second);

        SequenceEqual(["one", "two"], fixture.Sender.Inserted);
        Equal(2, fixture.Sender.Count);
    }

    private static async Task RepeatedCalls()
    {
        var fixture = new Fixture(string.Empty);
        for (var index = 0; index < 50; index++)
            await fixture.Service.PasteTextAsync($"value-{index}", Target(50));

        Equal(50, fixture.Sender.Count);
        SequenceEqual(Enumerable.Range(0, 50).Select(i => $"value-{i}"), fixture.Sender.Inserted);
    }

    private static Task NativeInputContract()
    {
        Equal(Environment.Is64BitProcess ? 40 : 28, Marshal.SizeOf<NativeMethods.INPUT>());
        var inputs = Win32PasteShortcutSender.CreatePasteInputs();
        Equal(4, inputs.Length);
        Equal(NativeMethods.VkControl, inputs[0].U.ki.wVk);
        Equal(NativeMethods.VkV, inputs[1].U.ki.wVk);
        Equal(NativeMethods.VkV, inputs[2].U.ki.wVk);
        Equal(NativeMethods.VkControl, inputs[3].U.ki.wVk);
        Equal(0u, inputs[0].U.ki.dwFlags);
        Equal(NativeMethods.KeyeventfKeyup, inputs[2].U.ki.dwFlags);
        Equal(NativeMethods.KeyeventfKeyup, inputs[3].U.ki.dwFlags);

        var withHeldShift = Win32PasteShortcutSender.CreatePasteInputs([NativeMethods.VkLeftShift]);
        Equal(6, withHeldShift.Length);
        Equal(NativeMethods.VkLeftShift, withHeldShift[0].U.ki.wVk);
        Equal(NativeMethods.KeyeventfKeyup, withHeldShift[0].U.ki.dwFlags);
        Equal(NativeMethods.VkControl, withHeldShift[1].U.ki.wVk);
        Equal(NativeMethods.VkLeftShift, withHeldShift[5].U.ki.wVk);
        Equal(0u, withHeldShift[5].U.ki.dwFlags);
        return Task.CompletedTask;
    }

    private static Task FunctionKeysAreAccepted()
    {
        True(HotkeyGesture.TryParse("F1", out var f1, out var f1Reason), f1Reason);
        Equal(HotkeyModifiers.None, f1.Modifiers);
        True(HotkeyGesture.TryParse("F12", out var f12, out var f12Reason), f12Reason);
        Equal(HotkeyModifiers.None, f12.Modifiers);
        True(HotkeyGesture.TryParse("Ctrl+Shift+Z", out _, out var ctrlReason), ctrlReason);
        True(HotkeyGesture.TryParse("Alt+A", out _, out var altReason), altReason);
        True(!HotkeyGesture.TryParse("A", out _, out _), "plain letter must not be accepted");
        return Task.CompletedTask;
    }

    private static ActiveTarget Target(long handle) => new(new IntPtr(handle), IntPtr.Zero);

    private static async Task Throws<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}");
    }

    private static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'");
    }

    private static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException($"Expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]");
    }

    private sealed class Fixture
    {
        public Fixture(string clipboardText)
        {
            Clipboard = new FakeClipboard(clipboardText);
            Activator = new FakeActivator(Events);
            Sender = new FakeSender(Clipboard, Events);
            Service = new InputService(Clipboard, Activator, Sender);
        }

        public ConcurrentQueue<string> Events { get; } = new();
        public FakeClipboard Clipboard { get; }
        public FakeActivator Activator { get; }
        public FakeSender Sender { get; }
        public InputService Service { get; }
    }

    private sealed class FakeClipboard(string text) : ITextClipboard
    {
        private readonly object _sync = new();
        private string _text = text;
        public ConcurrentQueue<string> Writes { get; } = new();
        public string Text { get { lock (_sync) return _text; } }
        public string GetTextOrEmpty() => Text;
        public void SetText(string value)
        {
            lock (_sync) _text = value;
            Writes.Enqueue(value);
        }
    }

    private sealed class FakeActivator(ConcurrentQueue<string> events) : ITargetActivator
    {
        public bool Succeeds { get; set; } = true;
        public ConcurrentQueue<long> Targets { get; } = new();
        public bool Activate(ActiveTarget target)
        {
            var value = target.TopLevelWindow.ToInt64();
            Targets.Enqueue(value);
            events.Enqueue($"activate:{value}");
            return Succeeds;
        }
    }

    private sealed class FakeSender(FakeClipboard clipboard, ConcurrentQueue<string> events) : IPasteShortcutSender
    {
        private int _count;
        public int Count => _count;
        public bool BlockFirstCall { get; set; }
        public ManualResetEventSlim FirstCallEntered { get; } = new(false);
        public ManualResetEventSlim ReleaseFirstCall { get; } = new(false);
        public ConcurrentQueue<string> Inserted { get; } = new();

        public void SendPasteShortcut()
        {
            var call = Interlocked.Increment(ref _count);
            var value = clipboard.Text;
            if (BlockFirstCall && call == 1)
            {
                FirstCallEntered.Set();
                ReleaseFirstCall.Wait(TimeSpan.FromSeconds(2));
            }
            Inserted.Enqueue(value);
            events.Enqueue($"inject:{value}");
        }
    }
}
