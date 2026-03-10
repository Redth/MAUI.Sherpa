namespace MauiSherpa.Core.Models.Profiling;

public enum ProfilingAnalysisKind
{
    Metadata,
    Speedscope,
    Logs,
    Json,
    GcDump
}

public enum ProfilingAnalysisInsightSeverity
{
    Info,
    Warning,
    Critical
}

public record ProfilingArtifactAnalysisResult(
    ProfilingArtifactAnalysis? Analysis,
    string? Message = null
);

public record ProfilingArtifactAnalysis(
    ProfilingArtifactMetadata Artifact,
    string? ArtifactPath,
    bool ArtifactExists,
    ProfilingAnalysisKind Kind,
    string Summary,
    IReadOnlyList<ProfilingAnalysisMetric> Metrics,
    IReadOnlyList<ProfilingAnalysisHotspot> Hotspots,
    IReadOnlyList<ProfilingAnalysisInsight> Insights,
    IReadOnlyList<string> Notes
);

public record ProfilingAnalysisMetric(
    string Key,
    string Label,
    string Value,
    double? NumericValue = null,
    string? Unit = null
);

public record ProfilingAnalysisHotspot(
    string Name,
    string Value,
    double PercentOfTrace,
    string? Description = null
);

public record ProfilingAnalysisInsight(
    ProfilingAnalysisInsightSeverity Severity,
    string Title,
    string Description
);

// GC dump report data
public record GcDumpReport(
    IReadOnlyList<GcDumpTypeEntry> Types,
    long TotalSize,
    long TotalCount,
    string? RawOutput = null
);

public record GcDumpTypeEntry(
    string TypeName,
    long Count,
    long Size
);
