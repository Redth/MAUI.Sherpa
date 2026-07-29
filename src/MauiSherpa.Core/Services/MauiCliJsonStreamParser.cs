using System.Text;
using System.Text.Json;
using MauiSherpa.Core.Models.Profiling;

namespace MauiSherpa.Core.Services;

public sealed class MauiCliJsonStreamParser
{
    private readonly StringBuilder _buffer = new();
    private bool _started;
    private bool _inString;
    private bool _escaped;
    private int _depth;

    public IReadOnlyList<MauiCliMessage> Append(string fragment)
    {
        if (string.IsNullOrEmpty(fragment))
            return [];

        var messages = new List<MauiCliMessage>();

        foreach (var character in fragment)
        {
            if (!_started)
            {
                if (character is not ('{' or '['))
                    continue;

                _started = true;
                _depth = 1;
                _buffer.Append(character);
                continue;
            }

            _buffer.Append(character);

            if (_inString)
            {
                if (_escaped)
                {
                    _escaped = false;
                    continue;
                }

                if (character == '\\')
                {
                    _escaped = true;
                    continue;
                }

                if (character == '"')
                    _inString = false;

                continue;
            }

            if (character == '"')
            {
                _inString = true;
                continue;
            }

            if (character is '{' or '[')
                _depth++;
            else if (character is '}' or ']')
                _depth--;

            if (_depth != 0)
                continue;

            var json = _buffer.ToString();
            ResetFrame();

            try
            {
                using var document = JsonDocument.Parse(json);
                messages.Add(ParseMessage(document.RootElement));
            }
            catch (JsonException)
            {
                // Human output can contain braces. Ignore invalid frames and resume scanning.
            }
        }

        return messages;
    }

    private void ResetFrame()
    {
        _buffer.Clear();
        _started = false;
        _inString = false;
        _escaped = false;
        _depth = 0;
    }

    private static MauiCliMessage ParseMessage(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
            return new MauiCliDeviceListMessage(ParseDevices(root));

        if (root.ValueKind != JsonValueKind.Object)
            return new MauiCliUnknownMessage(root.Clone());

        if (TryGetString(root, "code", out var code))
            return ParseError(root, code);

        if (TryGetString(root, "status", out var status) &&
            TryGetString(root, "message", out var statusMessage))
        {
            return new MauiCliStatusMessage(
                status,
                statusMessage,
                TryGetInt32(root, "percentage"));
        }

        if (TryGetString(root, "output_path", out _))
            return new MauiProfileResultMessage(ParseProfileResult(root));

        if (TryGetString(root, "version", out var version))
        {
            return new MauiCliVersionMessage(
                version,
                GetString(root, "runtime"),
                GetString(root, "os"));
        }

        return new MauiCliUnknownMessage(root.Clone());
    }

    private static MauiCliErrorMessage ParseError(JsonElement root, string code)
    {
        MauiCliRemediation? remediation = null;
        if (TryGetProperty(root, "remediation", out var remediationElement) &&
            remediationElement.ValueKind == JsonValueKind.Object)
        {
            remediation = new MauiCliRemediation(
                GetString(remediationElement, "type") ?? "unknown",
                GetString(remediationElement, "command"),
                GetStringArray(remediationElement, "manual_steps"));
        }

        JsonElement? context = null;
        if (TryGetProperty(root, "context", out var contextElement))
            context = contextElement.Clone();

        return new MauiCliErrorMessage(
            code,
            GetString(root, "category") ?? "tool",
            GetString(root, "severity") ?? "error",
            GetString(root, "message") ?? "The MAUI CLI command failed.",
            GetString(root, "native_error"),
            remediation,
            GetString(root, "docs_url"),
            GetString(root, "correlation_id"),
            context);
    }

    private static MauiProfileResult ParseProfileResult(JsonElement root)
    {
        return new MauiProfileResult
        {
            ProjectPath = GetRequiredString(root, "project_path"),
            ProjectName = GetRequiredString(root, "project_name"),
            Framework = GetRequiredString(root, "framework"),
            Platform = GetRequiredString(root, "platform"),
            DeviceId = GetRequiredString(root, "device_id"),
            DeviceName = GetRequiredString(root, "device_name"),
            Configuration = GetRequiredString(root, "configuration"),
            Format = GetRequiredString(root, "format"),
            OutputPath = GetRequiredString(root, "output_path"),
            RawTracePath = GetString(root, "raw_trace_path"),
            DsrouterKind = GetString(root, "dsrouter_kind"),
            DiagnosticAddress = GetString(root, "diagnostic_address"),
            DiagnosticPort = TryGetInt32(root, "diagnostic_port"),
            UsedStoppingEvent = TryGetBoolean(root, "used_stopping_event") ?? false,
            StartedAtUtc = TryGetDateTimeOffset(root, "started_at_utc"),
            CompletedAtUtc = TryGetDateTimeOffset(root, "completed_at_utc")
        };
    }

    private static IReadOnlyList<MauiCliDevice> ParseDevices(JsonElement root)
    {
        var devices = new List<MauiCliDevice>();
        foreach (var item in root.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var name = GetString(item, "name");
            var identifier = GetString(item, "identifier");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(identifier))
                continue;

            devices.Add(new MauiCliDevice
            {
                Name = name,
                Identifier = identifier,
                EmulatorId = GetString(item, "emulator_id"),
                Platforms = GetStringArray(item, "platforms").ToArray(),
                Version = GetString(item, "version"),
                VersionName = GetString(item, "version_name"),
                Model = GetString(item, "model"),
                IsEmulator = TryGetBoolean(item, "is_emulator") ?? false,
                IsRunning = TryGetBoolean(item, "is_running") ?? false,
                ConnectionType = GetString(item, "connection_type")
            });
        }

        return devices;
    }

    private static string GetRequiredString(JsonElement element, string propertyName)
    {
        return GetString(element, propertyName)
            ?? throw new JsonException($"Required MAUI CLI property '{propertyName}' was missing.");
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return TryGetString(element, propertyName, out var value) ? value : null;
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!TryGetProperty(element, propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return true;
    }

    private static int? TryGetInt32(JsonElement element, string propertyName)
    {
        return TryGetProperty(element, propertyName, out var property) &&
               property.TryGetInt32(out var value)
            ? value
            : null;
    }

    private static bool? TryGetBoolean(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var property))
            return null;

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static DateTimeOffset? TryGetDateTimeOffset(JsonElement element, string propertyName)
    {
        return TryGetString(element, propertyName, out var value) &&
               DateTimeOffset.TryParse(value, out var parsed)
            ? parsed
            : null;
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property
            .EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .ToArray();
    }

    private static bool TryGetProperty(
        JsonElement element,
        string propertyName,
        out JsonElement property)
    {
        var normalizedName = Normalize(propertyName);
        foreach (var candidate in element.EnumerateObject())
        {
            if (Normalize(candidate.Name) == normalizedName)
            {
                property = candidate.Value;
                return true;
            }
        }

        property = default;
        return false;
    }

    private static string Normalize(string value)
    {
        return string.Concat(value.Where(char.IsLetterOrDigit)).ToUpperInvariant();
    }
}
