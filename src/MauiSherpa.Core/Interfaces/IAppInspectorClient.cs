using MauiSherpa.Core.Models.Inspector;

namespace MauiSherpa.Core.Interfaces;

/// <summary>
/// Framework-agnostic interface for inspecting and interacting with running
/// applications via the DevFlow protocol. UI code programs against this
/// interface — never a concrete client.
/// </summary>
public interface IAppInspectorClient : IDisposable
{
    // ─────────────────────── Connection ───────────────────────

    /// <summary>Which protocol version this client speaks.</summary>
    InspectorProtocolVersion ProtocolVersion { get; }

    /// <summary>Host the agent is running on.</summary>
    string Host { get; }

    /// <summary>Port the agent is listening on.</summary>
    int Port { get; }

    /// <summary>Get the agent's status including platform and device info.</summary>
    Task<InspectorAgentStatus> GetAgentStatusAsync(CancellationToken ct = default);

    /// <summary>Get the agent's capability manifest.</summary>
    Task<InspectorCapabilities> GetCapabilitiesAsync(CancellationToken ct = default);

    // ─────────────────────── Visual Tree ─────────────────────

    /// <summary>Get the full visual tree.</summary>
    Task<IReadOnlyList<InspectorElement>> GetTreeAsync(TreeOptions? options = null, CancellationToken ct = default);

    /// <summary>Get a single element by its globally unique ID.</summary>
    Task<InspectorElement?> GetElementAsync(string elementId, CancellationToken ct = default);

    /// <summary>Query elements using a locator strategy.</summary>
    Task<IReadOnlyList<InspectorElement>> QueryElementsAsync(ElementQuery query, CancellationToken ct = default);

    /// <summary>Find elements at the given coordinates (deepest first).</summary>
    Task<IReadOnlyList<InspectorElement>> HitTestAsync(double x, double y, string? window = null, CancellationToken ct = default);

    // ─────────────────────── Screenshots ─────────────────────

    /// <summary>Capture a screenshot (full window or specific element).</summary>
    Task<byte[]?> GetScreenshotAsync(ScreenshotOptions? options = null, CancellationToken ct = default);

    // ─────────────────────── Element Properties ──────────────

    /// <summary>Get the current value of a named property on an element.</summary>
    Task<object?> GetPropertyAsync(string elementId, string propertyName, CancellationToken ct = default);

    /// <summary>Set the value of a named property on an element.</summary>
    Task<object?> SetPropertyAsync(string elementId, string propertyName, object value, CancellationToken ct = default);

    // ─────────────────────── UI Actions ──────────────────────

    Task<ActionResult> TapAsync(TapRequest request, CancellationToken ct = default);
    Task<ActionResult> FillAsync(FillRequest request, CancellationToken ct = default);
    Task<ActionResult> ClearAsync(ClearRequest request, CancellationToken ct = default);
    Task<ActionResult> FocusAsync(FocusRequest request, CancellationToken ct = default);
    Task<ActionResult> ScrollAsync(ScrollRequest request, CancellationToken ct = default);
    Task<ActionResult> NavigateAsync(NavigateRequest request, CancellationToken ct = default);
    Task<ActionResult> ResizeAsync(ResizeRequest request, CancellationToken ct = default);
    Task<ActionResult> BackAsync(BackRequest request, CancellationToken ct = default);
    Task<ActionResult> KeyAsync(KeyRequest request, CancellationToken ct = default);
    Task<ActionResult> GestureAsync(GestureRequest request, CancellationToken ct = default);
    Task<ActionResult> BatchAsync(BatchRequest request, CancellationToken ct = default);

    // ─────────────────────── WebView ─────────────────────────

    /// <summary>List all active WebView contexts.</summary>
    Task<IReadOnlyList<InspectorWebViewContext>> GetWebViewContextsAsync(CancellationToken ct = default);

    /// <summary>Evaluate a JavaScript expression in a WebView.</summary>
    Task<WebViewEvalResult> EvaluateJavaScriptAsync(string expression, string? contextId = null, CancellationToken ct = default);

    /// <summary>Get a DOM snapshot from a WebView.</summary>
    Task<object?> GetWebViewDomAsync(string? contextId = null, CancellationToken ct = default);

    /// <summary>Query the DOM with a CSS selector.</summary>
    Task<IReadOnlyList<object>> QueryWebViewDomAsync(string selector, string? contextId = null, CancellationToken ct = default);

    /// <summary>Get the HTML source of a WebView page.</summary>
    Task<string?> GetWebViewSourceAsync(string? contextId = null, CancellationToken ct = default);

    /// <summary>Navigate a WebView to a URL.</summary>
    Task<bool> NavigateWebViewAsync(string url, string? contextId = null, CancellationToken ct = default);

    /// <summary>Click an element in a WebView by CSS selector.</summary>
    Task<bool> ClickInWebViewAsync(string selector, string? contextId = null, CancellationToken ct = default);

    /// <summary>Fill text into a WebView input element by CSS selector.</summary>
    Task<bool> FillInWebViewAsync(string selector, string text, string? contextId = null, CancellationToken ct = default);

    /// <summary>Capture a screenshot of a WebView.</summary>
    Task<byte[]?> GetWebViewScreenshotAsync(string? contextId = null, CancellationToken ct = default);

    // ─────────────────────── Network ─────────────────────────

    /// <summary>Get captured network requests.</summary>
    Task<IReadOnlyList<InspectorNetworkRequest>> GetNetworkRequestsAsync(CancellationToken ct = default);

    /// <summary>Get full details of a network request.</summary>
    Task<InspectorNetworkRequestDetail?> GetNetworkRequestDetailAsync(string id, CancellationToken ct = default);

    /// <summary>Clear the captured request buffer.</summary>
    Task ClearNetworkRequestsAsync(CancellationToken ct = default);

    /// <summary>Stream network requests in real time.</summary>
    Task StreamNetworkRequestsAsync(
        Action<IReadOnlyList<InspectorNetworkRequest>>? onReplay,
        Action<InspectorNetworkRequest> onRequest,
        CancellationToken ct = default);

    // ─────────────────────── Logs ────────────────────────────

    /// <summary>Get recent log entries.</summary>
    Task<IReadOnlyList<InspectorLogEntry>> GetLogsAsync(LogQuery? query = null, CancellationToken ct = default);

    /// <summary>Stream log entries in real time.</summary>
    Task StreamLogsAsync(
        Action<IReadOnlyList<InspectorLogEntry>>? onReplay,
        Action<InspectorLogEntry> onEntry,
        LogStreamOptions? options = null,
        CancellationToken ct = default);

    // ─────────────────────── Profiler ────────────────────────

    Task<InspectorProfilerCapabilities> GetProfilerCapabilitiesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<InspectorProfilerSession>> GetProfilerSessionsAsync(CancellationToken ct = default);
    Task<InspectorProfilerSession> StartProfilingAsync(int? sampleIntervalMs = null, CancellationToken ct = default);
    Task StopProfilingAsync(string sessionId, CancellationToken ct = default);
    Task<InspectorProfilerBatch> GetProfilerSamplesAsync(string sessionId, int? sampleCursor = null, int? markerCursor = null, int? spanCursor = null, int? limit = null, CancellationToken ct = default);
    Task<IReadOnlyList<InspectorProfilerHotspot>> GetProfilerHotspotsAsync(int? limit = null, double? minDurationMs = null, string? kind = null, CancellationToken ct = default);
    Task<IReadOnlyList<InspectorProfilerMarker>> GetProfilerMarkersAsync(int? sampleCursor = null, int? limit = null, CancellationToken ct = default);
    Task<IReadOnlyList<InspectorProfilerSpan>> GetProfilerSpansAsync(int? spanCursor = null, int? limit = null, CancellationToken ct = default);

    // ─────────────────────── Device ──────────────────────────

    Task<InspectorDeviceInfo> GetDeviceInfoAsync(CancellationToken ct = default);
    Task<InspectorDisplayInfo> GetDisplayInfoAsync(CancellationToken ct = default);
    Task<InspectorBatteryInfo> GetBatteryInfoAsync(CancellationToken ct = default);
    Task<InspectorConnectivityInfo> GetConnectivityAsync(CancellationToken ct = default);
    Task<InspectorAppInfo> GetAppInfoAsync(CancellationToken ct = default);

    // ─────────────────────── Sensors ─────────────────────────

    Task<IReadOnlyList<InspectorSensorInfo>> GetSensorsAsync(CancellationToken ct = default);
    Task StartSensorAsync(string sensor, string? speed = null, CancellationToken ct = default);
    Task StopSensorAsync(string sensor, CancellationToken ct = default);
    Task StreamSensorAsync(string sensor, Action<InspectorSensorReading> onReading, string? speed = null, int? throttleMs = null, CancellationToken ct = default);

    // ─────────────────────── Permissions & Geolocation ───────

    Task<IReadOnlyList<InspectorPermissionStatus>> GetPermissionsAsync(CancellationToken ct = default);
    Task<InspectorPermissionStatus> CheckPermissionAsync(string permission, CancellationToken ct = default);
    Task<InspectorGeolocation> GetGeolocationAsync(string? accuracy = null, int? timeoutSeconds = null, CancellationToken ct = default);

    // ─────────────────────── Storage ─────────────────────────

    Task<IReadOnlyList<InspectorPreferenceEntry>> GetPreferencesAsync(string? sharedName = null, CancellationToken ct = default);
    Task<InspectorPreferenceEntry?> GetPreferenceAsync(string key, string? type = null, string? sharedName = null, CancellationToken ct = default);
    Task SetPreferenceAsync(string key, object value, string? type = null, string? sharedName = null, CancellationToken ct = default);
    Task DeletePreferenceAsync(string key, string? sharedName = null, CancellationToken ct = default);
    Task ClearPreferencesAsync(string? sharedName = null, CancellationToken ct = default);

    Task<InspectorSecureStorageEntry?> GetSecureStorageAsync(string key, CancellationToken ct = default);
    Task SetSecureStorageAsync(string key, string value, CancellationToken ct = default);
    Task DeleteSecureStorageAsync(string key, CancellationToken ct = default);
    Task ClearSecureStorageAsync(CancellationToken ct = default);

    // ─────────────────────── File Storage ────────────────────

    /// <summary>List the directories the agent will let you browse.</summary>
    Task<IReadOnlyList<InspectorStorageRoot>> GetStorageRootsAsync(CancellationToken ct = default);

    /// <summary>List one directory. <paramref name="path"/> is relative to the root; empty lists the root itself.</summary>
    Task<InspectorFileListing> ListFilesAsync(string? root = null, string? path = null, CancellationToken ct = default);

    /// <summary>Read a file's bytes.</summary>
    Task<InspectorFileContent?> DownloadFileAsync(string path, string? root = null, CancellationToken ct = default);

    /// <summary>Write a file, creating any missing parent directories. Overwrites what is there.</summary>
    Task UploadFileAsync(string path, byte[] content, string? root = null, CancellationToken ct = default);

    Task DeleteFileAsync(string path, string? root = null, CancellationToken ct = default);

    Task CreateDirectoryAsync(string path, string? root = null, CancellationToken ct = default);

    /// <summary>Delete a directory. Without <paramref name="recursive"/> a non-empty one is refused.</summary>
    Task DeleteDirectoryAsync(string path, bool recursive = false, string? root = null, CancellationToken ct = default);

    /// <summary>Rename or move a file or directory within one root.</summary>
    Task MoveAsync(string from, string to, bool overwrite = false, string? root = null, CancellationToken ct = default);

    // ─────────────────────── SQLite ──────────────────────────
    //
    // Answered by the app itself, against the live file. Copying the database out would read a
    // snapshot, miss whatever is still in the write-ahead log, and overwrite the app's own writes
    // when it went back.

    /// <summary>The tables and views in a database.</summary>
    Task<InspectorDatabaseSchema> GetDatabaseSchemaAsync(string path, string? root = null, CancellationToken ct = default);

    /// <summary>
    /// Runs SQL and returns a page of the result, or the error it failed with. Does not throw for
    /// anything the SQL did - a statement that will not parse is an answer, not a fault.
    /// </summary>
    Task<InspectorDatabaseResult> QueryDatabaseAsync(string path, string sql, int? maxRows = null, string? root = null, CancellationToken ct = default);

    /// <summary>One table's rows, with the rowid that names each - what the editable grid is built from.</summary>
    Task<InspectorDatabaseRows> GetDatabaseRowsAsync(string path, string table, int? maxRows = null, string? root = null, CancellationToken ct = default);

    /// <summary>Adds one record, and answers with it as it stands afterwards.</summary>
    Task<InspectorDatabaseResult> InsertDatabaseRowAsync(string path, string table, IReadOnlyList<InspectorDatabaseCell> values, string? root = null, CancellationToken ct = default);

    /// <summary>Changes cells of one record, and answers with the record as it stands afterwards.</summary>
    Task<InspectorDatabaseResult> UpdateDatabaseRowAsync(string path, string table, long rowId, IReadOnlyList<InspectorDatabaseCell> changes, string? root = null, CancellationToken ct = default);

    Task<InspectorDatabaseResult> DeleteDatabaseRowAsync(string path, string table, long rowId, string? root = null, CancellationToken ct = default);

    /// <summary>Makes a new, empty database - a real one, with the header a file needs.</summary>
    Task CreateDatabaseAsync(string path, string? root = null, CancellationToken ct = default);
}

/// <summary>
/// Thrown when the agent refuses a file operation. Carries the agent's own wording — it explains the
/// refusal far better than a status code does ("root 'cache' does not support 'upload'").
/// </summary>
public class InspectorFileException : Exception
{
    public InspectorFileException(string message) : base(message) { }
}

/// <summary>
/// Factory that auto-detects whether the target agent speaks v1 or legacy
/// and returns the correct <see cref="IAppInspectorClient"/> implementation.
/// </summary>
public interface IAppInspectorClientFactory
{
    /// <summary>
    /// Create a client for the agent at the given host and port.
    /// Probes v1 first; falls back to legacy on failure.
    /// </summary>
    Task<IAppInspectorClient> CreateAsync(string host, int port, CancellationToken ct = default);
}
