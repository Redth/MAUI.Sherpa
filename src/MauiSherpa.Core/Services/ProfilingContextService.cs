using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.DevFlow;
using MauiSherpa.Core.Models.Profiling;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Builds lightweight profiling summaries from live MauiDevFlow data.
/// </summary>
public class ProfilingContextService : IProfilingContextService
{
    private const string DefaultHost = "localhost";
    private const int DefaultAgentPort = 9223;
    private const int DefaultBrokerPort = 19223;

    private readonly ILoggingService _logger;
    private readonly Func<string, int, DevFlowAgentClient> _clientFactory;

    public ProfilingContextService(ILoggingService logger)
        : this(logger, (host, port) => new DevFlowAgentClient(host, port))
    {
    }

    internal ProfilingContextService(
        ILoggingService logger,
        Func<string, int, DevFlowAgentClient> clientFactory)
    {
        _logger = logger;
        _clientFactory = clientFactory;
    }

    public async Task<IReadOnlyList<ProfilingTargetInfo>> GetAvailableTargetsAsync(CancellationToken ct = default)
    {
        try
        {
            var agents = await DevFlowAgentClient.GetBrokerAgentsAsync(DefaultHost, DefaultBrokerPort, ct);
            if (agents.Count > 0)
            {
                return agents
                    .OrderByDescending(a => a.ConnectedAt ?? DateTimeOffset.MinValue)
                    .ThenBy(a => a.AppName ?? a.Project ?? a.Id, StringComparer.OrdinalIgnoreCase)
                    .Select(a => new ProfilingTargetInfo(
                        a.Id,
                        DefaultHost,
                        a.Port,
                        a.AppName ?? a.Project ?? a.Id,
                        a.AppName,
                        a.Platform,
                        a.Project,
                        a.Tfm,
                        a.ConnectedAt,
                        true,
                        "broker"))
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"Profiling target discovery via broker failed: {ex.Message}");
        }

        var directTarget = await TryGetDirectTargetAsync(ct);
        return directTarget is null ? Array.Empty<ProfilingTargetInfo>() : new[] { directTarget };
    }

    public async Task<ProfilingSnapshotResult> GetSnapshotAsync(ProfilingSnapshotOptions options, CancellationToken ct = default)
    {
        var targets = await GetAvailableTargetsAsync(ct);
        if (targets.Count == 0)
        {
            return new ProfilingSnapshotResult(
                null,
                "No local profiling targets are available. Start a MAUI app with MauiDevFlow enabled, then try again.");
        }

        var resolvedTarget = ResolveTarget(targets, options.TargetId);
        if (resolvedTarget.Target is null)
        {
            return new ProfilingSnapshotResult(
                null,
                options.TargetId is null
                    ? "No profiling target could be resolved."
                    : $"Profiling target '{options.TargetId}' was not found. Use list_profiling_targets to see available targets.");
        }

        using var client = _clientFactory(resolvedTarget.Target.Host, resolvedTarget.Target.Port);
        var status = await client.GetStatusAsync(ct);
        if (status == null)
        {
            return new ProfilingSnapshotResult(
                null,
                $"Could not connect to profiling target '{resolvedTarget.Target.DisplayName}'.");
        }

        var notes = new List<string>();
        if (!string.IsNullOrWhiteSpace(resolvedTarget.Note))
        {
            notes.Add(resolvedTarget.Note);
        }

        var networkSampleSize = Math.Clamp(options.NetworkSampleSize, 5, 200);
        ProfilingNetworkSummary? networkSummary = null;
        if (options.IncludeNetworkSummary)
        {
            var requests = await client.GetNetworkRequestsAsync(ct);
            networkSummary = BuildNetworkSummary(requests, networkSampleSize);
            if (networkSummary.SampleSize == 0)
            {
                notes.Add("No network requests have been captured yet for this target.");
            }
        }

        ProfilingVisualTreeSummary? visualTreeSummary = null;
        if (options.IncludeVisualTreeSummary)
        {
            var roots = await client.GetTreeAsync(ct: ct);
            visualTreeSummary = BuildVisualTreeSummary(roots);
            if (visualTreeSummary.TotalElementCount == 0)
            {
                notes.Add("The visual tree is currently empty or could not be enumerated.");
            }
        }

        var runtime = new ProfilingRuntimeInfo(
            status.Agent,
            status.Version,
            status.Platform,
            status.DeviceType,
            status.Idiom,
            status.AppName,
            status.Running,
            status.CdpReady,
            status.CdpWebViewCount);

        return new ProfilingSnapshotResult(
            new ProfilingSnapshot(
                resolvedTarget.Target,
                runtime,
                networkSummary,
                visualTreeSummary,
                notes));
    }

    internal static ProfilingNetworkSummary BuildNetworkSummary(
        IEnumerable<DevFlowNetworkRequest> requests,
        int sampleSize)
    {
        var sample = requests
            .OrderByDescending(r => r.Timestamp)
            .Take(Math.Max(sampleSize, 0))
            .ToList();

        if (sample.Count == 0)
        {
            return new ProfilingNetworkSummary(0, 0, 0, 0, 0, 0, 0, 0, Array.Empty<ProfilingRequestSummary>());
        }

        var durations = sample
            .Select(r => r.DurationMs)
            .OrderBy(v => v)
            .ToArray();

        var successCount = sample.Count(r => string.IsNullOrWhiteSpace(r.Error) && (!r.StatusCode.HasValue || r.StatusCode.Value < 400));
        var failureCount = sample.Count - successCount;

        var slowestRequests = sample
            .OrderByDescending(r => r.DurationMs)
            .ThenBy(r => r.Url, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select(r => new ProfilingRequestSummary(
                r.Method,
                r.Url,
                r.StatusCode,
                r.DurationMs,
                r.Error))
            .ToList();

        return new ProfilingNetworkSummary(
            sample.Count,
            successCount,
            failureCount,
            sample.Average(r => r.DurationMs),
            CalculatePercentile(durations, 0.95),
            durations[^1],
            sample.Sum(r => r.RequestSize ?? 0),
            sample.Sum(r => r.ResponseSize ?? 0),
            slowestRequests);
    }

    internal static ProfilingVisualTreeSummary BuildVisualTreeSummary(IEnumerable<DevFlowElementInfo> roots)
    {
        var rootList = roots.ToList();
        var flattened = FlattenTree(rootList).ToList();

        if (flattened.Count == 0)
        {
            return new ProfilingVisualTreeSummary(0, 0, 0, 0, 0, 0, Array.Empty<ProfilingElementTypeCount>());
        }

        var topElementTypes = flattened
            .GroupBy(item => string.IsNullOrWhiteSpace(item.Element.Type) ? "Unknown" : item.Element.Type)
            .Select(group => new ProfilingElementTypeCount(group.Key, group.Count()))
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group.Type, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();

        return new ProfilingVisualTreeSummary(
            rootList.Count,
            flattened.Count,
            flattened.Count(item => item.Element.IsVisible),
            flattened.Count(item => item.Element.IsFocused),
            flattened.Count(item => item.Element.Gestures?.Count > 0),
            flattened.Max(item => item.Depth),
            topElementTypes);
    }

    private async Task<ProfilingTargetInfo?> TryGetDirectTargetAsync(CancellationToken ct)
    {
        try
        {
            using var client = _clientFactory(DefaultHost, DefaultAgentPort);
            var status = await client.GetStatusAsync(ct);
            if (status == null)
            {
                return null;
            }

            return new ProfilingTargetInfo(
                "localhost",
                DefaultHost,
                DefaultAgentPort,
                status.AppName ?? "Local MauiDevFlow agent",
                status.AppName,
                status.Platform,
                null,
                null,
                null,
                status.Running,
                "direct");
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"Profiling direct target discovery failed: {ex.Message}");
            return null;
        }
    }

    private static (ProfilingTargetInfo? Target, string? Note) ResolveTarget(
        IReadOnlyList<ProfilingTargetInfo> targets,
        string? targetId)
    {
        if (!string.IsNullOrWhiteSpace(targetId))
        {
            var match = targets.FirstOrDefault(t =>
                t.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase) ||
                t.DisplayName.Equals(targetId, StringComparison.OrdinalIgnoreCase) ||
                (t.AppName?.Equals(targetId, StringComparison.OrdinalIgnoreCase) ?? false));

            return (match, null);
        }

        if (targets.Count == 1)
        {
            return (targets[0], null);
        }

        var selected = targets
            .OrderByDescending(t => t.ConnectedAt ?? DateTimeOffset.MinValue)
            .ThenBy(t => t.DisplayName, StringComparer.OrdinalIgnoreCase)
            .First();

        return (selected, $"Multiple profiling targets were available. Auto-selected '{selected.DisplayName}'.");
    }

    private static IEnumerable<(DevFlowElementInfo Element, int Depth)> FlattenTree(IEnumerable<DevFlowElementInfo> roots)
    {
        var stack = new Stack<(DevFlowElementInfo Element, int Depth)>(
            roots.Reverse().Select(root => (root, 1)));

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            yield return current;

            if (current.Element.Children == null)
            {
                continue;
            }

            for (var i = current.Element.Children.Count - 1; i >= 0; i--)
            {
                stack.Push((current.Element.Children[i], current.Depth + 1));
            }
        }
    }

    private static double CalculatePercentile(IReadOnlyList<long> orderedValues, double percentile)
    {
        if (orderedValues.Count == 0)
        {
            return 0;
        }

        if (orderedValues.Count == 1)
        {
            return orderedValues[0];
        }

        var position = (orderedValues.Count - 1) * percentile;
        var lowerIndex = (int)Math.Floor(position);
        var upperIndex = (int)Math.Ceiling(position);

        if (lowerIndex == upperIndex)
        {
            return orderedValues[lowerIndex];
        }

        var fraction = position - lowerIndex;
        return orderedValues[lowerIndex] + ((orderedValues[upperIndex] - orderedValues[lowerIndex]) * fraction);
    }
}
