using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using MauiSherpa.AppInspector;
using MauiSherpa.AppInspector.Services;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

AppContext.SetSwitch("Microsoft.Extensions.DependencyInjection.DisableDynamicEngine", true);

var options = InspectorCliOptions.Parse(args);
if (options.ShowHelp)
{
    Console.WriteLine(InspectorCliOptions.HelpText);
    return 0;
}

if (options.Error is not null)
{
    Console.Error.WriteLine(options.Error);
    Console.Error.WriteLine();
    Console.Error.WriteLine(InspectorCliOptions.HelpText);
    return 2;
}

var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
var security = new InspectorSecurity(token);
var lifecycle = new InspectorLifecycleState(options.IdleTimeout, options.NoAutoExit);

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(static options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss ";
});
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.UseSetting(WebHostDefaults.PreventHostingStartupKey, "true");

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(security);
builder.Services.AddSingleton(lifecycle);
builder.Services.AddHostedService<InspectorIdleShutdownService>();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IAppInspectorClientFactory, AppInspectorClientFactory>();
builder.Services.AddSingleton<IThemeService, StaticInspectorThemeService>();

var app = builder.Build();
var scheme = options.Https ? "https" : "http";
app.Urls.Add($"{scheme}://{options.ListenHost}:{options.ListenPort}");

app.UseStaticFiles();
app.Use(async (context, next) =>
{
    if (IsPublicAsset(context.Request.Path) || security.Authorize(context))
    {
        await next();
        return;
    }

    context.Response.StatusCode = StatusCodes.Status404NotFound;
});
app.UseAntiforgery();

app.MapPost("/internal/heartbeat", (InspectorLifecycleState state) =>
{
    state.MarkHeartbeat();
    return Results.NoContent();
});

app.MapMethods("/internal/shutdown", ["GET", "POST"], (IHostApplicationLifetime lifetime) =>
{
    lifetime.StopApplication();
    return Results.Json(new { status = "stopping" });
});

app.MapGet("/internal/status", (InspectorLifecycleState state) => Results.Json(new
{
    status = "running",
    connected = state.HasConnected,
    lastHeartbeatUtc = state.LastHeartbeatUtc
}));

app.MapRazorComponents<ServerApp>()
    .AddInteractiveServerRenderMode();

await app.StartAsync();

var addresses = app.Services.GetRequiredService<IServer>().Features
    .Get<IServerAddressesFeature>()?
    .Addresses
    .ToArray() ?? [];
var endpoint = addresses.FirstOrDefault() ?? $"{scheme}://{options.ListenHost}:{options.ListenPort}";
var inspectorUrl = BuildInspectorUrl(endpoint, options, token);
var ready = InspectorReadyPayload.Create(options, addresses, inspectorUrl, token);

Console.WriteLine($"INSPECTOR_READY {JsonSerializer.Serialize(ready, JsonDefaults.Options)}");
Console.Error.WriteLine($"MAUI Sherpa inspector is serving {inspectorUrl}");
Console.Error.WriteLine($"Stop with Ctrl+C, by terminating PID {Environment.ProcessId}, or by calling {ready.Stop.ShutdownUrl}");

await app.WaitForShutdownAsync();
return 0;

static string BuildInspectorUrl(string endpoint, InspectorCliOptions options, string token)
{
    var agentId = Uri.EscapeDataString(options.AgentId ?? "agent");
    var tab = Uri.EscapeDataString(options.Tab ?? "tree");
    var host = Uri.EscapeDataString(options.AgentHost);
    var port = options.AgentPort!.Value;
    var query = $"host={host}&port={port}&token={Uri.EscapeDataString(token)}";
    if (!string.IsNullOrWhiteSpace(options.Project))
        query += $"&project={Uri.EscapeDataString(options.Project)}";
    if (!string.IsNullOrWhiteSpace(options.SessionId))
        query += $"&sessionId={Uri.EscapeDataString(options.SessionId)}";
    if (!string.IsNullOrWhiteSpace(options.AppName))
        query += $"&appName={Uri.EscapeDataString(options.AppName)}";
    return $"{endpoint.TrimEnd('/')}/inspector/devflow/{agentId}/{tab}?{query}";
}

static bool IsPublicAsset(PathString path)
{
    return path.StartsWithSegments("/_framework")
        || path.StartsWithSegments("/_content")
        || path.Value?.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase) == true;
}

internal sealed record InspectorCliOptions
{
    public bool ShowHelp { get; init; }
    public string? Error { get; init; }
    public string AgentHost { get; init; } = "localhost";
    public int? AgentPort { get; init; }
    public string? AgentId { get; init; }
    public string? Project { get; init; }
    public string? SessionId { get; init; }
    public string? AppName { get; init; }
    public string? Tab { get; init; } = "tree";
    public string ListenHost { get; init; } = "127.0.0.1";
    public int ListenPort { get; init; }
    public bool Https { get; init; }
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(60);
    public bool NoAutoExit { get; init; }

    public static string HelpText =>
        """
        MAUI Sherpa App Inspector

        Usage:
          maui-sherpa-inspector serve --agent-port <port> [options]
          maui-sherpa-inspector --agent-port <port> [options]

        Required:
          --agent-port, --port <port>       Target app inspector agent port.

        Agent metadata:
          --agent-host <host>               Target agent host. Default: localhost.
          --agent-id <id>                   Agent/session id for the inspector route.
          --project <path-or-name>          Project metadata echoed in startup output.
          --session-id <id>                 Host session metadata echoed in startup output.
          --app-name <name>                 App name metadata echoed in startup output.
          --tab <name>                      Initial tab: tree, network, profiling, webview, logs, platform.

        Server:
          --listen-host <host>              Local bind host. Default: 127.0.0.1.
          --listen-port <port>              Local bind port. Default: 0 (ephemeral).
          --https                           Serve HTTPS instead of HTTP when a cert is available.
          --idle-timeout <seconds>          Exit after this many seconds without WebView heartbeat. Default: 60.
          --no-auto-exit                    Keep running until Ctrl+C/SIGTERM/shutdown endpoint.

        Output:
          The server prints a single machine-readable line when ready:
          INSPECTOR_READY { "url": "...", "endpoints": [...], "pid": 123, "stop": { ... } }
        """;

    public static InspectorCliOptions Parse(string[] args)
    {
        if (args.Length == 0)
            return new InspectorCliOptions { ShowHelp = true };

        var index = 0;
        if (args[index].Equals("serve", StringComparison.OrdinalIgnoreCase))
            index++;

        var result = new MutableOptions();
        while (index < args.Length)
        {
            var arg = args[index++];
            switch (arg)
            {
                case "-h":
                case "--help":
                    return new InspectorCliOptions { ShowHelp = true };
                case "--https":
                    result.Https = true;
                    break;
                case "--no-auto-exit":
                    result.NoAutoExit = true;
                    break;
                case "--agent-host":
                    if (!ReadValue(args, ref index, arg, out var agentHost, out var hostError))
                        return new InspectorCliOptions { Error = hostError };
                    result.AgentHost = agentHost!;
                    break;
                case "--agent-port":
                case "--port":
                    if (!ReadInt(args, ref index, arg, out result.AgentPort, out var portError))
                        return new InspectorCliOptions { Error = portError };
                    break;
                case "--agent-id":
                    if (!ReadValue(args, ref index, arg, out result.AgentId, out var agentError))
                        return new InspectorCliOptions { Error = agentError };
                    break;
                case "--project":
                    if (!ReadValue(args, ref index, arg, out result.Project, out var projectError))
                        return new InspectorCliOptions { Error = projectError };
                    break;
                case "--session-id":
                    if (!ReadValue(args, ref index, arg, out result.SessionId, out var sessionError))
                        return new InspectorCliOptions { Error = sessionError };
                    break;
                case "--app-name":
                    if (!ReadValue(args, ref index, arg, out result.AppName, out var appError))
                        return new InspectorCliOptions { Error = appError };
                    break;
                case "--tab":
                    if (!ReadValue(args, ref index, arg, out result.Tab, out var tabError))
                        return new InspectorCliOptions { Error = tabError };
                    break;
                case "--listen-host":
                    if (!ReadValue(args, ref index, arg, out var listenHost, out var listenHostError))
                        return new InspectorCliOptions { Error = listenHostError };
                    result.ListenHost = listenHost!;
                    break;
                case "--listen-port":
                    if (!ReadInt(args, ref index, arg, out result.ListenPort, out var listenPortError))
                        return new InspectorCliOptions { Error = listenPortError };
                    break;
                case "--idle-timeout":
                    if (!ReadTimeout(args, ref index, arg, out result.IdleTimeout, out var timeoutError))
                        return new InspectorCliOptions { Error = timeoutError };
                    break;
                default:
                    return new InspectorCliOptions { Error = $"Unknown argument: {arg}" };
            }
        }

        if (result.AgentPort is null)
            return new InspectorCliOptions { Error = "Missing required --agent-port <port>." };
        if (result.AgentPort is <= 0 or > 65535)
            return new InspectorCliOptions { Error = "--agent-port must be between 1 and 65535." };
        if (result.ListenPort is < 0 or > 65535)
            return new InspectorCliOptions { Error = "--listen-port must be between 0 and 65535." };

        return result.ToOptions();
    }

    private static bool ReadValue(string[] args, ref int index, string name, out string? value, out string? error)
    {
        if (index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal))
        {
            value = null;
            error = $"{name} requires a value.";
            return false;
        }
        value = args[index++];
        error = null;
        return true;
    }

    private static bool ReadInt(string[] args, ref int index, string name, out int? value, out string? error)
    {
        if (!ReadValue(args, ref index, name, out var raw, out error))
        {
            value = null;
            return false;
        }
        if (!int.TryParse(raw, out var parsed))
        {
            value = null;
            error = $"{name} must be an integer.";
            return false;
        }
        value = parsed;
        return true;
    }

    private static bool ReadTimeout(string[] args, ref int index, string name, out TimeSpan value, out string? error)
    {
        if (!ReadValue(args, ref index, name, out var raw, out error))
        {
            value = default;
            return false;
        }
        if (double.TryParse(raw, out var seconds))
        {
            value = TimeSpan.FromSeconds(seconds);
            return true;
        }
        if (TimeSpan.TryParse(raw, out value))
            return true;

        error = $"{name} must be seconds or a TimeSpan value.";
        return false;
    }

    private sealed class MutableOptions
    {
        public string AgentHost = "localhost";
        public int? AgentPort;
        public string? AgentId;
        public string? Project;
        public string? SessionId;
        public string? AppName;
        public string? Tab = "tree";
        public string ListenHost = "127.0.0.1";
        public int? ListenPort;
        public bool Https;
        public TimeSpan IdleTimeout = TimeSpan.FromSeconds(60);
        public bool NoAutoExit;

        public InspectorCliOptions ToOptions() => new()
        {
            AgentHost = AgentHost,
            AgentPort = AgentPort,
            AgentId = AgentId,
            Project = Project,
            SessionId = SessionId,
            AppName = AppName,
            Tab = Tab,
            ListenHost = ListenHost,
            ListenPort = ListenPort ?? 0,
            Https = Https,
            IdleTimeout = IdleTimeout,
            NoAutoExit = NoAutoExit
        };
    }
}

internal sealed class InspectorSecurity
{
    private const string CookieName = "maui_sherpa_inspector";
    private readonly string _token;

    public InspectorSecurity(string token)
    {
        _token = token;
    }

    public bool Authorize(HttpContext context)
    {
        if (context.Request.Query.TryGetValue("token", out var queryToken) && queryToken == _token)
        {
            context.Response.Cookies.Append(CookieName, _token, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,
                Secure = context.Request.IsHttps
            });
            return true;
        }

        return context.Request.Cookies.TryGetValue(CookieName, out var cookieToken) && cookieToken == _token;
    }
}

internal sealed class InspectorLifecycleState
{
    private readonly object _gate = new();

    public InspectorLifecycleState(TimeSpan idleTimeout, bool noAutoExit)
    {
        IdleTimeout = idleTimeout;
        NoAutoExit = noAutoExit;
    }

    public TimeSpan IdleTimeout { get; }
    public bool NoAutoExit { get; }
    public bool HasConnected { get; private set; }
    public DateTimeOffset? LastHeartbeatUtc { get; private set; }

    public void MarkHeartbeat()
    {
        lock (_gate)
        {
            HasConnected = true;
            LastHeartbeatUtc = DateTimeOffset.UtcNow;
        }
    }

    public bool IsIdle(DateTimeOffset now)
    {
        lock (_gate)
            return HasConnected && LastHeartbeatUtc is { } last && now - last > IdleTimeout;
    }
}

internal sealed class InspectorIdleShutdownService : BackgroundService
{
    private readonly InspectorLifecycleState _state;
    private readonly IHostApplicationLifetime _lifetime;

    public InspectorIdleShutdownService(InspectorLifecycleState state, IHostApplicationLifetime lifetime)
    {
        _state = state;
        _lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_state.NoAutoExit || _state.IdleTimeout <= TimeSpan.Zero)
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            if (_state.IsIdle(DateTimeOffset.UtcNow))
            {
                Console.Error.WriteLine("No inspector WebView heartbeat detected; stopping server.");
                _lifetime.StopApplication();
                return;
            }
        }
    }
}

internal sealed record InspectorReadyPayload(
    string Status,
    string Url,
    IReadOnlyList<string> Endpoints,
    int Pid,
    InspectorReadyAgent Agent,
    InspectorReadyStop Stop)
{
    public static InspectorReadyPayload Create(InspectorCliOptions options, IReadOnlyList<string> endpoints, string url, string token)
    {
        var shutdownBase = endpoints.FirstOrDefault() ?? url.Split("/inspector/", StringSplitOptions.None)[0];
        return new InspectorReadyPayload(
            "ready",
            url,
            endpoints,
            Environment.ProcessId,
            new InspectorReadyAgent(
                options.AgentHost,
                options.AgentPort!.Value,
                options.AgentId,
                options.Project,
                options.SessionId,
                options.AppName),
            new InspectorReadyStop(
                "Send Ctrl+C/SIGTERM, terminate the PID, or call the shutdownUrl.",
                $"{shutdownBase.TrimEnd('/')}/internal/shutdown?token={WebUtility.UrlEncode(token)}",
                options.NoAutoExit ? null : (int)options.IdleTimeout.TotalSeconds));
    }
}

internal sealed record InspectorReadyAgent(
    string Host,
    int Port,
    string? AgentId,
    string? Project,
    string? SessionId,
    string? AppName);

internal sealed record InspectorReadyStop(
    string Instructions,
    string ShutdownUrl,
    int? AutoExitIdleSeconds);

internal static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };
}
