using System.CommandLine;
using System.Text.Json;
using MauiSherpa.Cli.Helpers;

namespace MauiSherpa.Cli.Commands.Apple;

public static class LogsCommand
{
    public static Command Create()
    {
        var identifierArg = new Argument<string>("identifier") { Description = "Simulator UDID or physical device identifier (from 'maui-sherpa apple simulators list' or 'maui-sherpa apple devices')" };

        var levelOpt = new Option<string?>("--level", "-l")
        {
            Description = "Minimum log level: default, debug, info, error, fault (default: default)",
        };

        var filterOpt = new Option<string?>("--filter", "-f")
        {
            Description = "Filter entries by process name, subsystem, or message substring (case-insensitive)",
        };

        var simulatorOpt = new Option<bool>("--simulator")
        {
            Description = "Force simulator mode (skip auto-detection)",
        };

        var deviceOpt = new Option<bool>("--device")
        {
            Description = "Force physical device mode (skip auto-detection)",
        };

        var cmd = new Command("logs", "Stream real-time device logs from an iOS simulator or physical device.")
        {
            identifierArg,
            levelOpt,
            filterOpt,
            simulatorOpt,
            deviceOpt,
        };

        cmd.SetAction(async (parseResult, ct) =>
        {
            var identifier = parseResult.GetValue(identifierArg)!;
            var json = parseResult.GetValue(CliOptions.Json);
            var level = parseResult.GetValue(levelOpt);
            var filter = parseResult.GetValue(filterOpt);
            var forceSimulator = parseResult.GetValue(simulatorOpt);
            var forceDevice = parseResult.GetValue(deviceOpt);
            await HandleAsync(identifier, json, level, filter, forceSimulator, forceDevice, ct);
        });

        return cmd;
    }

    private static async Task HandleAsync(string identifier, bool json, string? level, string? filter,
        bool forceSimulator, bool forceDevice, CancellationToken ct)
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsMacCatalyst())
        {
            Output.WriteError("Apple log streaming is only supported on macOS.");
            return;
        }

        var minLevel = ParseLevel(level);

        bool isSimulator;
        if (forceSimulator) isSimulator = true;
        else if (forceDevice) isSimulator = false;
        else isSimulator = await IsSimulatorAsync(identifier);

        if (isSimulator)
            await StreamSimulatorLogsAsync(identifier, json, minLevel, filter, ct);
        else
            await StreamPhysicalDeviceLogsAsync(identifier, json, minLevel, filter, ct);
    }

    private static async Task StreamSimulatorLogsAsync(string udid, bool json, SimLogLevel minLevel, string? filter, CancellationToken ct)
    {
        var xcrunPath = await ProcessRunner.WhichAsync("xcrun");
        if (xcrunPath is null)
        {
            Output.WriteError("xcrun not found. Ensure Xcode is installed and xcode-select is configured.");
            return;
        }

        if (!json)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"Streaming simulator logs from {udid} (level ≥ {minLevel}). Press Ctrl+C to stop.");
            Console.ResetColor();
            Console.WriteLine();
        }

        await foreach (var line in StreamingProcess.RunAsync(xcrunPath, $"simctl spawn {udid} log stream --style ndjson --level debug", ct))
        {
            var entry = SimulatorLogParser.Parse(line);
            if (entry is null) continue;

            if (entry.Level < minLevel) continue;

            if (!MatchesFilter(entry, filter)) continue;

            WriteEntry(entry, json);
        }
    }

    private static async Task StreamPhysicalDeviceLogsAsync(string identifier, bool json, SimLogLevel minLevel, string? filter, CancellationToken ct)
    {
        var (command, args) = await FindPhysicalLogCommandAsync(identifier);
        if (command is null)
        {
            Output.WriteError("No iOS device logging tool found. Install pymobiledevice3 (pip3 install pymobiledevice3) or libimobiledevice (brew install libimobiledevice).");
            return;
        }

        var isPymobiledevice3 = command.Contains("pymobiledevice3");

        if (!json)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"Streaming device logs from {identifier} via {Path.GetFileName(command)} (level ≥ {minLevel}). Press Ctrl+C to stop.");
            Console.ResetColor();
            Console.WriteLine();
        }

        await foreach (var line in StreamingProcess.RunAsync(command, args, ct))
        {
            var entry = PhysicalDeviceLogParser.Parse(line);
            if (entry is null)
            {
                // pymobiledevice3 can output non-syslog lines; show as raw if no level filter
                if (isPymobiledevice3 && minLevel == SimLogLevel.Default && !string.IsNullOrWhiteSpace(line))
                {
                    if (filter is not null && !line.Contains(filter, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (json)
                        Output.WriteJson(new { timestamp = "", processId = 0, level = "default", processName = "", message = line });
                    else
                        Console.WriteLine(line);
                }
                continue;
            }

            if (entry.Level < minLevel) continue;

            if (!MatchesFilter(entry, filter)) continue;

            WriteEntry(entry, json);
        }
    }

    private static void WriteEntry(SimLogEntry entry, bool json)
    {
        if (json)
        {
            Output.WriteJson(new
            {
                timestamp = entry.Timestamp,
                processId = entry.ProcessId,
                threadId = entry.ThreadId,
                level = entry.Level.ToString().ToLowerInvariant(),
                processName = entry.ProcessName,
                subsystem = entry.Subsystem,
                category = entry.Category,
                message = entry.Message,
            });
        }
        else
        {
            WriteColoredEntry(entry);
        }
    }

    private static void WriteColoredEntry(SimLogEntry entry)
    {
        var color = SimulatorLogParser.GetColor(entry.Level);

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($"{entry.Timestamp} ");
        Console.ForegroundColor = color;
        Console.Write($"[{entry.Level}]");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($" {entry.ProcessName}({entry.ProcessId})");
        if (entry.Subsystem is not null)
            Console.Write($" {entry.Subsystem}");
        if (entry.Category is not null)
            Console.Write($":{entry.Category}");
        Console.Write(": ");
        Console.ForegroundColor = color;
        Console.WriteLine(entry.Message);
        Console.ResetColor();
    }

    private static bool MatchesFilter(SimLogEntry entry, string? filter)
    {
        if (filter is null) return true;
        return entry.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               entry.Message.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               (entry.Subsystem?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (entry.Category?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static async Task<bool> IsSimulatorAsync(string identifier)
    {
        try
        {
            var result = await ProcessRunner.RunAsync("xcrun", "simctl list devices -j");
            if (result.ExitCode != 0) return false;

            using var doc = JsonDocument.Parse(result.Output);
            if (!doc.RootElement.TryGetProperty("devices", out var devices)) return false;

            foreach (var runtime in devices.EnumerateObject())
            {
                foreach (var device in runtime.Value.EnumerateArray())
                {
                    var udid = device.TryGetProperty("udid", out var u) ? u.GetString() : null;
                    if (string.Equals(udid, identifier, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
        }
        catch { /* fall through to false */ }

        return false;
    }

    private static async Task<(string? Command, string Args)> FindPhysicalLogCommandAsync(string identifier)
    {
        // Try pymobiledevice3 first (iOS 17+)
        var pyPath = await FindPymobiledevice3Async();
        if (pyPath is not null)
            return (pyPath, $"syslog live --mobdev2 --udid {identifier}");

        // Fallback to idevicesyslog (libimobiledevice)
        var idevicePath = await ProcessRunner.WhichAsync("idevicesyslog");
        if (idevicePath is not null)
            return (idevicePath, $"-u {identifier}");

        return (null, "");
    }

    private static async Task<string?> FindPymobiledevice3Async()
    {
        var onPath = await ProcessRunner.WhichAsync("pymobiledevice3");
        if (onPath is not null) return onPath;

        // Check common venv locations
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] venvPaths =
        [
            Path.Combine(home, ".venv", "bin", "pymobiledevice3"),
            Path.Combine(home, "pymobiledevice3-venv", "bin", "pymobiledevice3"),
            "/opt/homebrew/bin/pymobiledevice3",
            "/usr/local/bin/pymobiledevice3",
        ];

        foreach (var path in venvPaths)
        {
            if (File.Exists(path)) return path;
        }

        return null;
    }

    private static SimLogLevel ParseLevel(string? level) => level?.ToLowerInvariant() switch
    {
        "debug" or "d" => SimLogLevel.Debug,
        "info" or "i" => SimLogLevel.Info,
        "error" or "e" => SimLogLevel.Error,
        "fault" or "f" => SimLogLevel.Fault,
        _ => SimLogLevel.Default,
    };
}
