using System.Text.Json;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Parses ndjson output from xcrun simctl spawn log stream --style ndjson
/// </summary>
public static class SimulatorLogParser
{
    public static SimulatorLogEntry? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            // Skip non-log events (e.g., activityCreateEvent)
            var eventType = GetString(root, "eventType");
            if (eventType != "logEvent")
                return null;

            var timestamp = GetString(root, "timestamp") ?? "";
            var processId = root.TryGetProperty("processID", out var pid) ? pid.GetInt32() : 0;
            var threadId = root.TryGetProperty("threadID", out var tid) ? tid.GetInt32() : 0;
            var messageType = GetString(root, "messageType") ?? "Default";
            var message = GetString(root, "eventMessage") ?? "";
            var subsystem = GetString(root, "subsystem");
            var category = GetString(root, "category");

            // Extract process name from processImagePath
            var processPath = GetString(root, "processImagePath") ?? "";
            var processName = Path.GetFileName(processPath);

            var level = ParseLevel(messageType);

            return new SimulatorLogEntry(
                Timestamp: timestamp,
                ProcessId: processId,
                ThreadId: threadId,
                Level: level,
                ProcessName: processName,
                Subsystem: subsystem,
                Category: category,
                Message: message,
                RawLine: line
            );
        }
        catch
        {
            return null;
        }
    }

    public static SimulatorLogLevel ParseLevel(string messageType) => messageType switch
    {
        "Debug" => SimulatorLogLevel.Debug,
        "Info" => SimulatorLogLevel.Info,
        "Default" => SimulatorLogLevel.Default,
        "Error" => SimulatorLogLevel.Error,
        "Fault" => SimulatorLogLevel.Fault,
        _ => SimulatorLogLevel.Default
    };

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }
}
