using System.Text.Json;
using System.Text.RegularExpressions;

namespace MauiSherpa.Cli.Helpers;

#region Android Logcat

public enum LogcatLevel { Verbose = 0, Debug = 1, Info = 2, Warning = 3, Error = 4, Fatal = 5 }

public record LogcatEntry(
    string Timestamp,
    int Pid,
    int Tid,
    LogcatLevel Level,
    string Tag,
    string Message,
    string RawLine);

public static partial class LogcatParser
{
    // adb logcat -v threadtime format:
    // MM-DD HH:MM:SS.mmm  PID  TID  LEVEL  TAG: MESSAGE
    [GeneratedRegex(@"^(\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}\.\d{3})\s+(\d+)\s+(\d+)\s+([VDIWEFA])\s+(.+?):\s(.*)$")]
    private static partial Regex ThreadtimePattern();

    public static LogcatEntry? Parse(string line)
    {
        var m = ThreadtimePattern().Match(line);
        if (!m.Success) return null;

        var level = m.Groups[4].Value[0] switch
        {
            'V' => LogcatLevel.Verbose,
            'D' => LogcatLevel.Debug,
            'I' => LogcatLevel.Info,
            'W' => LogcatLevel.Warning,
            'E' => LogcatLevel.Error,
            'F' or 'A' => LogcatLevel.Fatal,
            _ => LogcatLevel.Verbose,
        };

        return new LogcatEntry(
            Timestamp: m.Groups[1].Value,
            Pid: int.TryParse(m.Groups[2].Value, out var pid) ? pid : 0,
            Tid: int.TryParse(m.Groups[3].Value, out var tid) ? tid : 0,
            Level: level,
            Tag: m.Groups[5].Value.Trim(),
            Message: m.Groups[6].Value,
            RawLine: line);
    }

    public static LogcatEntry CreateContinuation(string line, LogcatEntry? previous) =>
        new(
            Timestamp: previous?.Timestamp ?? "",
            Pid: previous?.Pid ?? 0,
            Tid: previous?.Tid ?? 0,
            Level: previous?.Level ?? LogcatLevel.Verbose,
            Tag: previous?.Tag ?? "",
            Message: line,
            RawLine: line);

    public static ConsoleColor GetColor(LogcatLevel level) => level switch
    {
        LogcatLevel.Verbose => ConsoleColor.DarkGray,
        LogcatLevel.Debug => ConsoleColor.Cyan,
        LogcatLevel.Info => ConsoleColor.Green,
        LogcatLevel.Warning => ConsoleColor.Yellow,
        LogcatLevel.Error => ConsoleColor.Red,
        LogcatLevel.Fatal => ConsoleColor.Magenta,
        _ => ConsoleColor.Gray,
    };
}

#endregion

#region iOS Simulator / Physical Device Logs

public enum SimLogLevel { Default = 0, Info = 1, Debug = 2, Error = 3, Fault = 4 }

public record SimLogEntry(
    string Timestamp,
    int ProcessId,
    int ThreadId,
    SimLogLevel Level,
    string ProcessName,
    string? Subsystem,
    string? Category,
    string Message,
    string RawLine);

public static class SimulatorLogParser
{
    public static SimLogEntry? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (GetString(root, "eventType") != "logEvent") return null;

            var level = GetString(root, "messageType") switch
            {
                "Debug" => SimLogLevel.Debug,
                "Info" => SimLogLevel.Info,
                "Error" => SimLogLevel.Error,
                "Fault" => SimLogLevel.Fault,
                _ => SimLogLevel.Default,
            };

            var processPath = GetString(root, "processImagePath") ?? "";
            var processName = Path.GetFileName(processPath);

            return new SimLogEntry(
                Timestamp: GetString(root, "timestamp") ?? "",
                ProcessId: root.TryGetProperty("processID", out var pidEl) && pidEl.TryGetInt32(out var pid) ? pid : 0,
                ThreadId: root.TryGetProperty("threadID", out var tidEl) && tidEl.TryGetInt32(out var tid) ? tid : 0,
                Level: level,
                ProcessName: processName,
                Subsystem: GetString(root, "subsystem"),
                Category: GetString(root, "category"),
                Message: GetString(root, "eventMessage") ?? "",
                RawLine: line);
        }
        catch
        {
            return null;
        }
    }

    private static string? GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var val) && val.ValueKind == JsonValueKind.String ? val.GetString() : null;

    public static ConsoleColor GetColor(SimLogLevel level) => level switch
    {
        SimLogLevel.Default => ConsoleColor.DarkGray,
        SimLogLevel.Debug => ConsoleColor.Cyan,
        SimLogLevel.Info => ConsoleColor.Green,
        SimLogLevel.Error => ConsoleColor.Red,
        SimLogLevel.Fault => ConsoleColor.Magenta,
        _ => ConsoleColor.Gray,
    };
}

public static partial class PhysicalDeviceLogParser
{
    // Syslog format: "Mon DD HH:MM:SS DeviceName process[pid] <Level>: message"
    [GeneratedRegex(@"^(\w{3}\s+\d+\s+\d{2}:\d{2}:\d{2})\s+\S+\s+(\S+?)\[(\d+)\]\s*(?:<(\w+)>)?:\s*(.*)$")]
    private static partial Regex SyslogPattern();

    public static SimLogEntry? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        var m = SyslogPattern().Match(line);
        if (!m.Success) return null;

        var level = m.Groups[4].Value switch
        {
            "Error" => SimLogLevel.Error,
            "Fault" => SimLogLevel.Fault,
            "Warning" => SimLogLevel.Error,
            "Info" => SimLogLevel.Info,
            "Debug" => SimLogLevel.Debug,
            "Notice" => SimLogLevel.Info,
            _ => SimLogLevel.Default,
        };

        return new SimLogEntry(
            Timestamp: m.Groups[1].Value,
            ProcessId: int.TryParse(m.Groups[3].Value, out var pid) ? pid : 0,
            ThreadId: 0,
            Level: level,
            ProcessName: m.Groups[2].Value,
            Subsystem: null,
            Category: null,
            Message: m.Groups[5].Value,
            RawLine: line);
    }
}

#endregion
