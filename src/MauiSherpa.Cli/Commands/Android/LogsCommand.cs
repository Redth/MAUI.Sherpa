using System.CommandLine;
using MauiSherpa.Cli.Helpers;

namespace MauiSherpa.Cli.Commands.Android;

public static class LogsCommand
{
    public static Command Create()
    {
        var serialArg = new Argument<string>("serial") { Description = "Device or emulator serial number (from 'maui-sherpa android devices')" };

        var levelOpt = new Option<string?>("--level", "-l")
        {
            Description = "Minimum log level: verbose, debug, info, warning, error, fatal (default: verbose)",
        };

        var filterOpt = new Option<string?>("--filter", "-f")
        {
            Description = "Filter entries by tag or message substring (case-insensitive)",
        };

        var clearOpt = new Option<bool>("--clear")
        {
            Description = "Clear the logcat buffer before streaming",
        };

        var cmd = new Command("logs", "Stream real-time device logs from an Android device or emulator via adb logcat.")
        {
            serialArg,
            levelOpt,
            filterOpt,
            clearOpt,
        };

        cmd.SetAction(async (parseResult, ct) =>
        {
            var serial = parseResult.GetValue(serialArg)!;
            var json = parseResult.GetValue(CliOptions.Json);
            var level = parseResult.GetValue(levelOpt);
            var filter = parseResult.GetValue(filterOpt);
            var clear = parseResult.GetValue(clearOpt);
            await HandleAsync(serial, json, level, filter, clear, ct);
        });

        return cmd;
    }

    private static async Task HandleAsync(string serial, bool json, string? level, string? filter, bool clear, CancellationToken ct)
    {
        var adbPath = await ProcessRunner.WhichAsync("adb");
        if (adbPath is null)
        {
            Output.WriteError("adb not found on PATH. Install platform-tools: 'maui-sherpa android sdk install platform-tools'.");
            return;
        }

        var minLevel = ParseLevel(level);

        if (clear)
        {
            var clearResult = await ProcessRunner.RunAsync(adbPath, $"-s {serial} logcat -c");
            if (clearResult.ExitCode != 0)
            {
                Output.WriteError($"Failed to clear logcat: {clearResult.Error}");
                return;
            }
            if (!json) Output.WriteInfo("Logcat buffer cleared.");
        }

        if (!json)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"Streaming logs from {serial} (level ≥ {minLevel}). Press Ctrl+C to stop.");
            Console.ResetColor();
            Console.WriteLine();
        }

        LogcatEntry? lastEntry = null;

        await foreach (var line in StreamingProcess.RunAsync(adbPath, $"-s {serial} logcat -v threadtime", ct))
        {
            var entry = LogcatParser.Parse(line);
            if (entry is null && !string.IsNullOrWhiteSpace(line))
                entry = LogcatParser.CreateContinuation(line, lastEntry);
            if (entry is null) continue;

            lastEntry = entry;

            if (entry.Level < minLevel) continue;

            if (filter is not null &&
                !entry.Tag.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                !entry.Message.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;

            if (json)
            {
                Output.WriteJson(new
                {
                    timestamp = entry.Timestamp,
                    pid = entry.Pid,
                    tid = entry.Tid,
                    level = entry.Level.ToString().ToLowerInvariant(),
                    tag = entry.Tag,
                    message = entry.Message,
                });
            }
            else
            {
                WriteColoredEntry(entry);
            }
        }
    }

    private static void WriteColoredEntry(LogcatEntry entry)
    {
        var color = LogcatParser.GetColor(entry.Level);
        var levelChar = entry.Level switch
        {
            LogcatLevel.Verbose => 'V',
            LogcatLevel.Debug => 'D',
            LogcatLevel.Info => 'I',
            LogcatLevel.Warning => 'W',
            LogcatLevel.Error => 'E',
            LogcatLevel.Fatal => 'F',
            _ => '?',
        };

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($"{entry.Timestamp} ");
        Console.ForegroundColor = color;
        Console.Write($"{levelChar}/{entry.Tag}");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($"({entry.Pid}): ");
        Console.ForegroundColor = color;
        Console.WriteLine(entry.Message);
        Console.ResetColor();
    }

    private static LogcatLevel ParseLevel(string? level) => level?.ToLowerInvariant() switch
    {
        "verbose" or "v" => LogcatLevel.Verbose,
        "debug" or "d" => LogcatLevel.Debug,
        "info" or "i" => LogcatLevel.Info,
        "warning" or "warn" or "w" => LogcatLevel.Warning,
        "error" or "e" => LogcatLevel.Error,
        "fatal" or "f" => LogcatLevel.Fatal,
        _ => LogcatLevel.Verbose,
    };
}
