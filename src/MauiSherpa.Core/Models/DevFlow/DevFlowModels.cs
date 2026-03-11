using System.Text.Json;
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

    [JsonPropertyName("version")]
    public string? Version { get; set; }
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

    [JsonPropertyName("cdpReady")]
    public bool CdpReady { get; set; }

    [JsonPropertyName("cdpWebViewCount")]
    public int CdpWebViewCount { get; set; }
}

/// <summary>
/// Profiler capabilities from /api/profiler/capabilities.
/// </summary>
public class DevFlowProfilerCapabilities
{
    [JsonPropertyName("available")]
    public bool Available { get; set; }

    [JsonPropertyName("supportedInBuild")]
    public bool SupportedInBuild { get; set; }

    [JsonPropertyName("featureEnabled")]
    public bool FeatureEnabled { get; set; }

    [JsonPropertyName("platform")]
    public string? Platform { get; set; }

    [JsonPropertyName("managedMemorySupported")]
    public bool ManagedMemorySupported { get; set; }

    [JsonPropertyName("nativeMemorySupported")]
    public bool NativeMemorySupported { get; set; }

    [JsonPropertyName("gcSupported")]
    public bool GcSupported { get; set; }

    [JsonPropertyName("cpuPercentSupported")]
    public bool CpuPercentSupported { get; set; }

    [JsonPropertyName("fpsSupported")]
    public bool FpsSupported { get; set; }

    [JsonPropertyName("frameTimingsEstimated")]
    public bool FrameTimingsEstimated { get; set; }

    [JsonPropertyName("nativeFrameTimingsSupported")]
    public bool NativeFrameTimingsSupported { get; set; }

    [JsonPropertyName("jankEventsSupported")]
    public bool JankEventsSupported { get; set; }

    [JsonPropertyName("uiThreadStallSupported")]
    public bool UiThreadStallSupported { get; set; }

    [JsonPropertyName("threadCountSupported")]
    public bool ThreadCountSupported { get; set; }
}

/// <summary>
/// Profiler session metadata.
/// </summary>
public class DevFlowProfilerSessionInfo
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("startedAtUtc")]
    public DateTimeOffset StartedAtUtc { get; set; }

    [JsonPropertyName("sampleIntervalMs")]
    public int SampleIntervalMs { get; set; }

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }
}

/// <summary>
/// Single profiler sample.
/// </summary>
public class DevFlowProfilerSample
{
    [JsonPropertyName("tsUtc")]
    public DateTimeOffset TsUtc { get; set; }

    [JsonPropertyName("fps")]
    public double? Fps { get; set; }

    [JsonPropertyName("frameTimeMsP50")]
    public double? FrameTimeMsP50 { get; set; }

    [JsonPropertyName("frameTimeMsP95")]
    public double? FrameTimeMsP95 { get; set; }

    [JsonPropertyName("worstFrameTimeMs")]
    public double? WorstFrameTimeMs { get; set; }

    [JsonPropertyName("managedBytes")]
    public long ManagedBytes { get; set; }

    [JsonPropertyName("nativeMemoryBytes")]
    public long? NativeMemoryBytes { get; set; }

    [JsonPropertyName("nativeMemoryKind")]
    public string? NativeMemoryKind { get; set; }

    [JsonPropertyName("gc0")]
    public int Gc0 { get; set; }

    [JsonPropertyName("gc1")]
    public int Gc1 { get; set; }

    [JsonPropertyName("gc2")]
    public int Gc2 { get; set; }

    [JsonPropertyName("cpuPercent")]
    public double? CpuPercent { get; set; }

    [JsonPropertyName("threadCount")]
    public int? ThreadCount { get; set; }

    [JsonPropertyName("jankFrameCount")]
    public int JankFrameCount { get; set; }

    [JsonPropertyName("uiThreadStallCount")]
    public int UiThreadStallCount { get; set; }

    [JsonPropertyName("frameSource")]
    public string? FrameSource { get; set; }

    [JsonPropertyName("frameQuality")]
    public string? FrameQuality { get; set; }
}

/// <summary>
/// Profiler correlation marker.
/// </summary>
public class DevFlowProfilerMarker
{
    [JsonPropertyName("tsUtc")]
    public DateTimeOffset TsUtc { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("payloadJson")]
    public string? PayloadJson { get; set; }
}

/// <summary>
/// Profiler samples and markers batch from /api/profiler/samples.
/// </summary>
public class DevFlowProfilerBatch
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("samples")]
    public List<DevFlowProfilerSample> Samples { get; set; } = new();

    [JsonPropertyName("markers")]
    public List<DevFlowProfilerMarker> Markers { get; set; } = new();

    [JsonPropertyName("spans")]
    public List<DevFlowProfilerSpan> Spans { get; set; } = new();

    [JsonPropertyName("sampleCursor")]
    public long SampleCursor { get; set; }

    [JsonPropertyName("markerCursor")]
    public long MarkerCursor { get; set; }

    [JsonPropertyName("spanCursor")]
    public long SpanCursor { get; set; }

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }
}

/// <summary>
/// Timed profiler operation span.
/// </summary>
public class DevFlowProfilerSpan
{
    [JsonPropertyName("spanId")]
    public string SpanId { get; set; } = string.Empty;

    [JsonPropertyName("parentSpanId")]
    public string? ParentSpanId { get; set; }

    [JsonPropertyName("traceId")]
    public string? TraceId { get; set; }

    [JsonPropertyName("startTsUtc")]
    public DateTimeOffset StartTsUtc { get; set; }

    [JsonPropertyName("endTsUtc")]
    public DateTimeOffset EndTsUtc { get; set; }

    [JsonPropertyName("durationMs")]
    public double DurationMs { get; set; }

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("threadId")]
    public int? ThreadId { get; set; }

    [JsonPropertyName("screen")]
    public string? Screen { get; set; }

    [JsonPropertyName("elementPath")]
    public string? ElementPath { get; set; }

    [JsonPropertyName("tagsJson")]
    public string? TagsJson { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>
/// Aggregated span hotspot.
/// </summary>
public class DevFlowProfilerHotspot
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("screen")]
    public string? Screen { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("errorCount")]
    public int ErrorCount { get; set; }

    [JsonPropertyName("avgDurationMs")]
    public double AvgDurationMs { get; set; }

    [JsonPropertyName("p95DurationMs")]
    public double P95DurationMs { get; set; }

    [JsonPropertyName("maxDurationMs")]
    public double MaxDurationMs { get; set; }
}

/// <summary>
/// Response payload from /api/profiler/start.
/// </summary>
public class DevFlowProfilerStartResponse
{
    [JsonPropertyName("session")]
    public DevFlowProfilerSessionInfo? Session { get; set; }

    [JsonPropertyName("capabilities")]
    public DevFlowProfilerCapabilities? Capabilities { get; set; }
}

/// <summary>
/// Response payload from /api/profiler/stop.
/// </summary>
public class DevFlowProfilerStopResponse
{
    [JsonPropertyName("session")]
    public DevFlowProfilerSessionInfo? Session { get; set; }

    [JsonPropertyName("stoppedAtUtc")]
    public DateTimeOffset StoppedAtUtc { get; set; }
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

    [JsonPropertyName("windowBounds")]
    public DevFlowBoundsInfo? WindowBounds { get; set; }

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
/// Hit test result from /api/hittest.
/// </summary>
public class DevFlowHitTestResult
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("window")]
    public int Window { get; set; }

    [JsonPropertyName("elements")]
    public List<DevFlowHitTestElement> Elements { get; set; } = new();
}

public class DevFlowHitTestElement
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("automationId")]
    public string? AutomationId { get; set; }

    [JsonPropertyName("bounds")]
    public DevFlowBoundsInfo? Bounds { get; set; }

    [JsonPropertyName("windowBounds")]
    public DevFlowBoundsInfo? WindowBounds { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }
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
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("result")]
    public object? Result { get; set; }

    [JsonPropertyName("error")]
    public object? Error { get; set; }

    [JsonIgnore]
    public bool Success => Error == null && Result != null;

    public string? GetErrorMessage()
    {
        if (Error is JsonElement el)
        {
            if (el.TryGetProperty("message", out var msg))
                return msg.GetString();
            return el.ToString();
        }
        return Error?.ToString();
    }
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

// ── Platform Info DTOs ──

public class DevFlowAppInfo
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("packageName")] public string? PackageName { get; set; }
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("buildNumber")] public string? BuildNumber { get; set; }
    [JsonPropertyName("requestedTheme")] public string? RequestedTheme { get; set; }
    [JsonPropertyName("requestedLayoutDirection")] public string? RequestedLayoutDirection { get; set; }
}

public class DevFlowDeviceInfo
{
    [JsonPropertyName("manufacturer")] public string? Manufacturer { get; set; }
    [JsonPropertyName("model")] public string? Model { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("platform")] public string? Platform { get; set; }
    [JsonPropertyName("idiom")] public string? Idiom { get; set; }
    [JsonPropertyName("deviceType")] public string? DeviceType { get; set; }
    [JsonPropertyName("osVersion")] public string? OsVersion { get; set; }
}

public class DevFlowDisplayInfo
{
    [JsonPropertyName("width")] public double Width { get; set; }
    [JsonPropertyName("height")] public double Height { get; set; }
    [JsonPropertyName("density")] public double Density { get; set; }
    [JsonPropertyName("orientation")] public string? Orientation { get; set; }
    [JsonPropertyName("rotation")] public string? Rotation { get; set; }
    [JsonPropertyName("refreshRate")] public double RefreshRate { get; set; }
}

public class DevFlowBatteryInfo
{
    [JsonPropertyName("chargeLevel")] public double ChargeLevel { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("powerSource")] public string? PowerSource { get; set; }
    [JsonPropertyName("energySaverStatus")] public string? EnergySaverStatus { get; set; }
}

public class DevFlowConnectivityInfo
{
    [JsonPropertyName("networkAccess")] public string? NetworkAccess { get; set; }
    [JsonPropertyName("connectionProfiles")] public List<string>? ConnectionProfiles { get; set; }
}

public class DevFlowVersionTracking
{
    [JsonPropertyName("currentVersion")] public string? CurrentVersion { get; set; }
    [JsonPropertyName("currentBuild")] public string? CurrentBuild { get; set; }
    [JsonPropertyName("previousVersion")] public string? PreviousVersion { get; set; }
    [JsonPropertyName("previousBuild")] public string? PreviousBuild { get; set; }
    [JsonPropertyName("firstInstalledVersion")] public string? FirstInstalledVersion { get; set; }
    [JsonPropertyName("firstInstalledBuild")] public string? FirstInstalledBuild { get; set; }
    [JsonPropertyName("isFirstLaunchEver")] public bool IsFirstLaunchEver { get; set; }
    [JsonPropertyName("isFirstLaunchForCurrentVersion")] public bool IsFirstLaunchForCurrentVersion { get; set; }
    [JsonPropertyName("isFirstLaunchForCurrentBuild")] public bool IsFirstLaunchForCurrentBuild { get; set; }
    [JsonPropertyName("versionHistory")] public List<string>? VersionHistory { get; set; }
    [JsonPropertyName("buildHistory")] public List<string>? BuildHistory { get; set; }
}

public class DevFlowPermissionStatus
{
    [JsonPropertyName("permission")] public string Permission { get; set; } = string.Empty;
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
}

public class DevFlowPermissionsResponse
{
    [JsonPropertyName("permissions")] public List<DevFlowPermissionStatus>? Permissions { get; set; }
}

public class DevFlowGeolocation
{
    [JsonPropertyName("latitude")] public double Latitude { get; set; }
    [JsonPropertyName("longitude")] public double Longitude { get; set; }
    [JsonPropertyName("altitude")] public double? Altitude { get; set; }
    [JsonPropertyName("accuracy")] public double? Accuracy { get; set; }
    [JsonPropertyName("speed")] public double? Speed { get; set; }
    [JsonPropertyName("course")] public double? Course { get; set; }
    [JsonPropertyName("timestamp")] public DateTimeOffset Timestamp { get; set; }
    [JsonPropertyName("isFromMockProvider")] public bool IsFromMockProvider { get; set; }
}

// ── Sensor DTOs ──

public class DevFlowSensorStatus
{
    [JsonPropertyName("sensor")] public string Sensor { get; set; } = string.Empty;
    [JsonPropertyName("active")] public bool Active { get; set; }
    [JsonPropertyName("supported")] public bool Supported { get; set; }
    [JsonPropertyName("subscribers")] public int Subscribers { get; set; }
}

public class DevFlowSensorReading
{
    [JsonPropertyName("sensor")] public string Sensor { get; set; } = string.Empty;
    [JsonPropertyName("timestamp")] public string? Timestamp { get; set; }
    [JsonPropertyName("data")] public JsonElement Data { get; set; }
}

// ── Storage DTOs ──

public class DevFlowPreferenceEntry
{
    [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
    [JsonPropertyName("value")] public string? Value { get; set; }
    [JsonPropertyName("sharedName")] public string? SharedName { get; set; }
}

public class DevFlowPreferencesListResponse
{
    [JsonPropertyName("keys")] public List<DevFlowPreferenceEntry>? Keys { get; set; }
}

public class DevFlowPreferenceSetRequest
{
    [JsonPropertyName("value")] public object? Value { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("sharedName")] public string? SharedName { get; set; }
}

public class DevFlowSecureStorageEntry
{
    [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
    [JsonPropertyName("value")] public string? Value { get; set; }
    [JsonPropertyName("exists")] public bool Exists { get; set; }
}
