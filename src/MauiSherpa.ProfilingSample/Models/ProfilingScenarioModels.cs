namespace MauiSherpa.ProfilingSample.Models;

public sealed record RenderTile(int Id, string Label, int Hue, double Scale, int LoadValue, bool IsHot);

public sealed record FeedItem(int Id, string Title, string Category, int ActiveUsers, int DurationMs, int Score, string Summary);

public sealed record MemorySnapshot(string Id, DateTimeOffset Timestamp, long RetainedBytes, long ManagedHeapBytes, int ChunkCount);

public sealed class NetworkRunResult
{
    public string Url { get; set; } = string.Empty;

    public int? StatusCode { get; set; }

    public bool Success { get; set; }

    public long DurationMs { get; set; }

    public string? Error { get; set; }

    public long Bytes { get; set; }
}

public sealed class NetworkBurstOptions
{
    public string Mode { get; set; } = "local";

    public int RequestCount { get; set; } = 12;
}

public sealed class CpuScenarioState
{
    public bool IsRunning { get; set; }

    public int WorkerCount { get; set; }

    public int DurationSeconds { get; set; }

    public long IterationsCompleted { get; set; }

    public double LastBatchMilliseconds { get; set; }

    public string Status { get; set; } = "Idle";

    public DateTimeOffset? StartedAt { get; set; }
}

public sealed class MemoryScenarioState
{
    public bool IsChurning { get; set; }

    public int ChunkCount { get; set; }

    public long RetainedBytes { get; set; }

    public long ManagedHeapBytes { get; set; }

    public string Status { get; set; } = "Idle";

    public IReadOnlyList<MemorySnapshot> Snapshots { get; set; } = Array.Empty<MemorySnapshot>();
}

public sealed class RenderingScenarioState
{
    public bool IsAnimating { get; set; }

    public int Frame { get; set; }

    public IReadOnlyList<RenderTile> Tiles { get; set; } = Array.Empty<RenderTile>();

    public IReadOnlyList<FeedItem> FeedItems { get; set; } = Array.Empty<FeedItem>();

    public string Status { get; set; } = "Ready";
}

public sealed class NetworkScenarioState
{
    public bool IsRunning { get; set; }

    public string LastMode { get; set; } = "Idle";

    public int SuccessCount { get; set; }

    public int FailureCount { get; set; }

    public double AverageDurationMs { get; set; }

    public long TotalBytes { get; set; }

    public string Status { get; set; } = "Idle";

    public IReadOnlyList<NetworkRunResult> RecentRequests { get; set; } = Array.Empty<NetworkRunResult>();
}
