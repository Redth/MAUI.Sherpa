using System.Text.Json;

namespace MauiSherpa.Components;

/// <summary>
/// One column of a tool result table. <paramref name="Kind"/> is "text", "mono", "date" or "pill";
/// pill columns look their value (lower-cased; booleans as "true"/"false") up in <paramref name="Pills"/>.
/// </summary>
public sealed record CopilotResultColumn(
    string Key,
    string Header,
    string Kind = "text",
    IReadOnlyDictionary<string, CopilotResultPill>? Pills = null);

/// <summary>
/// A status badge: its label and a tone of success, warning, error, info or neutral.
/// </summary>
public sealed record CopilotResultPill(string Label, string Tone);

/// <summary>
/// A per-row button that asks Copilot to do something with that row.
/// </summary>
public sealed record CopilotResultAction(
    string Label,
    string Icon,
    Func<JsonElement, bool> AppliesTo,
    Func<JsonElement, string> Prompt);

/// <summary>
/// How a tool's JSON array result is drawn in the chat.
/// </summary>
public sealed record CopilotToolResultSpec(
    string Title,
    string Icon,
    IReadOnlyList<CopilotResultColumn> Columns,
    CopilotResultAction? Action = null);

/// <summary>
/// The Sherpa tools whose results are drawn as tables instead of raw JSON, keyed by tool name.
/// </summary>
public static class CopilotToolResultSpecs
{
    static readonly Dictionary<string, CopilotResultPill> ExpiredPills = new()
    {
        ["true"] = new("Expired", "error"),
        ["false"] = new("Valid", "success")
    };

    public static IReadOnlyDictionary<string, CopilotToolResultSpec> All { get; } = new Dictionary<string, CopilotToolResultSpec>
    {
        ["list_certificates"] = new("Certificates", "fa-solid fa-certificate",
        [
            new("Name", "Name"),
            new("Type", "Type", "mono"),
            new("Platform", "Platform"),
            new("ExpirationDate", "Expires", "date"),
            new("IsExpired", "Status", "pill", ExpiredPills)
        ]),

        ["list_provisioning_profiles"] = new("Provisioning Profiles", "fa-solid fa-file-signature",
        [
            new("Name", "Name"),
            new("Type", "Type", "mono"),
            new("BundleId", "Bundle ID", "mono"),
            new("ExpirationDate", "Expires", "date"),
            new("State", "State", "pill", new Dictionary<string, CopilotResultPill>
            {
                ["active"] = new("Active", "success"),
                ["invalid"] = new("Invalid", "error")
            }),
            new("IsExpired", "Status", "pill", ExpiredPills)
        ]),

        ["list_devices"] = new("Apple Devices", "fa-solid fa-mobile-screen",
        [
            new("Name", "Name"),
            new("Model", "Model"),
            new("Platform", "Platform"),
            new("DeviceClass", "Class"),
            new("Udid", "UDID", "mono"),
            new("Status", "Status", "pill", new Dictionary<string, CopilotResultPill>
            {
                ["enabled"] = new("Enabled", "success"),
                ["disabled"] = new("Disabled", "neutral")
            })
        ]),

        ["list_bundle_ids"] = new("Bundle IDs", "fa-solid fa-box",
        [
            new("Identifier", "Identifier", "mono"),
            new("Name", "Name"),
            new("Platform", "Platform")
        ]),

        ["list_emulators"] = new("Android Emulators", "fa-brands fa-android",
        [
            new("Name", "Name"),
            new("Device", "Device"),
            new("Target", "Target"),
            new("IsRunning", "Status", "pill", new Dictionary<string, CopilotResultPill>
            {
                ["true"] = new("Running", "success"),
                ["false"] = new("Stopped", "neutral")
            })
        ],
        new CopilotResultAction("Start", "fa-solid fa-play",
            row => !IsTrue(row, "IsRunning"),
            row => $"Start the Android emulator \"{Text(row, "Name")}\".")),

        ["list_android_devices"] = new("Android Devices", "fa-brands fa-android",
        [
            new("Serial", "Serial", "mono"),
            new("Model", "Model"),
            new("Type", "Type"),
            new("State", "State", "pill", new Dictionary<string, CopilotResultPill>
            {
                ["device"] = new("Online", "success"),
                ["offline"] = new("Offline", "neutral"),
                ["unauthorized"] = new("Unauthorized", "warning")
            })
        ]),

        ["list_android_packages"] = new("Android SDK Packages", "fa-solid fa-cubes",
        [
            new("Path", "Package", "mono"),
            new("Description", "Description"),
            new("Version", "Version", "mono"),
            new("IsInstalled", "Status", "pill", new Dictionary<string, CopilotResultPill>
            {
                ["true"] = new("Installed", "success"),
                ["false"] = new("Available", "info")
            })
        ],
        new CopilotResultAction("Install", "fa-solid fa-download",
            row => !IsTrue(row, "IsInstalled"),
            row => $"Install the Android SDK package \"{Text(row, "Path")}\"."))
    };

    /// <summary>
    /// A row member as display text ("" when it's missing or null).
    /// </summary>
    public static string Text(JsonElement row, string key) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(key, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? "",
                JsonValueKind.Null or JsonValueKind.Undefined => "",
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => value.GetRawText()
            }
            : "";

    static bool IsTrue(JsonElement row, string key) => Text(row, key) == "true";
}
