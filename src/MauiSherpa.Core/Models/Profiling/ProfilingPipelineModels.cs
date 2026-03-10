using System;
using System.Collections.Generic;

namespace MauiSherpa.Core.Models.Profiling;

public enum ProfilingPipelineState
{
    NotStarted,
    Running,
    WaitingForStop,
    Completing,
    Completed,
    Failed,
    Cancelled
}

public enum ProfilingStepState
{
    Pending,
    WaitingForDependencies,
    Running,
    Completed,
    Failed,
    Stopped,
    Skipped,
    Cancelled
}

public class ProfilingStepStatus
{
    public required string StepId { get; init; }
    public required string DisplayName { get; init; }
    public required ProfilingCommandStepKind Kind { get; init; }
    public ProfilingStepState State { get; set; } = ProfilingStepState.Pending;
    public List<ProfilingStepOutputLine> OutputLines { get; } = new();
    public TimeSpan? Duration { get; set; }
    public int? ExitCode { get; set; }
    public int? ProcessId { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public bool IsLongRunning { get; init; }
    public bool CanRunParallel { get; init; }
    public ProfilingStopTrigger StopTrigger { get; init; }

    /// <summary>
    /// True when a long-running step has established its connection and is
    /// actively capturing. Used to gate dependent non-long-running steps
    /// (e.g. gcdump waits for trace to connect before running).
    /// </summary>
    public bool IsReady { get; set; }
}

public record ProfilingStepOutputLine(string Text, bool IsError, DateTime Timestamp);

public record ProfilingPipelineResult(
    bool Success,
    TimeSpan TotalDuration,
    ProfilingPipelineState FinalState,
    IReadOnlyList<ProfilingStepStatus> StepResults,
    IReadOnlyList<string> ArtifactPaths,
    IReadOnlyList<string> MissingArtifacts);

public class ProfilingPipelineStateChangedEventArgs : EventArgs
{
    public ProfilingPipelineState OldState { get; init; }
    public ProfilingPipelineState NewState { get; init; }
}

public class ProfilingStepStateChangedEventArgs : EventArgs
{
    public required string StepId { get; init; }
    public ProfilingStepState OldState { get; init; }
    public ProfilingStepState NewState { get; init; }
}

public class ProfilingStepOutputEventArgs : EventArgs
{
    public required string StepId { get; init; }
    public required string Text { get; init; }
    public bool IsError { get; init; }
}
