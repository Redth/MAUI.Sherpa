using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.DevFlow;
using MauiSherpa.Core.Models.Inspector;
using Microsoft.Extensions.Logging;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Adapter that wraps the legacy <see cref="DevFlowAgentClient"/> behind the
/// <see cref="IAppInspectorClient"/> interface. Maps old models to common ones.
/// When the legacy API is sunsetted, delete this class and DevFlowAgentClient.
/// </summary>
public class DevFlowLegacyClient : IAppInspectorClient
{
    private readonly DevFlowAgentClient _legacy;
    private readonly ILogger _logger;

    public InspectorProtocolVersion ProtocolVersion => InspectorProtocolVersion.Legacy;
    public string Host { get; }
    public int Port { get; }

    public DevFlowLegacyClient(string host, int port, IHttpClientFactory httpClientFactory, ILogger logger)
    {
        Host = host;
        Port = port;
        _logger = logger;
        _legacy = new DevFlowAgentClient(host, port);
    }

    // ─────────── Mapping Helpers ────────────

    private static InspectorElement MapElement(DevFlowElementInfo e) => new()
    {
        Id = e.Id,
        ParentId = e.ParentId,
        Type = e.Type,
        FullType = e.FullType,
        Framework = null, // Legacy doesn't report framework
        AutomationId = e.AutomationId,
        Text = e.Text,
        Value = null, // Not available in legacy
        Role = null, // Not available in legacy
        Traits = null, // Not available in legacy
        State = new ElementState
        {
            Displayed = e.IsVisible,
            Enabled = e.IsEnabled,
            Selected = false,
            Focused = e.IsFocused,
            Opacity = e.Opacity
        },
        Bounds = e.Bounds != null ? new BoundsInfo
        {
            X = e.Bounds.X,
            Y = e.Bounds.Y,
            Width = e.Bounds.Width,
            Height = e.Bounds.Height,
            CoordinateSystem = null
        } : null,
        Gestures = e.Gestures?.AsReadOnly(),
        Style = null,
        NativeView = e.NativeType != null ? new NativeViewInfo
        {
            Type = e.NativeType,
            Properties = e.NativeProperties?.ToDictionary(kv => kv.Key, kv => (object)(kv.Value ?? ""))
        } : null,
        FrameworkProperties = null,
        Children = e.Children?.Select(MapElement).ToList()?.AsReadOnly()
    };

    private static InspectorElement MapHitTestElement(DevFlowHitTestElement e) => new()
    {
        Id = e.Id,
        Type = e.Type ?? string.Empty,
        FullType = string.Empty,
        AutomationId = e.AutomationId,
        Text = e.Text,
        State = new ElementState(),
        Bounds = e.Bounds != null ? new BoundsInfo
        {
            X = e.Bounds.X,
            Y = e.Bounds.Y,
            Width = e.Bounds.Width,
            Height = e.Bounds.Height
        } : null,
    };

    private static InspectorNetworkRequest MapNetworkRequest(DevFlowNetworkRequest r) => new()
    {
        Id = r.Id,
        Timestamp = r.Timestamp,
        Method = r.Method,
        Url = r.Url,
        Host = r.Host,
        Path = r.Path,
        StatusCode = r.StatusCode,
        StatusText = r.StatusText,
        DurationMs = r.DurationMs,
        Error = r.Error,
        RequestContentType = r.RequestContentType,
        ResponseContentType = r.ResponseContentType,
        RequestSize = r.RequestSize,
        ResponseSize = r.ResponseSize
    };

    private static InspectorLogEntry MapLogEntry(DevFlowLogEntry e) => new()
    {
        Timestamp = e.Timestamp,
        Level = e.Level ?? "info",
        Category = e.Category ?? string.Empty,
        Message = e.Message ?? string.Empty,
        Exception = e.Exception,
        Source = e.Source ?? "framework"
    };

    // ─────────── Agent ────────────

    public async Task<InspectorAgentStatus> GetAgentStatusAsync(CancellationToken ct = default)
    {
        var status = await _legacy.GetStatusAsync(ct);
        return new InspectorAgentStatus
        {
            Agent = new InspectorAgentInfo
            {
                Name = status?.Agent ?? "unknown",
                Version = status?.Version ?? "0.0.0",
                Framework = "maui", // Legacy was MAUI-only
                FrameworkVersion = string.Empty
            },
            Platform = status?.Platform ?? string.Empty,
            Device = new InspectorDeviceStatus
            {
                Model = string.Empty,
                Manufacturer = string.Empty,
                OsVersion = string.Empty,
                Idiom = status?.Idiom ?? string.Empty
            },
            App = new InspectorAppStatus
            {
                Name = status?.AppName ?? string.Empty,
                Version = string.Empty,
                PackageId = string.Empty
            },
            Running = status?.Running ?? false,
        };
    }

    public Task<InspectorCapabilities> GetCapabilitiesAsync(CancellationToken ct = default)
    {
        // Legacy doesn't have capability discovery. Return a reasonable default for MAUI.
        var caps = new Dictionary<string, CapabilityDetail>
        {
            ["ui.tree"] = new() { Version = 1, Features = ["find", "query"] },
            ["ui.actions"] = new() { Version = 1, Features = ["tap", "fill", "focus"] },
            ["ui.screenshot"] = new() { Version = 1, Features = ["fullPage", "element"] },
            ["profiler"] = new() { Version = 1, Features = ["samples", "spans", "markers", "hotspots"] },
            ["network"] = new() { Version = 1, Features = ["capture", "detail"] },
            ["logs"] = new() { Version = 1, Features = ["stream", "query"] },
            ["device.info"] = new() { Version = 1, Features = ["display", "battery", "connectivity"] },
            ["storage.preferences"] = new() { Version = 1, Features = ["get", "set", "delete"] },
            ["storage.secure"] = new() { Version = 1, Features = ["get", "set", "delete"] },
        };

        return Task.FromResult(new InspectorCapabilities
        {
            Agent = new InspectorAgentInfo { Name = "devflow-legacy", Framework = "maui" },
            Capabilities = caps
        });
    }

    // ─────────── Visual Tree ────────────

    public async Task<IReadOnlyList<InspectorElement>> GetTreeAsync(TreeOptions? options = null, CancellationToken ct = default)
    {
        int windowInt = options?.Window != null && int.TryParse(options.Window, out var w) ? w : 0;
        var tree = await _legacy.GetTreeAsync(options?.Depth ?? 0, windowInt > 0 ? windowInt : null, ct);
        return tree?.Select(MapElement).ToList()?.AsReadOnly() ?? (IReadOnlyList<InspectorElement>)[];
    }

    public async Task<InspectorElement?> GetElementAsync(string elementId, CancellationToken ct = default)
    {
        var element = await _legacy.GetElementAsync(elementId, ct);
        return element != null ? MapElement(element) : null;
    }

    public async Task<IReadOnlyList<InspectorElement>> QueryElementsAsync(ElementQuery query, CancellationToken ct = default)
    {
        // Map v1 locator strategies to legacy query parameters
        string? type = null, automationId = null, text = null, selector = null;
        switch (query.Strategy)
        {
            case "type": type = query.Value; break;
            case "accessibility-id": automationId = query.Value; break;
            case "text": text = query.Value; break;
            case "css-selector": selector = query.Value; break;
        }

        var results = await _legacy.QueryAsync(type, automationId, text, selector, ct);
        return results?.Select(MapElement).ToList()?.AsReadOnly() ?? (IReadOnlyList<InspectorElement>)[];
    }

    public async Task<IReadOnlyList<InspectorElement>> HitTestAsync(double x, double y, string? window = null, CancellationToken ct = default)
    {
        int? windowInt = window != null && int.TryParse(window, out var w) ? w : null;
        var result = await _legacy.HitTestAsync(x, y, windowInt, ct);
        return result?.Elements?.Select(MapHitTestElement).ToList()?.AsReadOnly() ?? (IReadOnlyList<InspectorElement>)[];
    }

    // ─────────── Screenshots ────────────

    public Task<byte[]?> GetScreenshotAsync(ScreenshotOptions? options = null, CancellationToken ct = default)
    {
        int? windowInt = options?.Window != null && int.TryParse(options.Window, out var w) ? w : null;
        return _legacy.GetScreenshotAsync(windowInt, options?.ElementId, ct);
    }

    // ─────────── Properties ────────────

    public async Task<object?> GetPropertyAsync(string elementId, string propertyName, CancellationToken ct = default)
        => await _legacy.GetPropertyAsync(elementId, propertyName, ct);

    public async Task<object?> SetPropertyAsync(string elementId, string propertyName, object value, CancellationToken ct = default)
    {
        var result = await _legacy.SetPropertyAsync(elementId, propertyName, value?.ToString() ?? string.Empty, ct);
        return result ? value : null;
    }

    // ─────────── Actions ────────────

    public async Task<ActionResult> TapAsync(TapRequest request, CancellationToken ct = default)
    {
        try
        {
            if (request.ElementId != null)
                await _legacy.TapAsync(request.ElementId, ct);
            return new ActionResult { Success = true };
        }
        catch (Exception ex)
        {
            return new ActionResult { Success = false, Error = new InspectorError { Title = ex.Message, Status = 500 } };
        }
    }

    public async Task<ActionResult> FillAsync(FillRequest request, CancellationToken ct = default)
    {
        try
        {
            await _legacy.FillAsync(request.ElementId, request.Text, ct);
            return new ActionResult { Success = true };
        }
        catch (Exception ex)
        {
            return new ActionResult { Success = false, Error = new InspectorError { Title = ex.Message, Status = 500 } };
        }
    }

    public async Task<ActionResult> FocusAsync(FocusRequest request, CancellationToken ct = default)
    {
        try
        {
            await _legacy.FocusAsync(request.ElementId, ct);
            return new ActionResult { Success = true };
        }
        catch (Exception ex)
        {
            return new ActionResult { Success = false, Error = new InspectorError { Title = ex.Message, Status = 500 } };
        }
    }

    // These actions don't exist in legacy — return unsupported
    public Task<ActionResult> ClearAsync(ClearRequest request, CancellationToken ct = default)
        => Task.FromResult(new ActionResult { Success = false, Error = new InspectorError { Title = "Clear not supported in legacy protocol", Status = 501, ErrorCode = "unsupported-capability" } });

    public Task<ActionResult> ScrollAsync(ScrollRequest request, CancellationToken ct = default)
        => Task.FromResult(new ActionResult { Success = false, Error = new InspectorError { Title = "Scroll not supported in legacy protocol", Status = 501, ErrorCode = "unsupported-capability" } });

    public Task<ActionResult> NavigateAsync(NavigateRequest request, CancellationToken ct = default)
        => Task.FromResult(new ActionResult { Success = false, Error = new InspectorError { Title = "Navigate not supported in legacy protocol", Status = 501, ErrorCode = "unsupported-capability" } });

    public Task<ActionResult> ResizeAsync(ResizeRequest request, CancellationToken ct = default)
        => Task.FromResult(new ActionResult { Success = false, Error = new InspectorError { Title = "Resize not supported in legacy protocol", Status = 501, ErrorCode = "unsupported-capability" } });

    public Task<ActionResult> BackAsync(BackRequest request, CancellationToken ct = default)
        => Task.FromResult(new ActionResult { Success = false, Error = new InspectorError { Title = "Back not supported in legacy protocol", Status = 501, ErrorCode = "unsupported-capability" } });

    public Task<ActionResult> KeyAsync(KeyRequest request, CancellationToken ct = default)
        => Task.FromResult(new ActionResult { Success = false, Error = new InspectorError { Title = "Key not supported in legacy protocol", Status = 501, ErrorCode = "unsupported-capability" } });

    public Task<ActionResult> GestureAsync(GestureRequest request, CancellationToken ct = default)
        => Task.FromResult(new ActionResult { Success = false, Error = new InspectorError { Title = "Gesture not supported in legacy protocol", Status = 501, ErrorCode = "unsupported-capability" } });

    public Task<ActionResult> BatchAsync(BatchRequest request, CancellationToken ct = default)
        => Task.FromResult(new ActionResult { Success = false, Error = new InspectorError { Title = "Batch not supported in legacy protocol", Status = 501, ErrorCode = "unsupported-capability" } });

    // ─────────── WebView ────────────

    public async Task<IReadOnlyList<InspectorWebViewContext>> GetWebViewContextsAsync(CancellationToken ct = default)
    {
        var targets = await _legacy.GetCdpTargetsAsync(ct);
        return targets?.Select(t => new InspectorWebViewContext
        {
            Id = t.Id,
            Url = t.Url ?? string.Empty,
            Title = t.Title,
            Ready = t.Ready
        }).ToList()?.AsReadOnly() ?? (IReadOnlyList<InspectorWebViewContext>)[];
    }

    public async Task<WebViewEvalResult> EvaluateJavaScriptAsync(string expression, string? contextId = null, CancellationToken ct = default)
    {
        var response = await _legacy.SendCdpCommandAsync("Runtime.evaluate",
            new Dictionary<string, object> { ["expression"] = expression, ["returnByValue"] = true },
            contextId, ct);
        return new WebViewEvalResult { Result = response?.Result };
    }

    public Task<object?> GetWebViewDomAsync(string? contextId = null, CancellationToken ct = default)
        => Task.FromResult<object?>(null); // Not available in legacy

    public Task<IReadOnlyList<object>> QueryWebViewDomAsync(string selector, string? contextId = null, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<object>>([]);

    public Task<string?> GetWebViewSourceAsync(string? contextId = null, CancellationToken ct = default)
        => Task.FromResult<string?>(null);

    public Task<bool> NavigateWebViewAsync(string url, string? contextId = null, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task<bool> ClickInWebViewAsync(string selector, string? contextId = null, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task<bool> FillInWebViewAsync(string selector, string text, string? contextId = null, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task<byte[]?> GetWebViewScreenshotAsync(string? contextId = null, CancellationToken ct = default)
        => Task.FromResult<byte[]?>(null);

    // ─────────── Network ────────────

    public async Task<IReadOnlyList<InspectorNetworkRequest>> GetNetworkRequestsAsync(CancellationToken ct = default)
    {
        var requests = await _legacy.GetNetworkRequestsAsync(ct);
        return requests?.Select(MapNetworkRequest).ToList()?.AsReadOnly() ?? (IReadOnlyList<InspectorNetworkRequest>)[];
    }

    public async Task<InspectorNetworkRequestDetail?> GetNetworkRequestDetailAsync(string id, CancellationToken ct = default)
    {
        var detail = await _legacy.GetNetworkRequestDetailAsync(id, ct);
        if (detail == null) return null;
        return new InspectorNetworkRequestDetail
        {
            Id = detail.Id,
            Timestamp = detail.Timestamp,
            Method = detail.Method,
            Url = detail.Url,
            Host = detail.Host,
            Path = detail.Path,
            StatusCode = detail.StatusCode,
            StatusText = detail.StatusText,
            DurationMs = detail.DurationMs,
            Error = detail.Error,
            RequestContentType = detail.RequestContentType,
            ResponseContentType = detail.ResponseContentType,
            RequestSize = detail.RequestSize,
            ResponseSize = detail.ResponseSize,
            RequestHeaders = detail.RequestHeaders?.ToDictionary(kv => kv.Key, kv => string.Join(", ", kv.Value)),
            ResponseHeaders = detail.ResponseHeaders?.ToDictionary(kv => kv.Key, kv => string.Join(", ", kv.Value)),
            RequestBody = detail.RequestBody,
            ResponseBody = detail.ResponseBody,
            RequestBodyEncoding = detail.RequestBodyEncoding,
            ResponseBodyEncoding = detail.ResponseBodyEncoding,
            RequestBodyTruncated = detail.RequestBodyTruncated,
            ResponseBodyTruncated = detail.ResponseBodyTruncated
        };
    }

    public Task ClearNetworkRequestsAsync(CancellationToken ct = default)
        => _legacy.ClearNetworkRequestsAsync(ct);

    public Task StreamNetworkRequestsAsync(
        Action<IReadOnlyList<InspectorNetworkRequest>>? onReplay,
        Action<InspectorNetworkRequest> onRequest,
        CancellationToken ct = default)
        => _legacy.StreamNetworkRequestsAsync(r => onRequest(MapNetworkRequest(r)), ct);

    // ─────────── Logs ────────────

    public async Task<IReadOnlyList<InspectorLogEntry>> GetLogsAsync(LogQuery? query = null, CancellationToken ct = default)
    {
        var logs = await _legacy.GetLogsAsync(query?.Limit ?? 100, query?.Skip ?? 0, query?.Source, ct);
        return logs?.Select(MapLogEntry).ToList()?.AsReadOnly() ?? (IReadOnlyList<InspectorLogEntry>)[];
    }

    public Task StreamLogsAsync(
        Action<IReadOnlyList<InspectorLogEntry>>? onReplay,
        Action<InspectorLogEntry> onEntry,
        LogStreamOptions? options = null,
        CancellationToken ct = default)
        => _legacy.StreamLogsAsync(
            replay => onReplay?.Invoke(replay.Select(MapLogEntry).ToList()),
            entry => onEntry(MapLogEntry(entry)),
            options?.Source, options?.Replay ?? 100, ct);

    // ─────────── Profiler ────────────

    public async Task<InspectorProfilerCapabilities> GetProfilerCapabilitiesAsync(CancellationToken ct = default)
    {
        var caps = await _legacy.GetProfilerCapabilitiesAsync(ct);
        return new InspectorProfilerCapabilities
        {
            Platform = caps?.Platform ?? string.Empty,
            ManagedMemorySupported = caps?.ManagedMemorySupported ?? false,
            NativeMemorySupported = caps?.NativeMemorySupported ?? false,
            GcSupported = caps?.GcSupported ?? false,
            CpuPercentSupported = caps?.CpuPercentSupported ?? false,
            FpsSupported = caps?.FpsSupported ?? false,
            FrameTimingsEstimated = caps?.FrameTimingsEstimated ?? false,
            NativeFrameTimingsSupported = caps?.NativeFrameTimingsSupported ?? false,
            JankEventsSupported = caps?.JankEventsSupported ?? false,
            UiThreadStallSupported = caps?.UiThreadStallSupported ?? false,
            ThreadCountSupported = caps?.ThreadCountSupported ?? false
        };
    }

    public Task<IReadOnlyList<InspectorProfilerSession>> GetProfilerSessionsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<InspectorProfilerSession>>([]); // Legacy doesn't list sessions

    public async Task<InspectorProfilerSession> StartProfilingAsync(int? sampleIntervalMs = null, CancellationToken ct = default)
    {
        var result = await _legacy.StartProfilerAsync(sampleIntervalMs, ct);
        return new InspectorProfilerSession
        {
            SessionId = result?.Session?.SessionId ?? "legacy",
            StartedAtUtc = result?.Session?.StartedAtUtc ?? DateTimeOffset.UtcNow,
            SampleIntervalMs = result?.Session?.SampleIntervalMs ?? sampleIntervalMs ?? 1000,
            IsActive = true
        };
    }

    public Task StopProfilingAsync(string sessionId, CancellationToken ct = default)
        => _legacy.StopProfilerAsync(ct);

    public async Task<InspectorProfilerBatch> GetProfilerSamplesAsync(string sessionId, int? sampleCursor = null, int? markerCursor = null, int? spanCursor = null, int? limit = null, CancellationToken ct = default)
    {
        var batch = await _legacy.GetProfilerSamplesAsync(
            sampleCursor ?? 0, markerCursor ?? 0, spanCursor ?? 0, limit ?? 1000, ct);

        return new InspectorProfilerBatch
        {
            SessionId = batch?.SessionId ?? sessionId,
            Samples = batch?.Samples?.Select(s => new InspectorProfilerSample
            {
                TsUtc = s.TsUtc,
                Fps = s.Fps,
                FrameTimeMsP50 = s.FrameTimeMsP50,
                FrameTimeMsP95 = s.FrameTimeMsP95,
                WorstFrameTimeMs = s.WorstFrameTimeMs,
                ManagedBytes = s.ManagedBytes,
                Gc0 = s.Gc0,
                Gc1 = s.Gc1,
                Gc2 = s.Gc2,
                NativeMemoryBytes = s.NativeMemoryBytes,
                NativeMemoryKind = s.NativeMemoryKind,
                CpuPercent = s.CpuPercent,
                ThreadCount = s.ThreadCount,
                JankFrameCount = s.JankFrameCount,
                UiThreadStallCount = s.UiThreadStallCount,
                FrameSource = s.FrameSource,
                FrameQuality = s.FrameQuality
            }).ToList()?.AsReadOnly() ?? (IReadOnlyList<InspectorProfilerSample>)[],
            Markers = batch?.Markers?.Select(m => new InspectorProfilerMarker
            {
                TsUtc = m.TsUtc,
                Type = m.Type,
                Name = m.Name,
                PayloadJson = m.PayloadJson
            }).ToList()?.AsReadOnly() ?? (IReadOnlyList<InspectorProfilerMarker>)[],
            Spans = batch?.Spans?.Select(s => new InspectorProfilerSpan
            {
                SpanId = s.SpanId,
                ParentSpanId = s.ParentSpanId,
                TraceId = s.TraceId,
                StartTsUtc = s.StartTsUtc,
                EndTsUtc = s.EndTsUtc,
                DurationMs = s.DurationMs,
                Kind = s.Kind,
                Name = s.Name,
                Status = s.Status,
                ThreadId = s.ThreadId?.ToString(),
                Screen = s.Screen,
                ElementPath = s.ElementPath,
                TagsJson = s.TagsJson,
                Error = s.Error
            }).ToList()?.AsReadOnly() ?? (IReadOnlyList<InspectorProfilerSpan>)[],
            SampleCursor = (int)Math.Min(batch?.SampleCursor ?? 0, int.MaxValue),
            MarkerCursor = (int)Math.Min(batch?.MarkerCursor ?? 0, int.MaxValue),
            SpanCursor = (int)Math.Min(batch?.SpanCursor ?? 0, int.MaxValue),
            IsActive = batch?.IsActive ?? false
        };
    }

    public async Task<IReadOnlyList<InspectorProfilerHotspot>> GetProfilerHotspotsAsync(int? limit = null, double? minDurationMs = null, string? kind = null, CancellationToken ct = default)
    {
        var hotspots = await _legacy.GetProfilerHotspotsAsync(limit ?? 20, (int)(minDurationMs ?? 16), kind, ct);
        return hotspots?.Select(h => new InspectorProfilerHotspot
        {
            Kind = h.Kind,
            Name = h.Name,
            Screen = h.Screen,
            Count = h.Count,
            ErrorCount = h.ErrorCount,
            AvgDurationMs = h.AvgDurationMs,
            P95DurationMs = h.P95DurationMs,
            MaxDurationMs = h.MaxDurationMs
        }).ToList()?.AsReadOnly() ?? (IReadOnlyList<InspectorProfilerHotspot>)[];
    }

    public Task<IReadOnlyList<InspectorProfilerMarker>> GetProfilerMarkersAsync(int? sampleCursor = null, int? limit = null, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<InspectorProfilerMarker>>([]); // Legacy doesn't have separate marker endpoint

    public Task<IReadOnlyList<InspectorProfilerSpan>> GetProfilerSpansAsync(int? spanCursor = null, int? limit = null, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<InspectorProfilerSpan>>([]); // Legacy doesn't have separate span endpoint

    // ─────────── Device ────────────

    public async Task<InspectorDeviceInfo> GetDeviceInfoAsync(CancellationToken ct = default)
    {
        var info = await _legacy.GetDeviceInfoAsync(ct);
        return new InspectorDeviceInfo
        {
            Model = info?.Model ?? string.Empty,
            Manufacturer = info?.Manufacturer ?? string.Empty,
            OsVersion = info?.OsVersion ?? string.Empty,
            Platform = info?.Platform ?? string.Empty,
            Idiom = info?.Idiom ?? string.Empty,
            Architecture = null
        };
    }

    public async Task<InspectorDisplayInfo> GetDisplayInfoAsync(CancellationToken ct = default)
    {
        var info = await _legacy.GetDisplayInfoAsync(ct);
        return new InspectorDisplayInfo
        {
            Width = info?.Width ?? 0,
            Height = info?.Height ?? 0,
            Density = info?.Density ?? 1,
            Orientation = info?.Orientation ?? "portrait",
            RefreshRate = info?.RefreshRate
        };
    }

    public async Task<InspectorBatteryInfo> GetBatteryInfoAsync(CancellationToken ct = default)
    {
        var info = await _legacy.GetBatteryInfoAsync(ct);
        return new InspectorBatteryInfo
        {
            Level = info?.ChargeLevel ?? 0,
            State = info?.State ?? "unknown",
            PowerSource = info?.PowerSource ?? "unknown"
        };
    }

    public async Task<InspectorConnectivityInfo> GetConnectivityAsync(CancellationToken ct = default)
    {
        var info = await _legacy.GetConnectivityAsync(ct);
        return new InspectorConnectivityInfo
        {
            NetworkAccess = info?.NetworkAccess ?? "none",
            ConnectionProfiles = info?.ConnectionProfiles?.AsReadOnly() ?? (IReadOnlyList<string>)[]
        };
    }

    public async Task<InspectorAppInfo> GetAppInfoAsync(CancellationToken ct = default)
    {
        var info = await _legacy.GetAppInfoAsync(ct);
        return new InspectorAppInfo
        {
            Name = info?.Name ?? string.Empty,
            Version = info?.Version ?? string.Empty,
            BuildNumber = info?.BuildNumber ?? string.Empty,
            PackageId = info?.PackageName ?? string.Empty,
            Theme = info?.RequestedTheme
        };
    }

    // ─────────── Sensors ────────────

    public async Task<IReadOnlyList<InspectorSensorInfo>> GetSensorsAsync(CancellationToken ct = default)
    {
        var sensors = await _legacy.GetSensorsAsync(ct);
        return sensors?.Select(s => new InspectorSensorInfo
        {
            Name = s.Sensor,
            Available = s.Supported,
            Active = s.Active
        }).ToList()?.AsReadOnly() ?? (IReadOnlyList<InspectorSensorInfo>)[];
    }

    public Task StartSensorAsync(string sensor, string? speed = null, CancellationToken ct = default)
        => _legacy.StartSensorAsync(sensor, speed, ct);

    public Task StopSensorAsync(string sensor, CancellationToken ct = default)
        => _legacy.StopSensorAsync(sensor, ct);

    public Task StreamSensorAsync(string sensor, Action<InspectorSensorReading> onReading, string? speed = null, int? throttleMs = null, CancellationToken ct = default)
        => _legacy.StreamSensorAsync(sensor, r =>
        {
            // Legacy sensor readings have Data as a JsonElement — flatten to Dictionary<string, object>
            var values = new Dictionary<string, object>();
            if (r.Data.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var prop in r.Data.EnumerateObject())
                    values[prop.Name] = prop.Value.ToString() ?? string.Empty;
            }
            onReading(new InspectorSensorReading
            {
                Sensor = r.Sensor ?? sensor,
                Timestamp = DateTimeOffset.TryParse(r.Timestamp, out var ts) ? ts : DateTimeOffset.UtcNow,
                Values = values
            });
        }, speed ?? "UI", throttleMs ?? 100, ct);

    // ─────────── Permissions & Geolocation ────────────

    public async Task<IReadOnlyList<InspectorPermissionStatus>> GetPermissionsAsync(CancellationToken ct = default)
    {
        var result = await _legacy.GetPermissionsAsync(ct);
        return result?.Select(p => new InspectorPermissionStatus
        {
            Name = p.Permission ?? string.Empty,
            Status = p.Status ?? "unknown"
        }).ToList()?.AsReadOnly() ?? (IReadOnlyList<InspectorPermissionStatus>)[];
    }

    public async Task<InspectorPermissionStatus> CheckPermissionAsync(string permission, CancellationToken ct = default)
    {
        var result = await _legacy.CheckPermissionAsync(permission, ct);
        return new InspectorPermissionStatus
        {
            Name = permission,
            Status = result?.Status ?? "unknown"
        };
    }

    public async Task<InspectorGeolocation> GetGeolocationAsync(string? accuracy = null, int? timeoutSeconds = null, CancellationToken ct = default)
    {
        var result = await _legacy.GetGeolocationAsync(accuracy ?? "Medium", timeoutSeconds ?? 10, ct);
        return new InspectorGeolocation
        {
            Latitude = result?.Latitude ?? 0,
            Longitude = result?.Longitude ?? 0,
            Altitude = result?.Altitude,
            Accuracy = result?.Accuracy ?? 0,
            Timestamp = result?.Timestamp ?? DateTimeOffset.UtcNow
        };
    }

    // ─────────── Storage ────────────

    public async Task<IReadOnlyList<InspectorPreferenceEntry>> GetPreferencesAsync(string? sharedName = null, CancellationToken ct = default)
    {
        var prefs = await _legacy.GetPreferencesAsync(sharedName, ct);
        return prefs?.Select(p => new InspectorPreferenceEntry
        {
            Key = p.Key ?? string.Empty,
            Value = p.Value,
            Type = "string" // Legacy doesn't track types
        }).ToList()?.AsReadOnly() ?? (IReadOnlyList<InspectorPreferenceEntry>)[];
    }

    public async Task<InspectorPreferenceEntry?> GetPreferenceAsync(string key, string? type = null, string? sharedName = null, CancellationToken ct = default)
    {
        var pref = await _legacy.GetPreferenceAsync(key, type ?? "string", sharedName, ct);
        return pref != null ? new InspectorPreferenceEntry { Key = pref.Key ?? key, Value = pref.Value, Type = type ?? "string" } : null;
    }

    public Task SetPreferenceAsync(string key, object value, string? type = null, string? sharedName = null, CancellationToken ct = default)
        => _legacy.SetPreferenceAsync(key, value, type, sharedName, ct);

    public Task DeletePreferenceAsync(string key, string? sharedName = null, CancellationToken ct = default)
        => _legacy.DeletePreferenceAsync(key, sharedName, ct);

    public Task ClearPreferencesAsync(string? sharedName = null, CancellationToken ct = default)
        => _legacy.ClearPreferencesAsync(sharedName, ct);

    public async Task<InspectorSecureStorageEntry?> GetSecureStorageAsync(string key, CancellationToken ct = default)
    {
        var entry = await _legacy.GetSecureStorageAsync(key, ct);
        return entry != null ? new InspectorSecureStorageEntry { Key = key, Value = entry.Value, Exists = entry.Exists } : null;
    }

    public Task SetSecureStorageAsync(string key, string value, CancellationToken ct = default)
        => _legacy.SetSecureStorageAsync(key, value, ct);

    public Task DeleteSecureStorageAsync(string key, CancellationToken ct = default)
        => _legacy.DeleteSecureStorageAsync(key, ct);

    public Task ClearSecureStorageAsync(CancellationToken ct = default)
        => _legacy.ClearSecureStorageAsync(ct);

    public void Dispose()
    {
        _legacy.Dispose();
    }
}
