namespace MauiSherpa.Core.Models.Profiling;

public record ProfilingTargetInfo(
    string Id,
    string Host,
    int Port,
    string DisplayName,
    string? AppName,
    string? Platform,
    string? Project,
    string? Tfm,
    DateTimeOffset? ConnectedAt,
    bool Running,
    string DiscoveryMethod
);

public record ProfilingSnapshotOptions(
    string? TargetId = null,
    int NetworkSampleSize = 40,
    bool IncludeNetworkSummary = true,
    bool IncludeVisualTreeSummary = true
);

public record ProfilingSnapshotResult(
    ProfilingSnapshot? Snapshot,
    string? Message = null
);

public record ProfilingSnapshot(
    ProfilingTargetInfo Target,
    ProfilingRuntimeInfo Runtime,
    ProfilingNetworkSummary? Network,
    ProfilingVisualTreeSummary? VisualTree,
    IReadOnlyList<string> Notes
);

public record ProfilingRuntimeInfo(
    string? Agent,
    string? Version,
    string? Platform,
    string? DeviceType,
    string? Idiom,
    string? AppName,
    bool Running,
    bool CdpReady,
    int CdpWebViewCount
);

public record ProfilingNetworkSummary(
    int SampleSize,
    int SuccessCount,
    int FailureCount,
    double AverageDurationMs,
    double P95DurationMs,
    long MaxDurationMs,
    long TotalRequestBytes,
    long TotalResponseBytes,
    IReadOnlyList<ProfilingRequestSummary> SlowestRequests
);

public record ProfilingRequestSummary(
    string Method,
    string Url,
    int? StatusCode,
    long DurationMs,
    string? Error
);

public record ProfilingVisualTreeSummary(
    int RootCount,
    int TotalElementCount,
    int VisibleElementCount,
    int FocusedElementCount,
    int InteractiveElementCount,
    int MaxDepth,
    IReadOnlyList<ProfilingElementTypeCount> TopElementTypes
);

public record ProfilingElementTypeCount(
    string Type,
    int Count
);
