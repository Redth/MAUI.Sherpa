using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MauiSherpa.Core.Models.DevFlow;

namespace MauiSherpa.Core.Services;

/// <summary>
/// HTTP/WebSocket client for communicating with a MauiDevFlow agent and broker.
/// </summary>
public class DevFlowAgentClient : IDisposable
{
    private readonly HttpClient _http;
    private ClientWebSocket? _networkWs;
    private CancellationTokenSource? _networkWsCts;
    private ClientWebSocket? _logsWs;
    private CancellationTokenSource? _logsWsCts;
    private bool _disposed;

    public string AgentHost { get; }
    public int AgentPort { get; }
    public string BaseUrl => $"http://{AgentHost}:{AgentPort}";

    public DevFlowAgentClient(string host = "localhost", int port = 9223)
    {
        AgentHost = host;
        AgentPort = port;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    // --- Broker API ---

    /// <summary>
    /// Fetch agents from the broker at the given host/port.
    /// </summary>
    public static async Task<List<BrokerAgent>> GetBrokerAgentsAsync(
        string brokerHost = "localhost", int brokerPort = 19223, CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            var url = $"http://{brokerHost}:{brokerPort}/api/agents";
            var json = await http.GetStringAsync(url, ct);
            return JsonSerializer.Deserialize<List<BrokerAgent>>(json) ?? new();
        }
        catch
        {
            return new();
        }
    }

    /// <summary>
    /// Check if the broker is healthy.
    /// </summary>
    public static async Task<bool> IsBrokerHealthyAsync(
        string brokerHost = "localhost", int brokerPort = 19223, CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        try
        {
            var url = $"http://{brokerHost}:{brokerPort}/api/health";
            var response = await http.GetAsync(url, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // --- Agent API ---

    public async Task<DevFlowAgentStatus?> GetStatusAsync(CancellationToken ct = default)
    {
        return await GetAsync<DevFlowAgentStatus>("/api/status", ct);
    }

    public async Task<List<DevFlowElementInfo>> GetTreeAsync(int maxDepth = 0, int? window = null, CancellationToken ct = default)
    {
        var parts = new List<string>();
        if (maxDepth > 0) parts.Add($"depth={maxDepth}");
        if (window != null) parts.Add($"window={window}");
        var url = parts.Count > 0 ? $"/api/tree?{string.Join("&", parts)}" : "/api/tree";
        return await GetAsync<List<DevFlowElementInfo>>(url, ct) ?? new();
    }

    public async Task<DevFlowElementInfo?> GetElementAsync(string id, CancellationToken ct = default)
    {
        return await GetAsync<DevFlowElementInfo>($"/api/element/{Uri.EscapeDataString(id)}", ct);
    }

    public async Task<List<DevFlowElementInfo>> QueryAsync(
        string? type = null, string? automationId = null, string? text = null, string? selector = null, CancellationToken ct = default)
    {
        var parts = new List<string>();
        if (type != null) parts.Add($"type={Uri.EscapeDataString(type)}");
        if (automationId != null) parts.Add($"automationId={Uri.EscapeDataString(automationId)}");
        if (text != null) parts.Add($"text={Uri.EscapeDataString(text)}");
        if (selector != null) parts.Add($"selector={Uri.EscapeDataString(selector)}");
        var url = $"/api/query?{string.Join("&", parts)}";
        return await GetAsync<List<DevFlowElementInfo>>(url, ct) ?? new();
    }

    public async Task<string?> GetPropertyAsync(string elementId, string propertyName, CancellationToken ct = default)
    {
        try
        {
            var json = await _http.GetStringAsync($"{BaseUrl}/api/property/{Uri.EscapeDataString(elementId)}/{Uri.EscapeDataString(propertyName)}", ct);
            var result = JsonSerializer.Deserialize<JsonElement>(json);
            if (result.TryGetProperty("value", out var val))
                return val.GetString();
        }
        catch { }
        return null;
    }

    public async Task<bool> SetPropertyAsync(string elementId, string propertyName, string value, CancellationToken ct = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(new { value });
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync(
                $"{BaseUrl}/api/property/{Uri.EscapeDataString(elementId)}/{Uri.EscapeDataString(propertyName)}",
                content, ct);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<byte[]?> GetScreenshotAsync(int? window = null, string? elementId = null, CancellationToken ct = default)
    {
        try
        {
            var parts = new List<string>();
            if (window != null) parts.Add($"window={window}");
            if (elementId != null) parts.Add($"id={Uri.EscapeDataString(elementId)}");
            var url = parts.Count > 0
                ? $"{BaseUrl}/api/screenshot?{string.Join("&", parts)}"
                : $"{BaseUrl}/api/screenshot";
            var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadAsByteArrayAsync(ct);
        }
        catch { return null; }
    }

    public async Task<bool> TapAsync(string elementId, CancellationToken ct = default)
        => await PostActionAsync("/api/action/tap", new { elementId }, ct);

    public async Task<bool> FillAsync(string elementId, string text, CancellationToken ct = default)
        => await PostActionAsync("/api/action/fill", new { elementId, text }, ct);

    public async Task<bool> FocusAsync(string elementId, CancellationToken ct = default)
        => await PostActionAsync("/api/action/focus", new { elementId }, ct);

    // --- Hit Test ---

    public async Task<DevFlowHitTestResult?> HitTestAsync(double x, double y, int? window = null, CancellationToken ct = default)
    {
        var parts = new List<string> { $"x={x}", $"y={y}" };
        if (window != null) parts.Add($"window={window}");
        var url = $"/api/hittest?{string.Join("&", parts)}";
        return await GetAsync<DevFlowHitTestResult>(url, ct);
    }

    // --- Logs ---

    public async Task<List<DevFlowLogEntry>> GetLogsAsync(int limit = 100, int skip = 0, string? source = null, CancellationToken ct = default)
    {
        var parts = new List<string> { $"limit={limit}" };
        if (skip > 0) parts.Add($"skip={skip}");
        if (source != null) parts.Add($"source={Uri.EscapeDataString(source)}");
        var url = $"/api/logs?{string.Join("&", parts)}";
        return await GetAsync<List<DevFlowLogEntry>>(url, ct) ?? new();
    }

    // --- Profiling ---

    public async Task<DevFlowProfilerCapabilities?> GetProfilerCapabilitiesAsync(CancellationToken ct = default)
    {
        return await GetAsync<DevFlowProfilerCapabilities>("/api/profiler/capabilities", ct);
    }

    public async Task<DevFlowProfilerStartResponse?> StartProfilerAsync(int? sampleIntervalMs = null, CancellationToken ct = default)
    {
        if (sampleIntervalMs.HasValue)
            return await PostAsync<DevFlowProfilerStartResponse>("/api/profiler/start", new { sampleIntervalMs = sampleIntervalMs.Value }, ct);

        return await PostAsync<DevFlowProfilerStartResponse>("/api/profiler/start", new { }, ct);
    }

    public async Task<DevFlowProfilerStopResponse?> StopProfilerAsync(CancellationToken ct = default)
    {
        return await PostAsync<DevFlowProfilerStopResponse>("/api/profiler/stop", new { }, ct);
    }

    public async Task<DevFlowProfilerBatch?> GetProfilerSamplesAsync(
        long sampleCursor = 0,
        long markerCursor = 0,
        long spanCursor = 0,
        int limit = 200,
        CancellationToken ct = default)
    {
        var safeLimit = Math.Clamp(limit, 1, 5000);
        var url = $"/api/profiler/samples?sampleCursor={sampleCursor}&markerCursor={markerCursor}&spanCursor={spanCursor}&limit={safeLimit}";
        return await GetAsync<DevFlowProfilerBatch>(url, ct);
    }

    public async Task<List<DevFlowProfilerHotspot>> GetProfilerHotspotsAsync(
        int limit = 20,
        int minDurationMs = 16,
        string? kind = "ui.operation",
        CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 200);
        minDurationMs = Math.Clamp(minDurationMs, 0, 60_000);
        var url = $"/api/profiler/hotspots?limit={limit}&minDurationMs={minDurationMs}";
        if (!string.IsNullOrWhiteSpace(kind))
            url += $"&kind={Uri.EscapeDataString(kind)}";
        return await GetAsync<List<DevFlowProfilerHotspot>>(url, ct) ?? new();
    }

    public async Task<bool> PublishProfilerMarkerAsync(
        string name,
        string type = "user.action",
        string? payloadJson = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        return await PostActionAsync("/api/profiler/marker", new { name, type, payloadJson }, ct);
    }

    // --- CDP ---

    public async Task<CdpResponse?> SendCdpCommandAsync(string method, Dictionary<string, object?>? parameters = null, string? targetId = null, CancellationToken ct = default)
    {
        try
        {
            var bodyObj = new Dictionary<string, object?>
            {
                ["method"] = method,
            };
            if (parameters != null)
                bodyObj["params"] = parameters;
            if (targetId != null)
                bodyObj["targetId"] = targetId;

            var json = JsonSerializer.Serialize(bodyObj);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{BaseUrl}/api/cdp", content, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<CdpResponse>(responseBody);
        }
        catch { return null; }
    }

    public async Task<List<CdpTarget>> GetCdpTargetsAsync(CancellationToken ct = default)
    {
        return await GetAsync<List<CdpTarget>>("/api/cdp/targets", ct) ?? new();
    }

    // --- Network ---

    public async Task<List<DevFlowNetworkRequest>> GetNetworkRequestsAsync(CancellationToken ct = default)
    {
        return await GetAsync<List<DevFlowNetworkRequest>>("/api/network", ct) ?? new();
    }

    public async Task<DevFlowNetworkRequest?> GetNetworkRequestDetailAsync(string id, CancellationToken ct = default)
    {
        return await GetAsync<DevFlowNetworkRequest>($"/api/network/{Uri.EscapeDataString(id)}", ct);
    }

    public async Task<bool> ClearNetworkRequestsAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsync($"{BaseUrl}/api/network/clear", null, ct);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>
    /// Connect to the /ws/network WebSocket for live request streaming.
    /// Calls onRequest for each request received (replay + live).
    /// Returns when cancelled or disconnected.
    /// </summary>
    public async Task StreamNetworkRequestsAsync(Action<DevFlowNetworkRequest> onRequest, CancellationToken ct = default)
    {
        _networkWsCts?.Cancel();
        _networkWsCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _networkWsCts.Token;

        _networkWs?.Dispose();
        _networkWs = new ClientWebSocket();

        try
        {
            var wsUrl = $"ws://{AgentHost}:{AgentPort}/ws/network";
            await _networkWs.ConnectAsync(new Uri(wsUrl), token);

            var buffer = new byte[64 * 1024];
            var sb = new StringBuilder();

            while (_networkWs.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                var result = await _networkWs.ReceiveAsync(buffer, token);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (result.EndOfMessage)
                {
                    var json = sb.ToString();
                    sb.Clear();
                    try
                    {
                        var entry = JsonSerializer.Deserialize<DevFlowNetworkRequest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (entry != null) onRequest(entry);
                    }
                    catch { /* skip malformed messages */ }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            if (_networkWs?.State == WebSocketState.Open)
            {
                try { await _networkWs.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None); }
                catch { }
            }
        }
    }

    public void StopNetworkStream()
    {
        _networkWsCts?.Cancel();
    }

    // --- Log Streaming ---

    /// <summary>
    /// Connect to the /ws/logs WebSocket for live log streaming.
    /// Calls onReplay with the initial batch of recent entries, then onEntry for each live entry.
    /// Returns when cancelled or disconnected.
    /// </summary>
    public async Task StreamLogsAsync(
        Action<List<DevFlowLogEntry>> onReplay,
        Action<DevFlowLogEntry> onEntry,
        string? source = null,
        int replay = 100,
        CancellationToken ct = default)
    {
        _logsWsCts?.Cancel();
        _logsWsCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _logsWsCts.Token;

        _logsWs?.Dispose();
        _logsWs = new ClientWebSocket();

        try
        {
            var parts = new List<string>();
            if (source != null) parts.Add($"source={Uri.EscapeDataString(source)}");
            parts.Add($"replay={replay}");
            var query = string.Join("&", parts);
            var wsUrl = $"ws://{AgentHost}:{AgentPort}/ws/logs?{query}";
            await _logsWs.ConnectAsync(new Uri(wsUrl), token);

            var buffer = new byte[64 * 1024];
            var sb = new StringBuilder();

            while (_logsWs.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                var result = await _logsWs.ReceiveAsync(buffer, token);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (result.EndOfMessage)
                {
                    var json = sb.ToString();
                    sb.Clear();
                    try
                    {
                        using var doc = JsonDocument.Parse(json);
                        var type = doc.RootElement.TryGetProperty("type", out var typeProp)
                            ? typeProp.GetString() : null;

                        if (type == "replay" && doc.RootElement.TryGetProperty("entries", out var entries))
                        {
                            var list = JsonSerializer.Deserialize<List<DevFlowLogEntry>>(
                                entries.GetRawText()) ?? new();
                            onReplay(list);
                        }
                        else if (type == "log" && doc.RootElement.TryGetProperty("entry", out var entry))
                        {
                            var logEntry = JsonSerializer.Deserialize<DevFlowLogEntry>(
                                entry.GetRawText());
                            if (logEntry != null) onEntry(logEntry);
                        }
                    }
                    catch { /* skip malformed messages */ }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            if (_logsWs?.State == WebSocketState.Open)
            {
                try { await _logsWs.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None); }
                catch { }
            }
        }
    }

    public void StopLogStream()
    {
        _logsWsCts?.Cancel();
    }

    // --- Helpers ---

    private async Task<T?> GetAsync<T>(string path, CancellationToken ct = default) where T : class
    {
        try
        {
            var response = await _http.GetStringAsync($"{BaseUrl}{path}", ct);
            return JsonSerializer.Deserialize<T>(response, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }

    private async Task<T?> PostAsync<T>(string path, object body, CancellationToken ct = default) where T : class
    {
        try
        {
            var json = JsonSerializer.Serialize(body);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{BaseUrl}{path}", content, ct);
            if (!response.IsSuccessStatusCode) return null;

            var responseBody = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(responseBody)) return null;
            return JsonSerializer.Deserialize<T>(responseBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }

    private async Task<bool> PostActionAsync(string path, object body, CancellationToken ct = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(body);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{BaseUrl}{path}", content, ct);
            if (!response.IsSuccessStatusCode) return false;
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<JsonElement>(responseBody);
            return result.TryGetProperty("success", out var success) && success.GetBoolean();
        }
        catch { return false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _networkWsCts?.Cancel();
        _networkWs?.Dispose();
        _logsWsCts?.Cancel();
        _logsWs?.Dispose();
        _http.Dispose();
    }
}
