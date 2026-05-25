using System.Text.Json;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.DevFlow;
using MauiSherpa.Core.Models.Inspector;

namespace MauiSherpa.AppInspector.Services;

public sealed class InspectorUiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IAppInspectorClient _client;
    private readonly Dictionary<string, CancellationTokenSource> _sensorStreams = new(StringComparer.OrdinalIgnoreCase);
    private string? _activeProfilerSessionId;
    private bool _disposed;

    public InspectorUiClient(IAppInspectorClient client)
    {
        _client = client;
    }

    public string BaseUrl => $"http://{_client.Host}:{_client.Port}";

    public int StreamingSensorCount
    {
        get
        {
            lock (_sensorStreams)
                return _sensorStreams.Count;
        }
    }

    public async Task<DevFlowAgentStatus?> GetStatusAsync(CancellationToken ct = default)
    {
        var status = await _client.GetAgentStatusAsync(ct);
        IReadOnlyList<InspectorWebViewContext> webViews = [];
        try
        {
            webViews = await _client.GetWebViewContextsAsync(ct);
        }
        catch
        {
            // WebView support is optional.
        }

        return new DevFlowAgentStatus
        {
            Agent = status.Agent.Name,
            Version = status.Agent.Version,
            Platform = status.Platform,
            DeviceType = status.Device.Model,
            Idiom = status.Device.Idiom,
            AppName = status.App.Name,
            Running = status.Running,
            CdpReady = webViews.Any(w => w.Ready),
            CdpWebViewCount = webViews.Count
        };
    }

    public async Task<List<DevFlowElementInfo>> GetTreeAsync(int maxDepth = 0, int? window = null, CancellationToken ct = default)
    {
        var tree = await _client.GetTreeAsync(new TreeOptions
        {
            Depth = maxDepth > 0 ? maxDepth : null,
            Window = window?.ToString()
        }, ct);
        return tree.Select(MapElement).ToList();
    }

    public async Task<DevFlowElementInfo?> GetElementAsync(string id, CancellationToken ct = default)
    {
        var element = await _client.GetElementAsync(id, ct);
        return element is null ? null : MapElement(element);
    }

    public async Task<string?> GetPropertyAsync(string elementId, string propertyName, CancellationToken ct = default)
    {
        var value = await _client.GetPropertyAsync(elementId, propertyName, ct);
        return ValueToString(value);
    }

    public async Task<bool> SetPropertyAsync(string elementId, string propertyName, string value, CancellationToken ct = default)
    {
        await _client.SetPropertyAsync(elementId, propertyName, value, ct);
        return true;
    }

    public Task<byte[]?> GetScreenshotAsync(int? window = null, string? elementId = null, CancellationToken ct = default) =>
        _client.GetScreenshotAsync(new ScreenshotOptions { Window = window?.ToString(), ElementId = elementId }, ct);

    public async Task<bool> FocusAsync(string elementId, CancellationToken ct = default)
    {
        var result = await _client.FocusAsync(new FocusRequest { ElementId = elementId }, ct);
        return result.Success;
    }

    public async Task<DevFlowHitTestResult?> HitTestAsync(double x, double y, int? window = null, CancellationToken ct = default)
    {
        var elements = await _client.HitTestAsync(x, y, window?.ToString(), ct);
        return new DevFlowHitTestResult
        {
            X = x,
            Y = y,
            Window = window ?? 0,
            Elements = elements.Select(e => new DevFlowHitTestElement
            {
                Id = e.Id,
                Type = e.Type,
                AutomationId = e.AutomationId,
                Text = e.Text,
                Bounds = MapBounds(e.Bounds),
                WindowBounds = MapBounds(e.Bounds)
            }).ToList()
        };
    }

    public async Task<List<DevFlowLogEntry>> GetLogsAsync(int limit = 100, int skip = 0, string? source = null, CancellationToken ct = default)
    {
        var logs = await _client.GetLogsAsync(new LogQuery { Limit = limit, Skip = skip, Source = source }, ct);
        return logs.Select(MapLogEntry).ToList();
    }

    public Task StreamLogsAsync(
        Action<List<DevFlowLogEntry>> onReplay,
        Action<DevFlowLogEntry> onEntry,
        string? source = null,
        int replay = 100,
        CancellationToken ct = default) =>
        _client.StreamLogsAsync(
            entries => onReplay(entries.Select(MapLogEntry).ToList()),
            entry => onEntry(MapLogEntry(entry)),
            new LogStreamOptions { Source = source, Replay = replay },
            ct);

    public async Task<DevFlowProfilerCapabilities?> GetProfilerCapabilitiesAsync(CancellationToken ct = default)
    {
        var caps = await _client.GetProfilerCapabilitiesAsync(ct);
        return new DevFlowProfilerCapabilities
        {
            Available = true,
            SupportedInBuild = true,
            FeatureEnabled = true,
            Platform = caps.Platform,
            ManagedMemorySupported = caps.ManagedMemorySupported,
            NativeMemorySupported = caps.NativeMemorySupported,
            GcSupported = caps.GcSupported,
            CpuPercentSupported = caps.CpuPercentSupported,
            FpsSupported = caps.FpsSupported,
            FrameTimingsEstimated = caps.FrameTimingsEstimated,
            NativeFrameTimingsSupported = caps.NativeFrameTimingsSupported,
            JankEventsSupported = caps.JankEventsSupported,
            UiThreadStallSupported = caps.UiThreadStallSupported,
            ThreadCountSupported = caps.ThreadCountSupported
        };
    }

    public async Task<DevFlowProfilerStartResponse?> StartProfilerAsync(int? sampleIntervalMs = null, CancellationToken ct = default)
    {
        var session = await _client.StartProfilingAsync(sampleIntervalMs, ct);
        _activeProfilerSessionId = session.SessionId;
        return new DevFlowProfilerStartResponse
        {
            Session = MapProfilerSession(session),
            Capabilities = await GetProfilerCapabilitiesAsync(ct)
        };
    }

    public async Task<DevFlowProfilerStopResponse?> StopProfilerAsync(CancellationToken ct = default)
    {
        var sessionId = await EnsureProfilerSessionIdAsync(ct);
        if (sessionId is null)
            return null;

        await _client.StopProfilingAsync(sessionId, ct);
        return new DevFlowProfilerStopResponse
        {
            StoppedAtUtc = DateTimeOffset.UtcNow,
            Session = new DevFlowProfilerSessionInfo
            {
                SessionId = sessionId,
                IsActive = false
            }
        };
    }

    public async Task<DevFlowProfilerBatch?> GetProfilerSamplesAsync(
        long sampleCursor = 0,
        long markerCursor = 0,
        long spanCursor = 0,
        int limit = 200,
        CancellationToken ct = default)
    {
        var sessionId = await EnsureProfilerSessionIdAsync(ct);
        if (sessionId is null)
            return new DevFlowProfilerBatch();

        var batch = await _client.GetProfilerSamplesAsync(
            sessionId,
            checked((int)Math.Min(sampleCursor, int.MaxValue)),
            checked((int)Math.Min(markerCursor, int.MaxValue)),
            checked((int)Math.Min(spanCursor, int.MaxValue)),
            limit,
            ct);

        return new DevFlowProfilerBatch
        {
            SessionId = batch.SessionId,
            Samples = batch.Samples.Select(MapProfilerSample).ToList(),
            Markers = batch.Markers.Select(MapProfilerMarker).ToList(),
            Spans = batch.Spans.Select(MapProfilerSpan).ToList(),
            SampleCursor = batch.SampleCursor,
            MarkerCursor = batch.MarkerCursor,
            SpanCursor = batch.SpanCursor,
            IsActive = batch.IsActive
        };
    }

    public async Task<List<DevFlowProfilerHotspot>> GetProfilerHotspotsAsync(
        int limit = 20,
        int minDurationMs = 16,
        string? kind = "ui.operation",
        CancellationToken ct = default)
    {
        var hotspots = await _client.GetProfilerHotspotsAsync(limit, minDurationMs, kind, ct);
        return hotspots.Select(h => new DevFlowProfilerHotspot
        {
            Kind = h.Kind,
            Name = h.Name,
            Screen = h.Screen,
            Count = h.Count,
            ErrorCount = h.ErrorCount ?? 0,
            AvgDurationMs = h.AvgDurationMs,
            P95DurationMs = h.P95DurationMs ?? 0,
            MaxDurationMs = h.MaxDurationMs ?? 0
        }).ToList();
    }

    public async Task<CdpResponse?> SendCdpCommandAsync(string method, Dictionary<string, object?>? parameters = null, string? targetId = null, CancellationToken ct = default)
    {
        if (!method.Equals("Runtime.evaluate", StringComparison.Ordinal))
        {
            return new CdpResponse { Error = $"Unsupported WebView command: {method}" };
        }

        var expression = parameters?.TryGetValue("expression", out var expr) == true ? expr?.ToString() : null;
        if (string.IsNullOrWhiteSpace(expression))
            return new CdpResponse { Error = "Missing JavaScript expression." };

        try
        {
            var result = await _client.EvaluateJavaScriptAsync(expression, targetId, ct);
            if (result.ExceptionDetails != null)
                return new CdpResponse { Error = result.ExceptionDetails.Text };

            return new CdpResponse
            {
                Result = JsonSerializer.SerializeToElement(new
                {
                    result = new
                    {
                        type = GetCdpType(result.Result),
                        value = result.Result
                    }
                }, JsonOptions)
            };
        }
        catch (Exception ex)
        {
            return new CdpResponse { Error = ex.Message };
        }
    }

    public async Task<List<CdpTarget>> GetCdpTargetsAsync(CancellationToken ct = default)
    {
        var contexts = await _client.GetWebViewContextsAsync(ct);
        return contexts.Select(c => new CdpTarget
        {
            Id = c.Id,
            Title = c.Title,
            Url = c.Url,
            Ready = c.Ready
        }).ToList();
    }

    public async Task<List<DevFlowNetworkRequest>> GetNetworkRequestsAsync(CancellationToken ct = default)
    {
        var requests = await _client.GetNetworkRequestsAsync(ct);
        return requests.Select(MapNetworkRequest).ToList();
    }

    public async Task<DevFlowNetworkRequest?> GetNetworkRequestDetailAsync(string id, CancellationToken ct = default)
    {
        var detail = await _client.GetNetworkRequestDetailAsync(id, ct);
        return detail is null ? null : MapNetworkRequest(detail);
    }

    public async Task<bool> ClearNetworkRequestsAsync(CancellationToken ct = default)
    {
        await _client.ClearNetworkRequestsAsync(ct);
        return true;
    }

    public async Task<DevFlowAppInfo?> GetAppInfoAsync(CancellationToken ct = default)
    {
        var app = await _client.GetAppInfoAsync(ct);
        return new DevFlowAppInfo
        {
            Name = app.Name,
            PackageName = app.PackageId,
            Version = app.Version,
            BuildNumber = app.BuildNumber,
            RequestedTheme = app.Theme
        };
    }

    public async Task<DevFlowDeviceInfo?> GetDeviceInfoAsync(CancellationToken ct = default)
    {
        var device = await _client.GetDeviceInfoAsync(ct);
        return new DevFlowDeviceInfo
        {
            Manufacturer = device.Manufacturer,
            Model = device.Model,
            Platform = device.Platform,
            Idiom = device.Idiom,
            OsVersion = device.OsVersion,
            DeviceType = device.Architecture
        };
    }

    public async Task<DevFlowDisplayInfo?> GetDisplayInfoAsync(CancellationToken ct = default)
    {
        var display = await _client.GetDisplayInfoAsync(ct);
        return new DevFlowDisplayInfo
        {
            Width = display.Width,
            Height = display.Height,
            Density = display.Density,
            Orientation = display.Orientation,
            RefreshRate = display.RefreshRate ?? 0
        };
    }

    public async Task<DevFlowBatteryInfo?> GetBatteryInfoAsync(CancellationToken ct = default)
    {
        var battery = await _client.GetBatteryInfoAsync(ct);
        return new DevFlowBatteryInfo
        {
            ChargeLevel = battery.Level,
            State = battery.State,
            PowerSource = battery.PowerSource
        };
    }

    public async Task<DevFlowConnectivityInfo?> GetConnectivityAsync(CancellationToken ct = default)
    {
        var connectivity = await _client.GetConnectivityAsync(ct);
        return new DevFlowConnectivityInfo
        {
            NetworkAccess = connectivity.NetworkAccess,
            ConnectionProfiles = connectivity.ConnectionProfiles.ToList()
        };
    }

    public Task<DevFlowVersionTracking?> GetVersionTrackingAsync(CancellationToken ct = default) =>
        Task.FromResult<DevFlowVersionTracking?>(null);

    public async Task<List<DevFlowPreferenceEntry>> GetPreferencesAsync(string? sharedName = null, CancellationToken ct = default)
    {
        var preferences = await _client.GetPreferencesAsync(sharedName, ct);
        return preferences.Select(p => new DevFlowPreferenceEntry
        {
            Key = p.Key,
            Value = ValueToString(p.Value),
            SharedName = sharedName
        }).ToList();
    }

    public async Task<bool> SetPreferenceAsync(string key, object? value, string? type = null, string? sharedName = null, CancellationToken ct = default)
    {
        await _client.SetPreferenceAsync(key, value ?? string.Empty, type, sharedName, ct);
        return true;
    }

    public async Task<bool> DeletePreferenceAsync(string key, string? sharedName = null, CancellationToken ct = default)
    {
        await _client.DeletePreferenceAsync(key, sharedName, ct);
        return true;
    }

    public async Task<bool> ClearPreferencesAsync(string? sharedName = null, CancellationToken ct = default)
    {
        await _client.ClearPreferencesAsync(sharedName, ct);
        return true;
    }

    public async Task<DevFlowSecureStorageEntry?> GetSecureStorageAsync(string key, CancellationToken ct = default)
    {
        var entry = await _client.GetSecureStorageAsync(key, ct);
        return entry is null ? null : new DevFlowSecureStorageEntry
        {
            Key = entry.Key,
            Value = entry.Value,
            Exists = entry.Exists
        };
    }

    public async Task<bool> SetSecureStorageAsync(string key, string value, CancellationToken ct = default)
    {
        await _client.SetSecureStorageAsync(key, value, ct);
        return true;
    }

    public async Task<bool> DeleteSecureStorageAsync(string key, CancellationToken ct = default)
    {
        await _client.DeleteSecureStorageAsync(key, ct);
        return true;
    }

    public async Task<bool> ClearSecureStorageAsync(CancellationToken ct = default)
    {
        await _client.ClearSecureStorageAsync(ct);
        return true;
    }

    public async Task<List<DevFlowPermissionStatus>> GetPermissionsAsync(CancellationToken ct = default)
    {
        var permissions = await _client.GetPermissionsAsync(ct);
        return permissions.Select(p => new DevFlowPermissionStatus
        {
            Permission = p.Name,
            Status = p.Status
        }).ToList();
    }

    public async Task<DevFlowPermissionStatus?> CheckPermissionAsync(string permission, CancellationToken ct = default)
    {
        var status = await _client.CheckPermissionAsync(permission, ct);
        return new DevFlowPermissionStatus
        {
            Permission = status.Name,
            Status = status.Status
        };
    }

    public async Task<DevFlowGeolocation?> GetGeolocationAsync(string accuracy = "Medium", int timeoutSeconds = 10, CancellationToken ct = default)
    {
        var location = await _client.GetGeolocationAsync(accuracy, timeoutSeconds, ct);
        return new DevFlowGeolocation
        {
            Latitude = location.Latitude,
            Longitude = location.Longitude,
            Altitude = location.Altitude,
            Accuracy = location.Accuracy,
            Timestamp = location.Timestamp
        };
    }

    public async Task<List<DevFlowSensorStatus>> GetSensorsAsync(CancellationToken ct = default)
    {
        var sensors = await _client.GetSensorsAsync(ct);
        return sensors.Select(s => new DevFlowSensorStatus
        {
            Name = s.Name,
            Active = s.Active,
            Available = s.Available,
            Subscribers = IsSensorStreaming(s.Name) ? 1 : 0
        }).ToList();
    }

    public async Task<bool> StartSensorAsync(string sensor, string speed = "UI", CancellationToken ct = default)
    {
        await _client.StartSensorAsync(sensor, speed, ct);
        return true;
    }

    public async Task<bool> StopSensorAsync(string sensor, CancellationToken ct = default)
    {
        await _client.StopSensorAsync(sensor, ct);
        return true;
    }

    public async Task StreamSensorAsync(string sensor, Action<DevFlowSensorReading> onReading, string speed = "UI", int throttleMs = 100, CancellationToken ct = default)
    {
        StopSensorStream(sensor);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lock (_sensorStreams)
            _sensorStreams[sensor] = cts;

        try
        {
            await _client.StreamSensorAsync(sensor, reading => onReading(new DevFlowSensorReading
            {
                Sensor = reading.Sensor,
                Timestamp = reading.Timestamp.ToString("O"),
                Values = JsonSerializer.SerializeToElement(reading.Values, JsonOptions)
            }), speed, throttleMs, cts.Token);
        }
        catch (NotImplementedException)
        {
        }
        finally
        {
            lock (_sensorStreams)
                _sensorStreams.Remove(sensor);
            cts.Dispose();
        }
    }

    public void StopSensorStream(string sensor)
    {
        lock (_sensorStreams)
        {
            if (_sensorStreams.Remove(sensor, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
        }
    }

    public void StopAllSensorStreams()
    {
        lock (_sensorStreams)
        {
            foreach (var cts in _sensorStreams.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }
            _sensorStreams.Clear();
        }
    }

    public bool IsSensorStreaming(string sensor)
    {
        lock (_sensorStreams)
            return _sensorStreams.ContainsKey(sensor);
    }

    private async Task<string?> EnsureProfilerSessionIdAsync(CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_activeProfilerSessionId))
            return _activeProfilerSessionId;

        var sessions = await _client.GetProfilerSessionsAsync(ct);
        var session = sessions.FirstOrDefault(s => s.IsActive) ?? sessions.FirstOrDefault();
        _activeProfilerSessionId = session?.SessionId;
        return _activeProfilerSessionId;
    }

    private static DevFlowElementInfo MapElement(InspectorElement element) => new()
    {
        Id = element.Id,
        ParentId = element.ParentId,
        Type = element.Type,
        FullType = element.FullType,
        AutomationId = element.AutomationId,
        Text = element.Text ?? element.Value,
        IsVisible = element.State.Displayed,
        IsEnabled = element.State.Enabled,
        IsFocused = element.State.Focused,
        Opacity = element.State.Opacity,
        Bounds = MapBounds(element.Bounds),
        WindowBounds = MapBounds(element.WindowBounds ?? element.Bounds),
        Gestures = element.Gestures?.ToList(),
        NativeType = element.NativeView?.Type,
        NativeProperties = element.NativeView?.Properties?.ToDictionary(kv => kv.Key, kv => ValueToString(kv.Value)),
        Children = element.Children?.Select(MapElement).ToList()
    };

    private static DevFlowBoundsInfo? MapBounds(BoundsInfo? bounds) => bounds is null
        ? null
        : new DevFlowBoundsInfo
        {
            X = bounds.X,
            Y = bounds.Y,
            Width = bounds.Width,
            Height = bounds.Height
        };

    private static DevFlowLogEntry MapLogEntry(InspectorLogEntry entry) => new()
    {
        V1Timestamp = entry.Timestamp,
        V1Level = entry.Level,
        V1Source = entry.Source,
        V1Message = entry.Message,
        V1Category = entry.Category,
        V1Exception = entry.Exception
    };

    private static DevFlowNetworkRequest MapNetworkRequest(InspectorNetworkRequest request)
    {
        var result = new DevFlowNetworkRequest
        {
            Id = request.Id,
            Timestamp = request.Timestamp,
            Method = request.Method,
            Url = request.Url,
            Host = request.Host,
            Path = request.Path,
            StatusCode = request.StatusCode,
            StatusText = request.StatusText,
            DurationMs = (long)(request.DurationMs ?? 0),
            Error = request.Error,
            RequestContentType = request.RequestContentType,
            ResponseContentType = request.ResponseContentType,
            RequestSize = request.RequestSize,
            ResponseSize = request.ResponseSize
        };

        if (request is InspectorNetworkRequestDetail detail)
        {
            result.RequestHeadersRaw = JsonSerializer.SerializeToElement(detail.RequestHeaders, JsonOptions);
            result.ResponseHeadersRaw = JsonSerializer.SerializeToElement(detail.ResponseHeaders, JsonOptions);
            result.RequestBody = detail.RequestBody;
            result.ResponseBody = detail.ResponseBody;
            result.RequestBodyEncoding = detail.RequestBodyEncoding;
            result.ResponseBodyEncoding = detail.ResponseBodyEncoding;
            result.RequestBodyTruncated = detail.RequestBodyTruncated ?? false;
            result.ResponseBodyTruncated = detail.ResponseBodyTruncated ?? false;
        }

        return result;
    }

    private static DevFlowProfilerSessionInfo MapProfilerSession(InspectorProfilerSession session) => new()
    {
        SessionId = session.SessionId,
        StartedAtUtc = session.StartedAtUtc,
        SampleIntervalMs = session.SampleIntervalMs,
        IsActive = session.IsActive
    };

    private static DevFlowProfilerSample MapProfilerSample(InspectorProfilerSample sample) => new()
    {
        TsUtc = sample.TsUtc,
        Fps = sample.Fps,
        FrameTimeMsP50 = sample.FrameTimeMsP50,
        FrameTimeMsP95 = sample.FrameTimeMsP95,
        WorstFrameTimeMs = sample.WorstFrameTimeMs,
        ManagedBytes = sample.ManagedBytes ?? 0,
        NativeMemoryBytes = sample.NativeMemoryBytes,
        NativeMemoryKind = sample.NativeMemoryKind,
        Gc0 = sample.Gc0 ?? 0,
        Gc1 = sample.Gc1 ?? 0,
        Gc2 = sample.Gc2 ?? 0,
        CpuPercent = sample.CpuPercent,
        ThreadCount = sample.ThreadCount,
        JankFrameCount = sample.JankFrameCount ?? 0,
        UiThreadStallCount = sample.UiThreadStallCount ?? 0,
        FrameSource = sample.FrameSource,
        FrameQuality = sample.FrameQuality
    };

    private static DevFlowProfilerMarker MapProfilerMarker(InspectorProfilerMarker marker) => new()
    {
        TsUtc = marker.TsUtc,
        Type = marker.Type,
        Name = marker.Name,
        PayloadJson = marker.PayloadJson
    };

    private static DevFlowProfilerSpan MapProfilerSpan(InspectorProfilerSpan span) => new()
    {
        SpanId = span.SpanId,
        ParentSpanId = span.ParentSpanId,
        TraceId = span.TraceId,
        StartTsUtc = span.StartTsUtc,
        EndTsUtc = span.EndTsUtc ?? span.StartTsUtc.AddMilliseconds(span.DurationMs),
        DurationMs = span.DurationMs,
        Kind = span.Kind,
        Name = span.Name,
        Status = span.Status ?? string.Empty,
        ThreadId = int.TryParse(span.ThreadId, out var threadId) ? threadId : null,
        Screen = span.Screen,
        ElementPath = span.ElementPath,
        TagsJson = span.TagsJson,
        Error = span.Error
    };

    private static string GetCdpType(object? value) => value switch
    {
        null => "undefined",
        string => "string",
        bool => "boolean",
        int or long or float or double or decimal => "number",
        JsonElement element => element.ValueKind switch
        {
            JsonValueKind.String => "string",
            JsonValueKind.Number => "number",
            JsonValueKind.True or JsonValueKind.False => "boolean",
            JsonValueKind.Null or JsonValueKind.Undefined => "undefined",
            _ => "object"
        },
        _ => "object"
    };

    private static string? ValueToString(object? value) => value switch
    {
        null => null,
        string s => s,
        JsonElement element => element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString(),
        _ => value.ToString()
    };

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        StopAllSensorStreams();
        _client.Dispose();
    }
}
