using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MauiSherpa.Core.Models.DevFlow;

namespace MauiSherpa.Core.Services;

/// <summary>
/// HTTP/WebSocket client for communicating with a MAUI DevFlow agent and broker.
/// </summary>
public class DevFlowAgentClient : IDisposable
{
    private readonly HttpClient _http;
    private ClientWebSocket? _networkWs;
    private CancellationTokenSource? _networkWsCts;
    private ClientWebSocket? _logsWs;
    private CancellationTokenSource? _logsWsCts;
    private readonly Dictionary<string, (ClientWebSocket Ws, CancellationTokenSource Cts)> _sensorStreams = new();
    private string? _currentProfilerSessionId;
    // Default to v1 (modern DevFlow + Ailoha). GetStatusAsync probes both protocols
    // and switches to legacy if only /api/status is reachable.
    private bool _useV1 = true;
    private bool _protocolDetected;
    private bool _disposed;

    /// <summary>
    /// Protocol the client is using once detection has run. Null until the first
    /// successful <see cref="GetStatusAsync"/> call.
    /// </summary>
    public DevFlowAgentProtocol? Protocol
        => _protocolDetected ? (_useV1 ? DevFlowAgentProtocol.V1 : DevFlowAgentProtocol.Legacy) : null;

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

    /// <summary>Default broker port for the MAUI DevFlow CLI.</summary>
    public const int DevFlowBrokerPort = 19223;

    /// <summary>Default broker port for the Ailoha CLI.</summary>
    public const int AilohaBrokerPort = 19323;

    /// <summary>Standard broker ports we probe when the user accepts the default.</summary>
    public static readonly IReadOnlyList<int> DefaultBrokerPorts = new[] { DevFlowBrokerPort, AilohaBrokerPort };

    /// <summary>
    /// Fetch agents from the broker at the given host/port.
    /// </summary>
    public static async Task<List<BrokerAgent>> GetBrokerAgentsAsync(
        string brokerHost = "localhost", int brokerPort = DevFlowBrokerPort, CancellationToken ct = default)
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
        string brokerHost = "localhost", int brokerPort = DevFlowBrokerPort, CancellationToken ct = default)
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

    /// <summary>
    /// Find the first reachable broker on <paramref name="brokerHost"/> from the provided
    /// candidate ports. Returns <c>null</c> if none are reachable. Probes run in parallel.
    /// </summary>
    public static async Task<int?> FindReachableBrokerPortAsync(
        string brokerHost,
        IEnumerable<int> candidatePorts,
        CancellationToken ct = default)
    {
        var ports = candidatePorts?.Distinct().ToArray() ?? Array.Empty<int>();
        if (ports.Length == 0) return null;

        var tasks = ports
            .Select(async port => (port, healthy: await IsBrokerHealthyAsync(brokerHost, port, ct)))
            .ToList();

        var results = await Task.WhenAll(tasks);
        // Preserve candidate ordering — pick the first port in the input order that's healthy.
        foreach (var port in ports)
        {
            var match = results.FirstOrDefault(r => r.port == port && r.healthy);
            if (match.healthy) return port;
        }
        return null;
    }

    // --- Agent API ---

    public async Task<DevFlowAgentStatus?> GetStatusAsync(CancellationToken ct = default)
    {
        // Two wire shapes exist:
        //   v1  (/api/v1/agent/status): { agent:{name,version,…}, device:{model,idiom,…},
        //                                 app:{name,packageId,…}, platform, running, … }
        //   legacy (/api/status):       flat { agent, version, platform, deviceType, idiom,
        //                                     appName, running, cdpReady, cdpWebViewCount }
        // Probe v1 first; if 404/network-error, fall back to legacy. Cache the result so
        // every other endpoint dispatches to the matching URL family.
        if (!_protocolDetected)
        {
            try
            {
                var v1 = await _http.GetAsync($"{BaseUrl}/api/v1/agent/status", ct);
                if (v1.IsSuccessStatusCode)
                {
                    _useV1 = true;
                    _protocolDetected = true;
                    var body = await v1.Content.ReadAsStringAsync(ct);
                    return ParseV1Status(body);
                }

                if (v1.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    var legacy = await _http.GetAsync($"{BaseUrl}/api/status", ct);
                    if (legacy.IsSuccessStatusCode)
                    {
                        _useV1 = false;
                        _protocolDetected = true;
                        var body = await legacy.Content.ReadAsStringAsync(ct);
                        return JsonSerializer.Deserialize<DevFlowAgentStatus>(body,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    }
                }
            }
            catch { return null; }
            return null;
        }

        // Protocol already detected — just call the right endpoint.
        try
        {
            if (_useV1)
            {
                var json = await _http.GetStringAsync($"{BaseUrl}/api/v1/agent/status", ct);
                return ParseV1Status(json);
            }
            return await GetAsync<DevFlowAgentStatus>("/api/status", ct);
        }
        catch { return null; }
    }

    private static DevFlowAgentStatus? ParseV1Status(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? readNested(string parent, string child)
                => root.TryGetProperty(parent, out var p)
                   && p.ValueKind == JsonValueKind.Object
                   && p.TryGetProperty(child, out var c)
                   && c.ValueKind == JsonValueKind.String
                    ? c.GetString()
                    : null;

            string? readString(string name)
                => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                    ? v.GetString() : null;

            bool getBool(string name)
                => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

            int getInt(string name)
                => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
                   && v.TryGetInt32(out var n) ? n : 0;

            return new DevFlowAgentStatus
            {
                Agent = readNested("agent", "name") ?? readString("agent"),
                Version = readNested("agent", "version") ?? readString("version"),
                Platform = readString("platform") ?? readNested("device", "platform"),
                DeviceType = readNested("device", "model") ?? readString("deviceType"),
                Idiom = readNested("device", "idiom") ?? readString("idiom"),
                AppName = readNested("app", "name") ?? readString("appName"),
                Running = getBool("running"),
                CdpReady = getBool("cdpReady"),
                CdpWebViewCount = getInt("cdpWebViewCount"),
            };
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<DevFlowElementInfo>> GetTreeAsync(int maxDepth = 0, int? window = null, CancellationToken ct = default)
    {
        var parts = new List<string>();
        if (maxDepth > 0) parts.Add($"depth={maxDepth}");
        if (window != null) parts.Add($"window={window}");
        var basePath = V("/api/v1/ui/tree", "/api/tree");
        var url = parts.Count > 0 ? $"{basePath}?{string.Join("&", parts)}" : basePath;
        return await GetAsync<List<DevFlowElementInfo>>(url, ct) ?? new();
    }

    public async Task<DevFlowElementInfo?> GetElementAsync(string id, CancellationToken ct = default)
    {
        var path = V($"/api/v1/ui/elements/{Uri.EscapeDataString(id)}", $"/api/element/{Uri.EscapeDataString(id)}");
        return await GetAsync<DevFlowElementInfo>(path, ct);
    }

    public async Task<List<DevFlowElementInfo>> QueryAsync(
        string? type = null, string? automationId = null, string? text = null, string? selector = null, CancellationToken ct = default)
    {
        var parts = new List<string>();
        if (type != null) parts.Add($"type={Uri.EscapeDataString(type)}");
        if (automationId != null) parts.Add($"automationId={Uri.EscapeDataString(automationId)}");
        if (text != null) parts.Add($"text={Uri.EscapeDataString(text)}");
        if (selector != null) parts.Add($"selector={Uri.EscapeDataString(selector)}");
        var basePath = V("/api/v1/ui/elements", "/api/query");
        var url = $"{basePath}?{string.Join("&", parts)}";
        return await GetAsync<List<DevFlowElementInfo>>(url, ct) ?? new();
    }

    public async Task<string?> GetPropertyAsync(string elementId, string propertyName, CancellationToken ct = default)
    {
        try
        {
            var path = V(
                $"/api/v1/ui/elements/{Uri.EscapeDataString(elementId)}/properties/{Uri.EscapeDataString(propertyName)}",
                $"/api/property/{Uri.EscapeDataString(elementId)}/{Uri.EscapeDataString(propertyName)}");
            var json = await _http.GetStringAsync($"{BaseUrl}{path}", ct);
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
            var path = V(
                $"/api/v1/ui/elements/{Uri.EscapeDataString(elementId)}/properties/{Uri.EscapeDataString(propertyName)}",
                $"/api/property/{Uri.EscapeDataString(elementId)}/{Uri.EscapeDataString(propertyName)}");
            var response = await _http.PostAsync($"{BaseUrl}{path}", content, ct);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<byte[]?> GetScreenshotAsync(int? window = null, string? elementId = null, CancellationToken ct = default)
    {
        try
        {
            var parts = new List<string>();
            if (_useV1)
            {
                // v1 query parameter is `elementId` and there is no `window` parameter.
                if (elementId != null) parts.Add($"elementId={Uri.EscapeDataString(elementId)}");
            }
            else
            {
                if (window != null) parts.Add($"window={window}");
                if (elementId != null) parts.Add($"id={Uri.EscapeDataString(elementId)}");
            }
            var basePath = V("/api/v1/ui/screenshot", "/api/screenshot");
            var url = parts.Count > 0
                ? $"{BaseUrl}{basePath}?{string.Join("&", parts)}"
                : $"{BaseUrl}{basePath}";
            var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadAsByteArrayAsync(ct);
        }
        catch { return null; }
    }

    public async Task<bool> TapAsync(string elementId, CancellationToken ct = default)
        => await PostActionAsync(V("/api/v1/ui/actions/tap", "/api/action/tap"), new { elementId }, ct);

    public async Task<bool> FillAsync(string elementId, string text, CancellationToken ct = default)
        => await PostActionAsync(V("/api/v1/ui/actions/fill", "/api/action/fill"), new { elementId, text }, ct);

    public async Task<bool> FocusAsync(string elementId, CancellationToken ct = default)
        => await PostActionAsync(V("/api/v1/ui/actions/focus", "/api/action/focus"), new { elementId }, ct);

    // --- Hit Test ---

    public async Task<DevFlowHitTestResult?> HitTestAsync(double x, double y, int? window = null, CancellationToken ct = default)
    {
        var parts = new List<string> { $"x={x}", $"y={y}" };
        if (window != null) parts.Add($"window={window}");
        var basePath = V("/api/v1/ui/hit-test", "/api/hittest");
        var url = $"{basePath}?{string.Join("&", parts)}";
        return await GetAsync<DevFlowHitTestResult>(url, ct);
    }

    // --- Logs ---

    public async Task<List<DevFlowLogEntry>> GetLogsAsync(int limit = 100, int skip = 0, string? source = null, CancellationToken ct = default)
    {
        var parts = new List<string> { $"limit={limit}" };
        if (skip > 0) parts.Add($"skip={skip}");
        if (source != null) parts.Add($"source={Uri.EscapeDataString(source)}");
        var basePath = V("/api/v1/logs", "/api/logs");
        var url = $"{basePath}?{string.Join("&", parts)}";
        return await GetAsync<List<DevFlowLogEntry>>(url, ct) ?? new();
    }

    // --- Profiling ---

    public async Task<DevFlowProfilerCapabilities?> GetProfilerCapabilitiesAsync(CancellationToken ct = default)
    {
        return await GetAsync<DevFlowProfilerCapabilities>(
            V("/api/v1/profiler/capabilities", "/api/profiler/capabilities"), ct);
    }

    public async Task<DevFlowProfilerStartResponse?> StartProfilerAsync(int? sampleIntervalMs = null, CancellationToken ct = default)
    {
        // Legacy: POST /api/profiler/start returns { session, capabilities }.
        // v1: POST /api/v1/profiler/sessions returns the session directly. We track the
        // session id internally so stop/samples can target it.
        if (!_useV1)
        {
            object body = sampleIntervalMs.HasValue ? new { sampleIntervalMs = sampleIntervalMs.Value } : (object)new { };
            var legacyResp = await PostAsync<DevFlowProfilerStartResponse>("/api/profiler/start", body, ct);
            if (legacyResp?.Session?.SessionId is { Length: > 0 } id)
                _currentProfilerSessionId = id;
            return legacyResp;
        }

        try
        {
            object? body = sampleIntervalMs.HasValue ? new { sampleIntervalMs = sampleIntervalMs.Value } : new { };
            var json = JsonSerializer.Serialize(body);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{BaseUrl}/api/v1/profiler/sessions", content, ct);
            if (!response.IsSuccessStatusCode) return null;

            var responseBody = await response.Content.ReadAsStringAsync(ct);
            var session = JsonSerializer.Deserialize<DevFlowProfilerSessionInfo>(responseBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (session != null && !string.IsNullOrEmpty(session.SessionId))
                _currentProfilerSessionId = session.SessionId;

            var caps = await GetProfilerCapabilitiesAsync(ct);

            return new DevFlowProfilerStartResponse
            {
                Session = session,
                Capabilities = caps,
            };
        }
        catch { return null; }
    }

    public async Task<DevFlowProfilerStopResponse?> StopProfilerAsync(CancellationToken ct = default)
    {
        if (!_useV1)
        {
            var resp = await PostAsync<DevFlowProfilerStopResponse>("/api/profiler/stop", new { }, ct);
            _currentProfilerSessionId = null;
            return resp;
        }

        var sessionId = _currentProfilerSessionId;
        if (string.IsNullOrEmpty(sessionId)) return null;

        try
        {
            var response = await _http.DeleteAsync($"{BaseUrl}/api/v1/profiler/sessions/{Uri.EscapeDataString(sessionId)}", ct);
            if (!response.IsSuccessStatusCode) return null;

            DevFlowProfilerSessionInfo? session = null;
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            if (!string.IsNullOrWhiteSpace(responseBody))
            {
                try
                {
                    session = JsonSerializer.Deserialize<DevFlowProfilerSessionInfo>(responseBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch { }
            }

            _currentProfilerSessionId = null;

            return new DevFlowProfilerStopResponse
            {
                Session = session ?? new DevFlowProfilerSessionInfo { SessionId = sessionId, IsActive = false },
                StoppedAtUtc = DateTimeOffset.UtcNow,
            };
        }
        catch { return null; }
    }

    public async Task<DevFlowProfilerBatch?> GetProfilerSamplesAsync(
        long sampleCursor = 0,
        long markerCursor = 0,
        long spanCursor = 0,
        int limit = 200,
        CancellationToken ct = default)
    {
        var safeLimit = Math.Clamp(limit, 1, 5000);
        string url;
        if (_useV1)
        {
            var sessionId = _currentProfilerSessionId;
            if (string.IsNullOrEmpty(sessionId)) return null;
            url = $"/api/v1/profiler/sessions/{Uri.EscapeDataString(sessionId)}/samples?sampleCursor={sampleCursor}&markerCursor={markerCursor}&spanCursor={spanCursor}&limit={safeLimit}";
        }
        else
        {
            url = $"/api/profiler/samples?sampleCursor={sampleCursor}&markerCursor={markerCursor}&spanCursor={spanCursor}&limit={safeLimit}";
        }
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
        var basePath = V("/api/v1/profiler/hotspots", "/api/profiler/hotspots");
        var url = $"{basePath}?limit={limit}&minDurationMs={minDurationMs}";
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

        return await PostActionAsync(
            V("/api/v1/profiler/markers", "/api/profiler/marker"),
            new { name, type, payloadJson }, ct);
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
            var path = V("/api/v1/webview/evaluate", "/api/cdp");
            var response = await _http.PostAsync($"{BaseUrl}{path}", content, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<CdpResponse>(responseBody);
        }
        catch { return null; }
    }

    public async Task<List<CdpTarget>> GetCdpTargetsAsync(CancellationToken ct = default)
    {
        return await GetAsync<List<CdpTarget>>(V("/api/v1/webview/contexts", "/api/cdp/targets"), ct) ?? new();
    }

    // --- Network ---

    public async Task<List<DevFlowNetworkRequest>> GetNetworkRequestsAsync(CancellationToken ct = default)
    {
        return await GetAsync<List<DevFlowNetworkRequest>>(V("/api/v1/network/requests", "/api/network"), ct) ?? new();
    }

    public async Task<DevFlowNetworkRequest?> GetNetworkRequestDetailAsync(string id, CancellationToken ct = default)
    {
        var path = V($"/api/v1/network/requests/{Uri.EscapeDataString(id)}", $"/api/network/{Uri.EscapeDataString(id)}");
        return await GetAsync<DevFlowNetworkRequest>(path, ct);
    }

    public async Task<bool> ClearNetworkRequestsAsync(CancellationToken ct = default)
    {
        try
        {
            // v1: DELETE /api/v1/network/requests   |   legacy: POST /api/network/clear
            HttpResponseMessage response = _useV1
                ? await _http.DeleteAsync($"{BaseUrl}/api/v1/network/requests", ct)
                : await _http.PostAsync($"{BaseUrl}/api/network/clear", null, ct);
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
            var wsUrl = _useV1
                ? $"ws://{AgentHost}:{AgentPort}/ws/v1/network"
                : $"ws://{AgentHost}:{AgentPort}/ws/network";
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
            var wsBase = _useV1
                ? $"ws://{AgentHost}:{AgentPort}/ws/v1/logs"
                : $"ws://{AgentHost}:{AgentPort}/ws/logs";
            var wsUrl = $"{wsBase}?{query}";
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

    // --- Platform Info ---

    public async Task<DevFlowAppInfo?> GetAppInfoAsync(CancellationToken ct = default)
        => await GetAsync<DevFlowAppInfo>(V("/api/v1/device/app", "/api/platform/app-info"), ct);

    public async Task<DevFlowDeviceInfo?> GetDeviceInfoAsync(CancellationToken ct = default)
        => await GetAsync<DevFlowDeviceInfo>(V("/api/v1/device/info", "/api/platform/device-info"), ct);

    public async Task<DevFlowDisplayInfo?> GetDisplayInfoAsync(CancellationToken ct = default)
        => await GetAsync<DevFlowDisplayInfo>(V("/api/v1/device/display", "/api/platform/device-display"), ct);

    public async Task<DevFlowBatteryInfo?> GetBatteryInfoAsync(CancellationToken ct = default)
        => await GetAsync<DevFlowBatteryInfo>(V("/api/v1/device/battery", "/api/platform/battery"), ct);

    public async Task<DevFlowConnectivityInfo?> GetConnectivityAsync(CancellationToken ct = default)
        => await GetAsync<DevFlowConnectivityInfo>(V("/api/v1/device/connectivity", "/api/platform/connectivity"), ct);

    public async Task<DevFlowVersionTracking?> GetVersionTrackingAsync(CancellationToken ct = default)
        => await GetAsync<DevFlowVersionTracking>(V("/api/v1/device/version-tracking", "/api/platform/version-tracking"), ct);

    public async Task<List<DevFlowPermissionStatus>> GetPermissionsAsync(CancellationToken ct = default)
    {
        var result = await GetAsync<DevFlowPermissionsResponse>(V("/api/v1/device/permissions", "/api/platform/permissions"), ct);
        return result?.Permissions ?? new();
    }

    public async Task<DevFlowPermissionStatus?> CheckPermissionAsync(string permission, CancellationToken ct = default)
        => await GetAsync<DevFlowPermissionStatus>(
            V($"/api/v1/device/permissions/{Uri.EscapeDataString(permission)}",
              $"/api/platform/permissions/{Uri.EscapeDataString(permission)}"), ct);

    public async Task<DevFlowGeolocation?> GetGeolocationAsync(string accuracy = "Medium", int timeoutSeconds = 10, CancellationToken ct = default)
    {
        var basePath = V("/api/v1/device/geolocation", "/api/platform/geolocation");
        return await GetAsync<DevFlowGeolocation>($"{basePath}?accuracy={Uri.EscapeDataString(accuracy)}&timeout={timeoutSeconds}", ct);
    }

    // --- Preferences ---

    public async Task<List<DevFlowPreferenceEntry>> GetPreferencesAsync(string? sharedName = null, CancellationToken ct = default)
    {
        var query = sharedName != null ? $"?sharedName={Uri.EscapeDataString(sharedName)}" : "";
        var basePath = V("/api/v1/storage/preferences", "/api/preferences");
        try
        {
            var json = await _http.GetStringAsync($"{BaseUrl}{basePath}{query}", ct);
            if (string.IsNullOrWhiteSpace(json)) return new();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            // Bare array (DevFlow v1 reference shape)
            if (root.ValueKind == JsonValueKind.Array)
                return JsonSerializer.Deserialize<List<DevFlowPreferenceEntry>>(root.GetRawText(), opts) ?? new();

            // Wrapped: {keys:[...]} (legacy MAUI DevFlow) or {preferences:[...]} (Ailoha)
            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var name in new[] { "keys", "preferences", "items", "entries" })
                {
                    if (root.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array)
                        return JsonSerializer.Deserialize<List<DevFlowPreferenceEntry>>(arr.GetRawText(), opts) ?? new();
                }
            }
            return new();
        }
        catch { return new(); }
    }

    public async Task<DevFlowPreferenceEntry?> GetPreferenceAsync(string key, string type = "string", string? sharedName = null, CancellationToken ct = default)
    {
        var query = $"?type={Uri.EscapeDataString(type)}";
        if (sharedName != null) query += $"&sharedName={Uri.EscapeDataString(sharedName)}";
        var path = V(
            $"/api/v1/storage/preferences/{Uri.EscapeDataString(key)}",
            $"/api/preferences/{Uri.EscapeDataString(key)}");
        return await GetAsync<DevFlowPreferenceEntry>($"{path}{query}", ct);
    }

    public async Task<bool> SetPreferenceAsync(string key, object? value, string? type = null, string? sharedName = null, CancellationToken ct = default)
    {
        var body = new DevFlowPreferenceSetRequest { Value = value, Type = type ?? "string", SharedName = sharedName };
        var path = V(
            $"/api/v1/storage/preferences/{Uri.EscapeDataString(key)}",
            $"/api/preferences/{Uri.EscapeDataString(key)}");
        // v1 uses PUT for set; legacy uses POST.
        return _useV1
            ? await PutAsync(path, body, ct)
            : await PostAsync<DevFlowPreferenceEntry>(path, body, ct) != null;
    }

    public async Task<bool> DeletePreferenceAsync(string key, string? sharedName = null, CancellationToken ct = default)
    {
        var query = sharedName != null ? $"?sharedName={Uri.EscapeDataString(sharedName)}" : "";
        var path = V(
            $"/api/v1/storage/preferences/{Uri.EscapeDataString(key)}",
            $"/api/preferences/{Uri.EscapeDataString(key)}");
        return await DeleteAsync($"{path}{query}", ct);
    }

    public async Task<bool> ClearPreferencesAsync(string? sharedName = null, CancellationToken ct = default)
    {
        var query = sharedName != null ? $"?sharedName={Uri.EscapeDataString(sharedName)}" : "";
        try
        {
            // v1: DELETE collection. Legacy: POST /api/preferences/clear.
            HttpResponseMessage response = _useV1
                ? await _http.DeleteAsync($"{BaseUrl}/api/v1/storage/preferences{query}", ct)
                : await _http.PostAsync($"{BaseUrl}/api/preferences/clear{query}", null, ct);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // --- Secure Storage ---

    public async Task<DevFlowSecureStorageEntry?> GetSecureStorageAsync(string key, CancellationToken ct = default)
        => await GetAsync<DevFlowSecureStorageEntry>(
            V($"/api/v1/storage/secure/{Uri.EscapeDataString(key)}",
              $"/api/secure-storage/{Uri.EscapeDataString(key)}"), ct);

    public async Task<bool> SetSecureStorageAsync(string key, string value, CancellationToken ct = default)
    {
        var body = new { value };
        var path = V(
            $"/api/v1/storage/secure/{Uri.EscapeDataString(key)}",
            $"/api/secure-storage/{Uri.EscapeDataString(key)}");
        // v1 uses PUT for set; legacy uses POST.
        return _useV1
            ? await PutAsync(path, body, ct)
            : await PostAsync<DevFlowSecureStorageEntry>(path, body, ct) != null;
    }

    public async Task<bool> DeleteSecureStorageAsync(string key, CancellationToken ct = default)
        => await DeleteAsync(
            V($"/api/v1/storage/secure/{Uri.EscapeDataString(key)}",
              $"/api/secure-storage/{Uri.EscapeDataString(key)}"), ct);

    public async Task<bool> ClearSecureStorageAsync(CancellationToken ct = default)
    {
        try
        {
            HttpResponseMessage response = _useV1
                ? await _http.DeleteAsync($"{BaseUrl}/api/v1/storage/secure", ct)
                : await _http.PostAsync($"{BaseUrl}/api/secure-storage/clear", null, ct);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // --- Sensors ---

    public async Task<List<DevFlowSensorStatus>> GetSensorsAsync(CancellationToken ct = default)
    {
        var basePath = V("/api/v1/device/sensors", "/api/sensors");
        try
        {
            var json = await _http.GetStringAsync($"{BaseUrl}{basePath}", ct);
            if (string.IsNullOrWhiteSpace(json)) return new();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            if (root.ValueKind == JsonValueKind.Array)
                return JsonSerializer.Deserialize<List<DevFlowSensorStatus>>(root.GetRawText(), opts) ?? new();

            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("sensors", out var arr) &&
                arr.ValueKind == JsonValueKind.Array)
            {
                return JsonSerializer.Deserialize<List<DevFlowSensorStatus>>(arr.GetRawText(), opts) ?? new();
            }
            return new();
        }
        catch { return new(); }
    }

    public async Task<bool> StartSensorAsync(string sensor, string speed = "UI", CancellationToken ct = default)
    {
        try
        {
            var path = V(
                $"/api/v1/device/sensors/{Uri.EscapeDataString(sensor)}/start",
                $"/api/sensors/{Uri.EscapeDataString(sensor)}/start");
            var response = await _http.PostAsync($"{BaseUrl}{path}?speed={Uri.EscapeDataString(speed)}", null, ct);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> StopSensorAsync(string sensor, CancellationToken ct = default)
    {
        try
        {
            var path = V(
                $"/api/v1/device/sensors/{Uri.EscapeDataString(sensor)}/stop",
                $"/api/sensors/{Uri.EscapeDataString(sensor)}/stop");
            var response = await _http.PostAsync($"{BaseUrl}{path}", null, ct);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task StreamSensorAsync(string sensor, Action<DevFlowSensorReading> onReading, string speed = "UI", int throttleMs = 100, CancellationToken ct = default)
    {
        StopSensorStream(sensor);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var ws = new ClientWebSocket();
        lock (_sensorStreams) { _sensorStreams[sensor] = (ws, cts); }

        var token = cts.Token;
        try
        {
            var wsBase = _useV1
                ? $"ws://{AgentHost}:{AgentPort}/ws/v1/device/sensors"
                : $"ws://{AgentHost}:{AgentPort}/ws/sensors";
            var wsUrl = $"{wsBase}?sensor={Uri.EscapeDataString(sensor)}&speed={Uri.EscapeDataString(speed)}&throttleMs={throttleMs}";
            await ws.ConnectAsync(new Uri(wsUrl), token);

            var buffer = new byte[16 * 1024];
            var sb = new StringBuilder();

            while (ws.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(buffer, token);
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
                        var root = doc.RootElement;
                        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

                        // v1 envelope: {type:"reading", timestamp, reading:{sensor, timestamp, values}}
                        if (root.ValueKind == JsonValueKind.Object &&
                            root.TryGetProperty("type", out var typeProp) &&
                            typeProp.GetString() == "reading" &&
                            root.TryGetProperty("reading", out var readingProp))
                        {
                            var reading = JsonSerializer.Deserialize<DevFlowSensorReading>(readingProp.GetRawText(), opts);
                            if (reading != null) onReading(reading);
                        }
                        else
                        {
                            // Legacy: bare reading object
                            var reading = JsonSerializer.Deserialize<DevFlowSensorReading>(json, opts);
                            if (reading != null && (!string.IsNullOrEmpty(reading.Sensor) || reading.Data.ValueKind != JsonValueKind.Undefined))
                                onReading(reading);
                        }
                    }
                    catch { }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            if (ws.State == WebSocketState.Open)
            {
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None); }
                catch { }
            }
            lock (_sensorStreams) { _sensorStreams.Remove(sensor); }
        }
    }

    public void StopSensorStream(string sensor)
    {
        lock (_sensorStreams)
        {
            if (_sensorStreams.Remove(sensor, out var entry))
            {
                entry.Cts.Cancel();
                entry.Ws.Dispose();
            }
        }
    }

    public void StopAllSensorStreams()
    {
        lock (_sensorStreams)
        {
            foreach (var entry in _sensorStreams.Values)
            {
                entry.Cts.Cancel();
                entry.Ws.Dispose();
            }
            _sensorStreams.Clear();
        }
    }

    public bool IsSensorStreaming(string sensor)
    {
        lock (_sensorStreams) { return _sensorStreams.ContainsKey(sensor); }
    }

    public int StreamingSensorCount
    {
        get { lock (_sensorStreams) { return _sensorStreams.Count; } }
    }

    // --- Helpers ---

    /// <summary>Pick the v1 or legacy URL based on the detected protocol.</summary>
    private string V(string v1Path, string legacyPath) => _useV1 ? v1Path : legacyPath;

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

    private async Task<bool> DeleteAsync(string path, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.DeleteAsync($"{BaseUrl}{path}", ct);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private async Task<bool> PutAsync(string path, object body, CancellationToken ct = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(body);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _http.PutAsync($"{BaseUrl}{path}", content, ct);
            return response.IsSuccessStatusCode;
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
        StopAllSensorStreams();
        _http.Dispose();
    }
}

/// <summary>Protocol exposed by a MAUI DevFlow / Ailoha agent.</summary>
public enum DevFlowAgentProtocol
{
    /// <summary>Modern <c>/api/v1/*</c> + <c>/ws/v1/*</c> surface (DevFlow preview.7+, Ailoha).</summary>
    V1,
    /// <summary>Legacy <c>/api/*</c> + <c>/ws/*</c> surface (DevFlow preview.6 and earlier).</summary>
    Legacy,
}
