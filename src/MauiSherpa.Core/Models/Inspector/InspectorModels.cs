using System.Text.Json.Serialization;

namespace MauiSherpa.Core.Models.Inspector;

// ─────────────────────────── Agent & Capabilities ───────────────────────────

/// <summary>
/// Information about the connected DevFlow agent.
/// </summary>
public record InspectorAgentInfo
{
    /// <summary>Agent implementation name (e.g. "devflow-maui").</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Semantic version of the agent.</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>UI framework the agent instruments (e.g. "maui", "flutter", "react-native").</summary>
    public string Framework { get; init; } = string.Empty;

    /// <summary>Version of the UI framework.</summary>
    public string FrameworkVersion { get; init; } = string.Empty;
}

/// <summary>
/// Agent status including platform, device, and application context.
/// </summary>
public record InspectorAgentStatus
{
    public InspectorAgentInfo Agent { get; init; } = new();
    public string Platform { get; init; } = string.Empty;
    public InspectorDeviceStatus Device { get; init; } = new();
    public InspectorAppStatus App { get; init; } = new();
    public bool Running { get; init; }
    public string? Uptime { get; init; }
}

public record InspectorDeviceStatus
{
    public string Model { get; init; } = string.Empty;
    public string Manufacturer { get; init; } = string.Empty;
    public string OsVersion { get; init; } = string.Empty;
    public string Idiom { get; init; } = string.Empty;
}

public record InspectorAppStatus
{
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string PackageId { get; init; } = string.Empty;
}

/// <summary>
/// Capability discovery: what features the connected agent supports.
/// </summary>
public record InspectorCapabilities
{
    public InspectorAgentInfo Agent { get; init; } = new();

    /// <summary>
    /// Map of capability namespaces to their details.
    /// Keys are dot-separated (e.g. "ui.tree", "ui.actions", "profiler").
    /// </summary>
    public IReadOnlyDictionary<string, CapabilityDetail> Capabilities { get; init; }
        = new Dictionary<string, CapabilityDetail>();

    /// <summary>
    /// Map of extension namespaces (reverse-domain) to their route registrations.
    /// </summary>
    public IReadOnlyDictionary<string, ExtensionDetail>? Extensions { get; init; }

    /// <summary>Check whether a capability namespace is supported.</summary>
    public bool HasCapability(string ns) => Capabilities.ContainsKey(ns);

    /// <summary>Check whether a specific feature is supported within a namespace.</summary>
    public bool HasFeature(string ns, string feature) =>
        Capabilities.TryGetValue(ns, out var cap) && cap.Features.Contains(feature);
}

public record CapabilityDetail
{
    public int Version { get; init; } = 1;
    public IReadOnlyList<string> Features { get; init; } = [];
}

public record ExtensionDetail
{
    public IReadOnlyList<ExtensionRoute> Routes { get; init; } = [];
    public string? Description { get; init; }
}

public record ExtensionRoute
{
    public string Method { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
}

// ─────────────────────────── Visual Tree Elements ───────────────────────────

/// <summary>
/// Framework-agnostic visual tree element — the common denominator for MAUI,
/// Flutter, React Native, and any future UI stack.
/// </summary>
public record InspectorElement
{
    /// <summary>Globally unique element identifier.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Parent element ID, or null for root elements.</summary>
    public string? ParentId { get; init; }

    /// <summary>Short type name (e.g. "Button").</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Fully qualified type name (e.g. "Microsoft.Maui.Controls.Button").</summary>
    public string FullType { get; init; } = string.Empty;

    /// <summary>UI framework that owns this element ("maui", "flutter", etc.). Null for legacy agents.</summary>
    public string? Framework { get; init; }

    /// <summary>Automation/test identifier set by the developer.</summary>
    public string? AutomationId { get; init; }

    /// <summary>Visible text content.</summary>
    public string? Text { get; init; }

    /// <summary>Current value for input elements (text field content, slider value).</summary>
    public string? Value { get; init; }

    /// <summary>Semantic role (button, textbox, checkbox, image, etc.).</summary>
    public string? Role { get; init; }

    /// <summary>Semantic traits (interactive, focusable, scrollable, etc.).</summary>
    public IReadOnlyList<string>? Traits { get; init; }

    /// <summary>Current interactive and visual state.</summary>
    public ElementState State { get; init; } = new();

    /// <summary>Bounding rectangle.</summary>
    public BoundsInfo? Bounds { get; init; }

    /// <summary>Gesture types recognized by this element.</summary>
    public IReadOnlyList<string>? Gestures { get; init; }

    /// <summary>Style information (CSS classes, etc.).</summary>
    public StyleInfo? Style { get; init; }

    /// <summary>Underlying native platform view info.</summary>
    public NativeViewInfo? NativeView { get; init; }

    /// <summary>
    /// Framework-specific key-value pairs not captured by standard fields.
    /// For MAUI: maui:bindingContext, maui:keyboard, etc.
    /// </summary>
    public IReadOnlyDictionary<string, object>? FrameworkProperties { get; init; }

    /// <summary>Child elements forming the subtree.</summary>
    public IReadOnlyList<InspectorElement>? Children { get; init; }
}

/// <summary>Current interactive/visual state of an element.</summary>
public record ElementState
{
    public bool Displayed { get; init; } = true;
    public bool Enabled { get; init; } = true;
    public bool Selected { get; init; }
    public bool Focused { get; init; }
    public double Opacity { get; init; } = 1.0;
}

/// <summary>Bounding rectangle of an element.</summary>
public record BoundsInfo
{
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    /// <summary>Coordinate system: "window" or "screen". Null for legacy agents.</summary>
    public string? CoordinateSystem { get; init; }
}

/// <summary>Style information.</summary>
public record StyleInfo
{
    public IReadOnlyList<string>? Classes { get; init; }
}

/// <summary>Information about the underlying native platform view.</summary>
public record NativeViewInfo
{
    /// <summary>Native type name (e.g. "UIButton", "android.widget.Button").</summary>
    public string? Type { get; init; }
    /// <summary>Key-value pairs of native view properties.</summary>
    public IReadOnlyDictionary<string, object>? Properties { get; init; }
}

// ─────────────────────────── Tree Options ────────────────────────────────

/// <summary>Options for visual tree retrieval.</summary>
public record TreeOptions
{
    /// <summary>Maximum depth of tree to return. Null = unlimited.</summary>
    public int? Depth { get; init; }

    /// <summary>Which tree layer: "framework", "native", or "render".</summary>
    public string? Layer { get; init; }

    /// <summary>Element ID to scope tree to a subtree.</summary>
    public string? RootId { get; init; }

    /// <summary>Additional data to include (e.g. "properties").</summary>
    public IReadOnlyList<string>? Include { get; init; }

    /// <summary>Window identifier for multi-window scenarios.</summary>
    public string? Window { get; init; }
}

/// <summary>Parameters for querying elements.</summary>
public record ElementQuery
{
    /// <summary>Locator strategy: accessibility-id, css-selector, type, text, xpath.</summary>
    public string Strategy { get; init; } = "type";

    /// <summary>Locator value.</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>Maximum number of results.</summary>
    public int? Limit { get; init; }

    /// <summary>Additional data to include (e.g. "properties", "bounds").</summary>
    public IReadOnlyList<string>? Include { get; init; }
}

// ─────────────────────────── Actions ─────────────────────────────────────

/// <summary>Result of a UI action.</summary>
public record ActionResult
{
    public bool Success { get; init; }
    public InspectorError? Error { get; init; }
    /// <summary>Base64-encoded screenshot, if requested via Include.</summary>
    public string? Screenshot { get; init; }
    /// <summary>Visual tree snapshot, if requested via Include.</summary>
    public IReadOnlyList<InspectorElement>? Tree { get; init; }
}

/// <summary>Structured error (RFC 7807 ProblemDetails).</summary>
public record InspectorError
{
    public string Type { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public int Status { get; init; }
    public string? Detail { get; init; }
    public string? ErrorCode { get; init; }
}

/// <summary>What to include in action responses.</summary>
public enum ActionInclude
{
    Screenshot,
    Tree
}

public record TapRequest
{
    public string? ElementId { get; init; }
    public double? X { get; init; }
    public double? Y { get; init; }
    public IReadOnlyList<ActionInclude>? Include { get; init; }
}

public record FillRequest
{
    public string ElementId { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public IReadOnlyList<ActionInclude>? Include { get; init; }
}

public record ClearRequest
{
    public string ElementId { get; init; } = string.Empty;
    public IReadOnlyList<ActionInclude>? Include { get; init; }
}

public record FocusRequest
{
    public string ElementId { get; init; } = string.Empty;
    public IReadOnlyList<ActionInclude>? Include { get; init; }
}

public record ScrollRequest
{
    public string? ElementId { get; init; }
    public double? DeltaX { get; init; }
    public double? DeltaY { get; init; }
    public bool Animated { get; init; } = true;
    public int? ItemIndex { get; init; }
    public int? GroupIndex { get; init; }
    public string? ScrollToPosition { get; init; }
    public IReadOnlyList<ActionInclude>? Include { get; init; }
}

public record NavigateRequest
{
    public string Route { get; init; } = string.Empty;
    public IReadOnlyList<ActionInclude>? Include { get; init; }
}

public record ResizeRequest
{
    public string? ElementId { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public IReadOnlyList<ActionInclude>? Include { get; init; }
}

public record BackRequest
{
    public IReadOnlyList<ActionInclude>? Include { get; init; }
}

public record KeyRequest
{
    public string Key { get; init; } = string.Empty;
    public IReadOnlyList<ActionInclude>? Include { get; init; }
}

public record GestureAction
{
    public string Type { get; init; } = string.Empty;
    public double? X { get; init; }
    public double? Y { get; init; }
    public int? Duration { get; init; }
    public int? Button { get; init; }
}

public record GestureRequest
{
    public IReadOnlyList<GestureAction> Actions { get; init; } = [];
    public IReadOnlyList<ActionInclude>? Include { get; init; }
}

public record BatchActionItem
{
    public string Action { get; init; } = string.Empty;
    public string? ElementId { get; init; }
    public double? X { get; init; }
    public double? Y { get; init; }
    public string? Text { get; init; }
    public double? DeltaX { get; init; }
    public double? DeltaY { get; init; }
    public bool? Animated { get; init; }
    public int? ItemIndex { get; init; }
    public string? Route { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public string? Key { get; init; }
    public IReadOnlyList<GestureAction>? Actions { get; init; }
}

public record BatchRequest
{
    public IReadOnlyList<BatchActionItem> Actions { get; init; } = [];
    public IReadOnlyList<ActionInclude>? Include { get; init; }
    public bool ContinueOnError { get; init; }
}

// ─────────────────────────── Screenshots ─────────────────────────────────

public record ScreenshotOptions
{
    public string? ElementId { get; init; }
    public int? MaxWidth { get; init; }
    public string? Scale { get; init; }
    public string? Format { get; init; }
    public string? Window { get; init; }
}

// ─────────────────────────── WebView ─────────────────────────────────────

public record InspectorWebViewContext
{
    public string Id { get; init; } = string.Empty;
    public string? ElementId { get; init; }
    public string Url { get; init; } = string.Empty;
    public string? Title { get; init; }
    public bool Ready { get; init; }
}

public record WebViewEvalResult
{
    public object? Result { get; init; }
    public WebViewExceptionDetails? ExceptionDetails { get; init; }
}

public record WebViewExceptionDetails
{
    public string Text { get; init; } = string.Empty;
    public int? LineNumber { get; init; }
    public int? ColumnNumber { get; init; }
}

// ─────────────────────────── Network ─────────────────────────────────────

public record InspectorNetworkRequest
{
    public string Id { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; }
    public string Method { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string? Host { get; init; }
    public string? Path { get; init; }
    public int? StatusCode { get; init; }
    public string? StatusText { get; init; }
    public double? DurationMs { get; init; }
    public string? Error { get; init; }
    public string? RequestContentType { get; init; }
    public string? ResponseContentType { get; init; }
    public long? RequestSize { get; init; }
    public long? ResponseSize { get; init; }
}

public record InspectorNetworkRequestDetail : InspectorNetworkRequest
{
    public IReadOnlyDictionary<string, string>? RequestHeaders { get; init; }
    public IReadOnlyDictionary<string, string>? ResponseHeaders { get; init; }
    public string? RequestBody { get; init; }
    public string? ResponseBody { get; init; }
    public string? RequestBodyEncoding { get; init; }
    public string? ResponseBodyEncoding { get; init; }
    public bool? RequestBodyTruncated { get; init; }
    public bool? ResponseBodyTruncated { get; init; }
}

// ─────────────────────────── Logs ────────────────────────────────────────

public record InspectorLogEntry
{
    public DateTimeOffset Timestamp { get; init; }
    public string Level { get; init; } = "info";
    public string Category { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? Exception { get; init; }
    public string Source { get; init; } = "framework";
}

public record LogQuery
{
    public int? Limit { get; init; }
    public int? Skip { get; init; }
    public string? Source { get; init; }
    public string? Level { get; init; }
}

public record LogStreamOptions
{
    public string? Source { get; init; }
    public string? Level { get; init; }
    public int? Replay { get; init; }
}

// ─────────────────────────── Profiler ────────────────────────────────────

public record InspectorProfilerCapabilities
{
    public string Platform { get; init; } = string.Empty;
    public bool ManagedMemorySupported { get; init; }
    public bool NativeMemorySupported { get; init; }
    public bool GcSupported { get; init; }
    public bool CpuPercentSupported { get; init; }
    public bool FpsSupported { get; init; }
    public bool FrameTimingsEstimated { get; init; }
    public bool NativeFrameTimingsSupported { get; init; }
    public bool JankEventsSupported { get; init; }
    public bool UiThreadStallSupported { get; init; }
    public bool ThreadCountSupported { get; init; }
}

public record InspectorProfilerSession
{
    public string SessionId { get; init; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; init; }
    public int SampleIntervalMs { get; init; }
    public bool IsActive { get; init; }
}

public record InspectorProfilerSample
{
    public DateTimeOffset TsUtc { get; init; }
    public double? Fps { get; init; }
    public double? FrameTimeMsP50 { get; init; }
    public double? FrameTimeMsP95 { get; init; }
    public double? WorstFrameTimeMs { get; init; }
    public long? ManagedBytes { get; init; }
    public int? Gc0 { get; init; }
    public int? Gc1 { get; init; }
    public int? Gc2 { get; init; }
    public long? NativeMemoryBytes { get; init; }
    public string? NativeMemoryKind { get; init; }
    public double? CpuPercent { get; init; }
    public int? ThreadCount { get; init; }
    public int? JankFrameCount { get; init; }
    public int? UiThreadStallCount { get; init; }
    public string? FrameSource { get; init; }
    public string? FrameQuality { get; init; }
}

public record InspectorProfilerMarker
{
    public DateTimeOffset TsUtc { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? PayloadJson { get; init; }
}

public record InspectorProfilerSpan
{
    public string SpanId { get; init; } = string.Empty;
    public string? ParentSpanId { get; init; }
    public string? TraceId { get; init; }
    public DateTimeOffset StartTsUtc { get; init; }
    public DateTimeOffset? EndTsUtc { get; init; }
    public double DurationMs { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Status { get; init; }
    public string? ThreadId { get; init; }
    public string? Screen { get; init; }
    public string? ElementPath { get; init; }
    public string? TagsJson { get; init; }
    public string? Error { get; init; }
}

public record InspectorProfilerBatch
{
    public string SessionId { get; init; } = string.Empty;
    public IReadOnlyList<InspectorProfilerSample> Samples { get; init; } = [];
    public IReadOnlyList<InspectorProfilerMarker> Markers { get; init; } = [];
    public IReadOnlyList<InspectorProfilerSpan> Spans { get; init; } = [];
    public int SampleCursor { get; init; }
    public int MarkerCursor { get; init; }
    public int SpanCursor { get; init; }
    public bool IsActive { get; init; }
}

public record InspectorProfilerHotspot
{
    public string Kind { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Screen { get; init; }
    public int Count { get; init; }
    public int? ErrorCount { get; init; }
    public double AvgDurationMs { get; init; }
    public double? P95DurationMs { get; init; }
    public double? MaxDurationMs { get; init; }
}

// ─────────────────────────── Device ──────────────────────────────────────

public record InspectorDeviceInfo
{
    public string Model { get; init; } = string.Empty;
    public string Manufacturer { get; init; } = string.Empty;
    public string OsVersion { get; init; } = string.Empty;
    public string Platform { get; init; } = string.Empty;
    public string Idiom { get; init; } = string.Empty;
    public string? Architecture { get; init; }
}

public record InspectorDisplayInfo
{
    public double Width { get; init; }
    public double Height { get; init; }
    public double Density { get; init; }
    public string Orientation { get; init; } = "portrait";
    public double? RefreshRate { get; init; }
}

public record InspectorBatteryInfo
{
    public double Level { get; init; }
    public string State { get; init; } = "unknown";
    public string PowerSource { get; init; } = "unknown";
}

public record InspectorConnectivityInfo
{
    public string NetworkAccess { get; init; } = "none";
    public IReadOnlyList<string> ConnectionProfiles { get; init; } = [];
}

public record InspectorAppInfo
{
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string BuildNumber { get; init; } = string.Empty;
    public string PackageId { get; init; } = string.Empty;
    public string? Theme { get; init; }
}

// ─────────────────────────── Permissions & Geolocation ───────────────────

public record InspectorPermissionStatus
{
    public string Name { get; init; } = string.Empty;
    public string Status { get; init; } = "unknown";
}

public record InspectorGeolocation
{
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public double? Altitude { get; init; }
    public double Accuracy { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}

// ─────────────────────────── Sensors ─────────────────────────────────────

public record InspectorSensorInfo
{
    public string Name { get; init; } = string.Empty;
    public bool Available { get; init; }
    public bool Active { get; init; }
}

public record InspectorSensorReading
{
    public string Sensor { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; }
    public IReadOnlyDictionary<string, object> Values { get; init; } = new Dictionary<string, object>();
}

// ─────────────────────────── Storage ─────────────────────────────────────

public record InspectorPreferenceEntry
{
    public string Key { get; init; } = string.Empty;
    public object? Value { get; init; }
    public string Type { get; init; } = "string";
}

public record InspectorSecureStorageEntry
{
    public string Key { get; init; } = string.Empty;
    public string? Value { get; init; }
    public bool Exists { get; init; }
}

// ─────────────────────────── Protocol Version ────────────────────────────

/// <summary>Identifies which protocol version an agent speaks.</summary>
public enum InspectorProtocolVersion
{
    /// <summary>Legacy pre-v1 API (/api/*).</summary>
    Legacy,
    /// <summary>v1 spec (/api/v1/*).</summary>
    V1
}
