using System.Text.Json.Serialization;

namespace MauiSherpa.Core.Models.DevFlow;

/// <summary>
/// Agent session registered with the MauiDevFlow broker.
/// </summary>
public class BrokerAgent
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("project")]
    public string? Project { get; set; }

    [JsonPropertyName("tfm")]
    public string? Tfm { get; set; }

    [JsonPropertyName("platform")]
    public string? Platform { get; set; }

    [JsonPropertyName("appName")]
    public string? AppName { get; set; }

    [JsonPropertyName("port")]
    public int Port { get; set; }

    [JsonPropertyName("connectedAt")]
    public DateTimeOffset? ConnectedAt { get; set; }
}

/// <summary>
/// Agent status response from /api/status.
/// </summary>
public class DevFlowAgentStatus
{
    [JsonPropertyName("agent")]
    public string? Agent { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("platform")]
    public string? Platform { get; set; }

    [JsonPropertyName("deviceType")]
    public string? DeviceType { get; set; }

    [JsonPropertyName("idiom")]
    public string? Idiom { get; set; }

    [JsonPropertyName("appName")]
    public string? AppName { get; set; }

    [JsonPropertyName("running")]
    public bool Running { get; set; }
}

/// <summary>
/// Visual tree element from /api/tree or /api/element.
/// </summary>
public class DevFlowElementInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("parentId")]
    public string? ParentId { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("fullType")]
    public string FullType { get; set; } = string.Empty;

    [JsonPropertyName("automationId")]
    public string? AutomationId { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("isVisible")]
    public bool IsVisible { get; set; }

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; }

    [JsonPropertyName("isFocused")]
    public bool IsFocused { get; set; }

    [JsonPropertyName("opacity")]
    public double Opacity { get; set; }

    [JsonPropertyName("bounds")]
    public DevFlowBoundsInfo? Bounds { get; set; }

    [JsonPropertyName("gestures")]
    public List<string>? Gestures { get; set; }

    [JsonPropertyName("nativeType")]
    public string? NativeType { get; set; }

    [JsonPropertyName("nativeProperties")]
    public Dictionary<string, string?>? NativeProperties { get; set; }

    [JsonPropertyName("children")]
    public List<DevFlowElementInfo>? Children { get; set; }
}

public class DevFlowBoundsInfo
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }
}

/// <summary>
/// Captured HTTP request/response from /api/network.
/// </summary>
public class DevFlowNetworkRequest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("host")]
    public string? Host { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("statusCode")]
    public int? StatusCode { get; set; }

    [JsonPropertyName("statusText")]
    public string? StatusText { get; set; }

    [JsonPropertyName("durationMs")]
    public long DurationMs { get; set; }

    [JsonPropertyName("requestSize")]
    public long? RequestSize { get; set; }

    [JsonPropertyName("responseSize")]
    public long? ResponseSize { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("requestContentType")]
    public string? RequestContentType { get; set; }

    [JsonPropertyName("responseContentType")]
    public string? ResponseContentType { get; set; }

    [JsonPropertyName("requestHeaders")]
    public Dictionary<string, string[]>? RequestHeaders { get; set; }

    [JsonPropertyName("responseHeaders")]
    public Dictionary<string, string[]>? ResponseHeaders { get; set; }

    [JsonPropertyName("requestBody")]
    public string? RequestBody { get; set; }

    [JsonPropertyName("responseBody")]
    public string? ResponseBody { get; set; }

    [JsonPropertyName("requestBodyEncoding")]
    public string? RequestBodyEncoding { get; set; }

    [JsonPropertyName("responseBodyEncoding")]
    public string? ResponseBodyEncoding { get; set; }

    [JsonPropertyName("requestBodyTruncated")]
    public bool RequestBodyTruncated { get; set; }

    [JsonPropertyName("responseBodyTruncated")]
    public bool ResponseBodyTruncated { get; set; }
}

/// <summary>
/// Log entry from /api/logs.
/// </summary>
public class DevFlowLogEntry
{
    [JsonPropertyName("t")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonPropertyName("l")]
    public string? Level { get; set; }

    [JsonPropertyName("s")]
    public string? Source { get; set; }

    [JsonPropertyName("m")]
    public string? Message { get; set; }

    [JsonPropertyName("c")]
    public string? Category { get; set; }

    [JsonPropertyName("e")]
    public string? Exception { get; set; }
}

/// <summary>
/// CDP command request for /api/cdp.
/// </summary>
public class CdpRequest
{
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("params")]
    public Dictionary<string, object?>? Params { get; set; }
}

/// <summary>
/// CDP command response from /api/cdp.
/// </summary>
public class CdpResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("result")]
    public object? Result { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>
/// Represents a Blazor WebView CDP target from /api/cdp/targets.
/// </summary>
public class CdpTarget
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("ready")]
    public bool Ready { get; set; }
}
