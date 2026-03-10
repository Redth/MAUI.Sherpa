using System.Text.Json.Serialization;

namespace MauiSherpa.Core.Models.Profiling;

/// <summary>
/// Status of a profiling session.
/// </summary>
public enum ProfilingSessionStatus
{
    InProgress,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// Persistent manifest for a profiling session, stored as session.json in the session folder.
/// Contains all metadata needed to understand what was captured and how.
/// </summary>
public record ProfilingSessionManifest
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public ProfilingSessionStatus Status { get; set; } = ProfilingSessionStatus.InProgress;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Target device/simulator/desktop info.</summary>
    public required ProfilingSessionTarget Target { get; init; }

    /// <summary>Project info (null if attach mode).</summary>
    public ProfilingSessionProject? Project { get; init; }

    /// <summary>Capture kinds selected for this session.</summary>
    public required IReadOnlyList<ProfilingCaptureKind> CaptureKinds { get; init; }

    /// <summary>Capture options used.</summary>
    public required ProfilingSessionOptions Options { get; init; }

    /// <summary>Pipeline execution summary (populated after capture).</summary>
    public ProfilingSessionPipelineSummary? Pipeline { get; set; }

    /// <summary>Artifact files produced by this session.</summary>
    public List<ProfilingSessionArtifact> Artifacts { get; set; } = new();

    /// <summary>Path to the session folder on disk.</summary>
    [JsonIgnore]
    public string? DirectoryPath { get; set; }
}

/// <summary>
/// Target device/emulator/desktop for the profiling session.
/// </summary>
public record ProfilingSessionTarget
{
    public required ProfilingTargetPlatform Platform { get; init; }
    public required ProfilingTargetKind Kind { get; init; }
    public required string Identifier { get; init; }
    public required string DisplayName { get; init; }
}

/// <summary>
/// Project info for the profiling session (when using Launch mode).
/// </summary>
public record ProfilingSessionProject
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public required string Configuration { get; init; }
    public string? TargetFramework { get; init; }
}

/// <summary>
/// Options used for the profiling session.
/// </summary>
public record ProfilingSessionOptions
{
    public ProfilingCaptureLaunchMode LaunchMode { get; init; } = ProfilingCaptureLaunchMode.Launch;
    public int DiagnosticPort { get; init; } = 9000;
    public bool SuspendAtStartup { get; init; }
    public int? ProcessId { get; init; }
    public ProfilingScenarioKind Scenario { get; init; } = ProfilingScenarioKind.Launch;
}

/// <summary>
/// Summary of the pipeline execution results.
/// </summary>
public record ProfilingSessionPipelineSummary
{
    public bool Success { get; init; }
    public TimeSpan Duration { get; init; }
    public List<ProfilingSessionStepSummary> Steps { get; init; } = new();
}

/// <summary>
/// Summary of a single pipeline step.
/// </summary>
public record ProfilingSessionStepSummary
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string State { get; init; }
    public string? CommandLine { get; init; }
}

/// <summary>
/// An artifact file produced by the session.
/// </summary>
public record ProfilingSessionArtifact
{
    public required string FileName { get; init; }
    public required ProfilingArtifactKind Kind { get; init; }
    public long? SizeBytes { get; init; }
    public string? DisplayName { get; init; }
}
