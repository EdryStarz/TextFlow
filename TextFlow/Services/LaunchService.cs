using System.Diagnostics;
using System.IO;

namespace TextFlow.Services;

public static class LaunchService
{
    public static void Launch(string rawCommand)
    {
        var command = Environment.ExpandEnvironmentVariables(rawCommand.Trim());
        if (string.IsNullOrWhiteSpace(command)) throw new InvalidOperationException("Не указан путь или команда запуска.");

        var (fileName, arguments) = SplitCommand(command);
        Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = true
        });
    }

    private static (string FileName, string Arguments) SplitCommand(string command)
    {
        // Preserve a directory/file with spaces when the whole value itself exists.
        if (File.Exists(command) || Directory.Exists(command) || Uri.TryCreate(command, UriKind.Absolute, out _))
            return (command, string.Empty);

        if (command[0] == '"')
        {
            var endQuote = command.IndexOf('"', 1);
            if (endQuote > 0) return (command[1..endQuote], command[(endQuote + 1)..].Trim());
        }

        var separator = command.IndexOfAny([' ', '\t']);
        return separator < 0
            ? (command, string.Empty)
            : (command[..separator], command[(separator + 1)..].Trim());
    }
}
