using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Inspector;
using Microsoft.Extensions.Logging;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Client implementation for the DevFlow v1 protocol (/api/v1/*).
/// </summary>
public class DevFlowV1Client : IAppInspectorClient
{
    private readonly HttpClient _http;
    private readonly ILogger _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    public InspectorProtocolVersion ProtocolVersion => InspectorProtocolVersion.V1;
    public string Host { get; }
    public int Port { get; }

    public DevFlowV1Client(string host, int port, IHttpClientFactory httpClientFactory, ILogger logger)
    {
        Host = host;
        Port = port;
        _logger = logger;
        _http = httpClientFactory.CreateClient("DevFlowAgent");
        _http.BaseAddress = new Uri($"http://{host}:{port}");
        _http.Timeout = TimeSpan.FromSeconds(15);
    }

    // ─────────────────────── Agent ───────────────────────────

    public async Task<InspectorAgentStatus> GetAgentStatusAsync(CancellationToken ct = default)
    {
        var result = await _http.GetFromJsonAsync<InspectorAgentStatus>("/api/v1/agent/status", JsonOptions, ct);
        return result ?? new InspectorAgentStatus();
    }

    public async Task<InspectorCapabilities> GetCapabilitiesAsync(CancellationToken ct = default)
    {
        var result = await _http.GetFromJsonAsync<InspectorCapabilities>("/api/v1/agent/capabilities", JsonOptions, ct);
        return result ?? new InspectorCapabilities();
    }

    // ─────────────────────── Visual Tree ─────────────────────

    public async Task<IReadOnlyList<InspectorElement>> GetTreeAsync(TreeOptions? options = null, CancellationToken ct = default)
    {
        var url = "/api/v1/ui/tree";
        var query = new List<string>();
        if (options?.Depth.HasValue == true) query.Add($"depth={options.Depth.Value}");
        if (options?.Layer != null) query.Add($"layer={Uri.EscapeDataString(options.Layer)}");
        if (options?.RootId != null) query.Add($"rootId={Uri.EscapeDataString(options.RootId)}");
        if (options?.Include?.Count > 0) query.Add($"include={string.Join(",", options.Include)}");
        if (query.Count > 0) url += "?" + string.Join("&", query);

        var result = await _http.GetFromJsonAsync<List<InspectorElement>>(url, JsonOptions, ct);
        return result?.AsReadOnly() ?? (IReadOnlyList<InspectorElement>)[];
    }

    public async Task<InspectorElement?> GetElementAsync(string elementId, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<InspectorElement>(
                $"/api/v1/ui/elements/{Uri.EscapeDataString(elementId)}", JsonOptions, ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<InspectorElement>> QueryElementsAsync(ElementQuery query, CancellationToken ct = default)
    {
        var url = $"/api/v1/ui/elements?strategy={Uri.EscapeDataString(query.Strategy)}&value={Uri.EscapeDataString(query.Value)}";
        if (query.Limit.HasValue) url += $"&limit={query.Limit.Value}";
        if (query.Include?.Count > 0) url += $"&include={string.Join(",", query.Include)}";

        var result = await _http.GetFromJsonAsync<List<InspectorElement>>(url, JsonOptions, ct);
        return result?.AsReadOnly() ?? (IReadOnlyList<InspectorElement>)[];
    }

    public async Task<IReadOnlyList<InspectorElement>> HitTestAsync(double x, double y, string? window = null, CancellationToken ct = default)
    {
        var url = $"/api/v1/ui/hit-test?x={x}&y={y}";
        var result = await _http.GetFromJsonAsync<List<InspectorElement>>(url, JsonOptions, ct);
        return result?.AsReadOnly() ?? (IReadOnlyList<InspectorElement>)[];
    }

    // ─────────────────────── Screenshots ─────────────────────

    public async Task<byte[]?> GetScreenshotAsync(ScreenshotOptions? options = null, CancellationToken ct = default)
    {
        var url = "/api/v1/ui/screenshot";
        var query = new List<string>();
        if (options?.ElementId != null) query.Add($"elementId={Uri.EscapeDataString(options.ElementId)}");
        if (options?.MaxWidth.HasValue == true) query.Add($"maxWidth={options.MaxWidth.Value}");
        if (options?.Scale != null) query.Add($"scale={Uri.EscapeDataString(options.Scale)}");
        if (options?.Format != null) query.Add($"format={Uri.EscapeDataString(options.Format)}");
        if (query.Count > 0) url += "?" + string.Join("&", query);

        try
        {
            return await _http.GetByteArrayAsync(url, ct);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    // ─────────────────────── Element Properties ──────────────

    public async Task<object?> GetPropertyAsync(string elementId, string propertyName, CancellationToken ct = default)
    {
        var url = $"/api/v1/ui/elements/{Uri.EscapeDataString(elementId)}/properties/{Uri.EscapeDataString(propertyName)}";
        var result = await _http.GetFromJsonAsync<JsonElement>(url, JsonOptions, ct);
        return result.TryGetProperty("value", out var val) ? val.Deserialize<object>(JsonOptions) : null;
    }

    public async Task<object?> SetPropertyAsync(string elementId, string propertyName, object value, CancellationToken ct = default)
    {
        var url = $"/api/v1/ui/elements/{Uri.EscapeDataString(elementId)}/properties/{Uri.EscapeDataString(propertyName)}";
        var response = await _http.PutAsJsonAsync(url, new { value }, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, ct);
        return result.TryGetProperty("value", out var val) ? val.Deserialize<object>(JsonOptions) : null;
    }

    // ─────────────────────── UI Actions ──────────────────────

    private async Task<ActionResult> PostActionAsync<T>(string path, T request, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync(path, request, JsonOptions, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadFromJsonAsync<InspectorError>(JsonOptions, ct);
            return new ActionResult { Success = false, Error = error };
        }
        var result = await response.Content.ReadFromJsonAsync<ActionResult>(JsonOptions, ct);
        return result ?? new ActionResult { Success = true };
    }

    public Task<ActionResult> TapAsync(TapRequest request, CancellationToken ct = default) =>
        PostActionAsync("/api/v1/ui/actions/tap", request, ct);

    public Task<ActionResult> FillAsync(FillRequest request, CancellationToken ct = default) =>
        PostActionAsync("/api/v1/ui/actions/fill", request, ct);

    public Task<ActionResult> ClearAsync(ClearRequest request, CancellationToken ct = default) =>
        PostActionAsync("/api/v1/ui/actions/clear", request, ct);

    public Task<ActionResult> FocusAsync(FocusRequest request, CancellationToken ct = default) =>
        PostActionAsync("/api/v1/ui/actions/focus", request, ct);

    public Task<ActionResult> ScrollAsync(ScrollRequest request, CancellationToken ct = default) =>
        PostActionAsync("/api/v1/ui/actions/scroll", request, ct);

    public Task<ActionResult> NavigateAsync(NavigateRequest request, CancellationToken ct = default) =>
        PostActionAsync("/api/v1/ui/actions/navigate", request, ct);

    public Task<ActionResult> ResizeAsync(ResizeRequest request, CancellationToken ct = default) =>
        PostActionAsync("/api/v1/ui/actions/resize", request, ct);

    public Task<ActionResult> BackAsync(BackRequest request, CancellationToken ct = default) =>
        PostActionAsync("/api/v1/ui/actions/back", request, ct);

    public Task<ActionResult> KeyAsync(KeyRequest request, CancellationToken ct = default) =>
        PostActionAsync("/api/v1/ui/actions/key", request, ct);

    public Task<ActionResult> GestureAsync(GestureRequest request, CancellationToken ct = default) =>
        PostActionAsync("/api/v1/ui/actions/gesture", request, ct);

    public Task<ActionResult> BatchAsync(BatchRequest request, CancellationToken ct = default) =>
        PostActionAsync("/api/v1/ui/actions/batch", request, ct);

    // ─────────────────────── WebView ─────────────────────────

    public async Task<IReadOnlyList<InspectorWebViewContext>> GetWebViewContextsAsync(CancellationToken ct = default)
    {
        var result = await _http.GetFromJsonAsync<List<InspectorWebViewContext>>("/api/v1/webview/contexts", JsonOptions, ct);
        return result?.AsReadOnly() ?? (IReadOnlyList<InspectorWebViewContext>)[];
    }

    public async Task<WebViewEvalResult> EvaluateJavaScriptAsync(string expression, string? contextId = null, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/v1/webview/evaluate",
            new { expression, contextId }, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<WebViewEvalResult>(JsonOptions, ct);
        return result ?? new WebViewEvalResult();
    }

    public async Task<object?> GetWebViewDomAsync(string? contextId = null, CancellationToken ct = default)
    {
        var url = contextId != null ? $"/api/v1/webview/dom?contextId={Uri.EscapeDataString(contextId)}" : "/api/v1/webview/dom";
        return await _http.GetFromJsonAsync<object>(url, JsonOptions, ct);
    }

    public async Task<IReadOnlyList<object>> QueryWebViewDomAsync(string selector, string? contextId = null, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/v1/webview/dom/query",
            new { selector, contextId }, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<List<object>>(JsonOptions, ct);
        return result?.AsReadOnly() ?? (IReadOnlyList<object>)[];
    }

    public async Task<string?> GetWebViewSourceAsync(string? contextId = null, CancellationToken ct = default)
    {
        var url = contextId != null ? $"/api/v1/webview/source?contextId={Uri.EscapeDataString(contextId)}" : "/api/v1/webview/source";
        return await _http.GetStringAsync(url, ct);
    }

    public async Task<bool> NavigateWebViewAsync(string url, string? contextId = null, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/v1/webview/navigate",
            new { url, contextId }, JsonOptions, ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> ClickInWebViewAsync(string selector, string? contextId = null, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/v1/webview/input/click",
            new { selector, contextId }, JsonOptions, ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> FillInWebViewAsync(string selector, string text, string? contextId = null, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/v1/webview/input/fill",
            new { selector, text, contextId }, JsonOptions, ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<byte[]?> GetWebViewScreenshotAsync(string? contextId = null, CancellationToken ct = default)
    {
        var url = contextId != null ? $"/api/v1/webview/screenshot?contextId={Uri.EscapeDataString(contextId)}" : "/api/v1/webview/screenshot";
        try { return await _http.GetByteArrayAsync(url, ct); }
        catch (HttpRequestException) { return null; }
    }

    // ─────────────────────── Network ─────────────────────────

    public async Task<IReadOnlyList<InspectorNetworkRequest>> GetNetworkRequestsAsync(CancellationToken ct = default)
    {
        var result = await _http.GetFromJsonAsync<List<InspectorNetworkRequest>>("/api/v1/network/requests", JsonOptions, ct);
        return result?.AsReadOnly() ?? (IReadOnlyList<InspectorNetworkRequest>)[];
    }

    public async Task<InspectorNetworkRequestDetail?> GetNetworkRequestDetailAsync(string id, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<InspectorNetworkRequestDetail>(
                $"/api/v1/network/requests/{Uri.EscapeDataString(id)}", JsonOptions, ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task ClearNetworkRequestsAsync(CancellationToken ct = default)
    {
        await _http.DeleteAsync("/api/v1/network/requests", ct);
    }

    public Task StreamNetworkRequestsAsync(
        Action<IReadOnlyList<InspectorNetworkRequest>>? onReplay,
        Action<InspectorNetworkRequest> onRequest,
        CancellationToken ct = default)
    {
        // WebSocket streaming — will be implemented in create-v1-websocket todo
        throw new NotImplementedException("WebSocket streaming not yet implemented");
    }

    // ─────────────────────── Logs ────────────────────────────

    public async Task<IReadOnlyList<InspectorLogEntry>> GetLogsAsync(LogQuery? query = null, CancellationToken ct = default)
    {
        var url = "/api/v1/logs";
        var q = new List<string>();
        if (query?.Limit.HasValue == true) q.Add($"limit={query.Limit.Value}");
        if (query?.Skip.HasValue == true) q.Add($"skip={query.Skip.Value}");
        if (query?.Source != null) q.Add($"source={Uri.EscapeDataString(query.Source)}");
        if (query?.Level != null) q.Add($"level={Uri.EscapeDataString(query.Level)}");
        if (q.Count > 0) url += "?" + string.Join("&", q);

        var result = await _http.GetFromJsonAsync<List<InspectorLogEntry>>(url, JsonOptions, ct);
        return result?.AsReadOnly() ?? (IReadOnlyList<InspectorLogEntry>)[];
    }

    public Task StreamLogsAsync(
        Action<IReadOnlyList<InspectorLogEntry>>? onReplay,
        Action<InspectorLogEntry> onEntry,
        LogStreamOptions? options = null,
        CancellationToken ct = default)
    {
        // WebSocket streaming — will be implemented in create-v1-websocket todo
        throw new NotImplementedException("WebSocket streaming not yet implemented");
    }

    // ─────────────────────── Profiler ────────────────────────

    public async Task<InspectorProfilerCapabilities> GetProfilerCapabilitiesAsync(CancellationToken ct = default)
    {
        var result = await _http.GetFromJsonAsync<InspectorProfilerCapabilities>("/api/v1/profiler/capabilities", JsonOptions, ct);
        return result ?? new InspectorProfilerCapabilities();
    }

    public async Task<IReadOnlyList<InspectorProfilerSession>> GetProfilerSessionsAsync(CancellationToken ct = default)
    {
        var result = await _http.GetFromJsonAsync<List<InspectorProfilerSession>>("/api/v1/profiler/sessions", JsonOptions, ct);
        return result?.AsReadOnly() ?? (IReadOnlyList<InspectorProfilerSession>)[];
    }

    public async Task<InspectorProfilerSession> StartProfilingAsync(int? sampleIntervalMs = null, CancellationToken ct = default)
    {
        var payload = sampleIntervalMs.HasValue ? new { sampleIntervalMs = sampleIntervalMs.Value } : (object?)null;
        var response = await _http.PostAsJsonAsync("/api/v1/profiler/sessions", payload, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<InspectorProfilerSession>(JsonOptions, ct);
        return result ?? new InspectorProfilerSession();
    }

    public async Task StopProfilingAsync(string sessionId, CancellationToken ct = default)
    {
        await _http.DeleteAsync($"/api/v1/profiler/sessions/{Uri.EscapeDataString(sessionId)}", ct);
    }

    public async Task<InspectorProfilerBatch> GetProfilerSamplesAsync(string sessionId, int? sampleCursor = null, int? markerCursor = null, int? spanCursor = null, int? limit = null, CancellationToken ct = default)
    {
        var url = $"/api/v1/profiler/sessions/{Uri.EscapeDataString(sessionId)}/samples";
        var q = new List<string>();
        if (sampleCursor.HasValue) q.Add($"sampleCursor={sampleCursor.Value}");
        if (markerCursor.HasValue) q.Add($"markerCursor={markerCursor.Value}");
        if (spanCursor.HasValue) q.Add($"spanCursor={spanCursor.Value}");
        if (limit.HasValue) q.Add($"limit={limit.Value}");
        if (q.Count > 0) url += "?" + string.Join("&", q);

        var result = await _http.GetFromJsonAsync<InspectorProfilerBatch>(url, JsonOptions, ct);
        return result ?? new InspectorProfilerBatch();
    }

    public async Task<IReadOnlyList<InspectorProfilerHotspot>> GetProfilerHotspotsAsync(int? limit = null, double? minDurationMs = null, string? kind = null, CancellationToken ct = default)
    {
        var url = "/api/v1/profiler/hotspots";
        var q = new List<string>();
        if (limit.HasValue) q.Add($"limit={limit.Value}");
        if (minDurationMs.HasValue) q.Add($"minDurationMs={minDurationMs.Value}");
        if (kind != null) q.Add($"kind={Uri.EscapeDataString(kind)}");
        if (q.Count > 0) url += "?" + string.Join("&", q);

        var result = await _http.GetFromJsonAsync<List<InspectorProfilerHotspot>>(url, JsonOptions, ct);
        return result?.AsReadOnly() ?? (IReadOnlyList<InspectorProfilerHotspot>)[];
    }

    public async Task<IReadOnlyList<InspectorProfilerMarker>> GetProfilerMarkersAsync(int? sampleCursor = null, int? limit = null, CancellationToken ct = default)
    {
        var url = "/api/v1/profiler/markers";
        var q = new List<string>();
        if (sampleCursor.HasValue) q.Add($"cursor={sampleCursor.Value}");
        if (limit.HasValue) q.Add($"limit={limit.Value}");
        if (q.Count > 0) url += "?" + string.Join("&", q);

        var result = await _http.GetFromJsonAsync<List<InspectorProfilerMarker>>(url, JsonOptions, ct);
        return result?.AsReadOnly() ?? (IReadOnlyList<InspectorProfilerMarker>)[];
    }

    public async Task<IReadOnlyList<InspectorProfilerSpan>> GetProfilerSpansAsync(int? spanCursor = null, int? limit = null, CancellationToken ct = default)
    {
        var url = "/api/v1/profiler/spans";
        var q = new List<string>();
        if (spanCursor.HasValue) q.Add($"cursor={spanCursor.Value}");
        if (limit.HasValue) q.Add($"limit={limit.Value}");
        if (q.Count > 0) url += "?" + string.Join("&", q);

        var result = await _http.GetFromJsonAsync<List<InspectorProfilerSpan>>(url, JsonOptions, ct);
        return result?.AsReadOnly() ?? (IReadOnlyList<InspectorProfilerSpan>)[];
    }

    // ─────────────────────── Device ──────────────────────────

    public async Task<InspectorDeviceInfo> GetDeviceInfoAsync(CancellationToken ct = default)
    {
        var result = await _http.GetFromJsonAsync<InspectorDeviceInfo>("/api/v1/device/info", JsonOptions, ct);
        return result ?? new InspectorDeviceInfo();
    }

    public async Task<InspectorDisplayInfo> GetDisplayInfoAsync(CancellationToken ct = default)
    {
        var result = await _http.GetFromJsonAsync<InspectorDisplayInfo>("/api/v1/device/display", JsonOptions, ct);
        return result ?? new InspectorDisplayInfo();
    }

    public async Task<InspectorBatteryInfo> GetBatteryInfoAsync(CancellationToken ct = default)
    {
        var result = await _http.GetFromJsonAsync<InspectorBatteryInfo>("/api/v1/device/battery", JsonOptions, ct);
        return result ?? new InspectorBatteryInfo();
    }

    public async Task<InspectorConnectivityInfo> GetConnectivityAsync(CancellationToken ct = default)
    {
        var result = await _http.GetFromJsonAsync<InspectorConnectivityInfo>("/api/v1/device/connectivity", JsonOptions, ct);
        return result ?? new InspectorConnectivityInfo();
    }

    public async Task<InspectorAppInfo> GetAppInfoAsync(CancellationToken ct = default)
    {
        var result = await _http.GetFromJsonAsync<InspectorAppInfo>("/api/v1/device/app", JsonOptions, ct);
        return result ?? new InspectorAppInfo();
    }

    // ─────────────────────── Sensors ─────────────────────────

    public async Task<IReadOnlyList<InspectorSensorInfo>> GetSensorsAsync(CancellationToken ct = default)
    {
        var result = await _http.GetFromJsonAsync<List<InspectorSensorInfo>>("/api/v1/device/sensors", JsonOptions, ct);
        return result?.AsReadOnly() ?? (IReadOnlyList<InspectorSensorInfo>)[];
    }

    public async Task StartSensorAsync(string sensor, string? speed = null, CancellationToken ct = default)
    {
        var url = $"/api/v1/device/sensors/{Uri.EscapeDataString(sensor)}/start";
        if (speed != null) url += $"?speed={Uri.EscapeDataString(speed)}";
        var response = await _http.PostAsync(url, null, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task StopSensorAsync(string sensor, CancellationToken ct = default)
    {
        var response = await _http.PostAsync($"/api/v1/device/sensors/{Uri.EscapeDataString(sensor)}/stop", null, ct);
        response.EnsureSuccessStatusCode();
    }

    public Task StreamSensorAsync(string sensor, Action<InspectorSensorReading> onReading, string? speed = null, int? throttleMs = null, CancellationToken ct = default)
    {
        // WebSocket streaming — will be implemented in create-v1-websocket todo
        throw new NotImplementedException("WebSocket streaming not yet implemented");
    }

    // ─────────────────────── Permissions & Geolocation ───────

    public async Task<IReadOnlyList<InspectorPermissionStatus>> GetPermissionsAsync(CancellationToken ct = default)
    {
        var result = await _http.GetFromJsonAsync<List<InspectorPermissionStatus>>("/api/v1/device/permissions", JsonOptions, ct);
        return result?.AsReadOnly() ?? (IReadOnlyList<InspectorPermissionStatus>)[];
    }

    public async Task<InspectorPermissionStatus> CheckPermissionAsync(string permission, CancellationToken ct = default)
    {
        var result = await _http.GetFromJsonAsync<InspectorPermissionStatus>(
            $"/api/v1/device/permissions/{Uri.EscapeDataString(permission)}", JsonOptions, ct);
        return result ?? new InspectorPermissionStatus { Name = permission };
    }

    public async Task<InspectorGeolocation> GetGeolocationAsync(string? accuracy = null, int? timeoutSeconds = null, CancellationToken ct = default)
    {
        var url = "/api/v1/device/geolocation";
        var q = new List<string>();
        if (accuracy != null) q.Add($"accuracy={Uri.EscapeDataString(accuracy)}");
        if (timeoutSeconds.HasValue) q.Add($"timeout={timeoutSeconds.Value}");
        if (q.Count > 0) url += "?" + string.Join("&", q);

        var result = await _http.GetFromJsonAsync<InspectorGeolocation>(url, JsonOptions, ct);
        return result ?? new InspectorGeolocation();
    }

    // ─────────────────────── Storage ─────────────────────────

    public async Task<IReadOnlyList<InspectorPreferenceEntry>> GetPreferencesAsync(string? sharedName = null, CancellationToken ct = default)
    {
        var url = "/api/v1/storage/preferences";
        if (sharedName != null) url += $"?sharedName={Uri.EscapeDataString(sharedName)}";
        var result = await _http.GetFromJsonAsync<List<InspectorPreferenceEntry>>(url, JsonOptions, ct);
        return result?.AsReadOnly() ?? (IReadOnlyList<InspectorPreferenceEntry>)[];
    }

    public async Task<InspectorPreferenceEntry?> GetPreferenceAsync(string key, string? type = null, string? sharedName = null, CancellationToken ct = default)
    {
        var url = $"/api/v1/storage/preferences/{Uri.EscapeDataString(key)}";
        var q = new List<string>();
        if (type != null) q.Add($"type={Uri.EscapeDataString(type)}");
        if (sharedName != null) q.Add($"sharedName={Uri.EscapeDataString(sharedName)}");
        if (q.Count > 0) url += "?" + string.Join("&", q);

        try { return await _http.GetFromJsonAsync<InspectorPreferenceEntry>(url, JsonOptions, ct); }
        catch (HttpRequestException) { return null; }
    }

    public async Task SetPreferenceAsync(string key, object value, string? type = null, string? sharedName = null, CancellationToken ct = default)
    {
        var url = $"/api/v1/storage/preferences/{Uri.EscapeDataString(key)}";
        var response = await _http.PutAsJsonAsync(url, new { value, type, sharedName }, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeletePreferenceAsync(string key, string? sharedName = null, CancellationToken ct = default)
    {
        var url = $"/api/v1/storage/preferences/{Uri.EscapeDataString(key)}";
        if (sharedName != null) url += $"?sharedName={Uri.EscapeDataString(sharedName)}";
        await _http.DeleteAsync(url, ct);
    }

    public async Task ClearPreferencesAsync(string? sharedName = null, CancellationToken ct = default)
    {
        var url = "/api/v1/storage/preferences";
        if (sharedName != null) url += $"?sharedName={Uri.EscapeDataString(sharedName)}";
        await _http.DeleteAsync(url, ct);
    }

    public async Task<InspectorSecureStorageEntry?> GetSecureStorageAsync(string key, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<InspectorSecureStorageEntry>(
                $"/api/v1/storage/secure/{Uri.EscapeDataString(key)}", JsonOptions, ct);
        }
        catch (HttpRequestException) { return null; }
    }

    public async Task SetSecureStorageAsync(string key, string value, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync(
            $"/api/v1/storage/secure/{Uri.EscapeDataString(key)}", new { value }, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteSecureStorageAsync(string key, CancellationToken ct = default)
    {
        await _http.DeleteAsync($"/api/v1/storage/secure/{Uri.EscapeDataString(key)}", ct);
    }

    public async Task ClearSecureStorageAsync(CancellationToken ct = default)
    {
        await _http.DeleteAsync("/api/v1/storage/secure", ct);
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
