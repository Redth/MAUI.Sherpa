using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Inspector;
using Microsoft.Extensions.Logging;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Creates the appropriate <see cref="IAppInspectorClient"/> based on what
/// the target agent supports. Probes the v1 endpoint first; falls back to
/// legacy if the agent doesn't support v1.
/// </summary>
public class AppInspectorClientFactory : IAppInspectorClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AppInspectorClientFactory> _logger;

    public AppInspectorClientFactory(
        IHttpClientFactory httpClientFactory,
        ILogger<AppInspectorClientFactory> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<IAppInspectorClient> CreateAsync(string host, int port, CancellationToken ct = default)
    {
        var httpClient = _httpClientFactory.CreateClient("DevFlowAgent");
        httpClient.BaseAddress = new Uri($"http://{host}:{port}");
        httpClient.Timeout = TimeSpan.FromSeconds(5);

        // Try v1 first
        try
        {
            var response = await httpClient.GetAsync("/api/v1/agent/status", ct);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Agent at {Host}:{Port} supports DevFlow v1 protocol", host, port);
                return new DevFlowV1Client(host, port, _httpClientFactory, _logger);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogDebug(ex, "v1 probe failed for {Host}:{Port}, trying legacy", host, port);
        }

        // Fall back to legacy
        try
        {
            var response = await httpClient.GetAsync("/api/status", ct);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Agent at {Host}:{Port} uses legacy DevFlow protocol", host, port);
                return new DevFlowLegacyClient(host, port, _httpClientFactory, _logger);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Legacy probe also failed for {Host}:{Port}", host, port);
        }

        // Default to v1 — will produce clear errors on actual calls
        _logger.LogWarning("Could not determine protocol version for {Host}:{Port}, defaulting to v1", host, port);
        return new DevFlowV1Client(host, port, _httpClientFactory, _logger);
    }
}
