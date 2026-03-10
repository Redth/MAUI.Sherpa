namespace MauiSherpa.Core.Models.Profiling;

public enum ProfilingTargetPlatform
{
    Android,
    iOS,
    MacCatalyst,
    MacOS,
    Windows
}

public enum ProfilingTargetKind
{
    PhysicalDevice,
    Emulator,
    Simulator,
    Desktop
}

public enum ProfilingCaptureKind
{
    Startup,
    Cpu,
    Memory,
    Network,
    Rendering,
    Energy,
    SystemTrace,
    Logs
}

public enum ProfilingScenarioKind
{
    Launch,
    Interaction,
    Scrolling,
    BackgroundWork,
    MemoryInvestigation
}

public enum ProfilingArtifactKind
{
    Trace,
    Metrics,
    Screenshot,
    Logs,
    Report,
    Export,
    GcDump,
    Log,
    Other
}

public record ProfilingTarget(
    ProfilingTargetPlatform Platform,
    ProfilingTargetKind Kind,
    string Identifier,
    string DisplayName,
    string? OperatingSystemVersion = null,
    string? Model = null
);

public record ProfilingScenarioDefinition(
    ProfilingScenarioKind Kind,
    string DisplayName,
    string Description,
    IReadOnlyList<ProfilingCaptureKind> DefaultCaptureKinds,
    TimeSpan SuggestedDuration,
    bool SupportsContinuousCapture = false
);

public record ProfilingPlatformCapabilities(
    ProfilingTargetPlatform Platform,
    string DisplayName,
    IReadOnlyList<ProfilingTargetKind> SupportedTargetKinds,
    IReadOnlyList<ProfilingCaptureKind> SupportedCaptureKinds,
    IReadOnlyList<ProfilingArtifactKind> SupportedArtifactKinds,
    IReadOnlyList<ProfilingScenarioKind> SupportedScenarios,
    bool SupportsLaunchProfiling,
    bool SupportsAttachToProcess,
    bool SupportsLiveMetrics,
    bool SupportsSymbolication,
    string? Notes = null
);

public record ProfilingSessionDefinition(
    string Id,
    string Name,
    ProfilingTarget Target,
    ProfilingScenarioKind Scenario,
    IReadOnlyList<ProfilingCaptureKind> CaptureKinds,
    string? AppId = null,
    TimeSpan? Duration = null,
    IReadOnlyDictionary<string, string>? Tags = null,
    DateTimeOffset CreatedAt = default
);

public record ProfilingSessionValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<ProfilingCaptureKind> UnsupportedCaptureKinds
);

public record ProfilingArtifactMetadata(
    string Id,
    string SessionId,
    ProfilingArtifactKind Kind,
    string DisplayName,
    string FileName,
    string? RelativePath,
    string ContentType,
    DateTimeOffset CreatedAt,
    long? SizeBytes = null,
    IReadOnlyDictionary<string, string>? Properties = null
);

public record ProfilingCatalog(
    IReadOnlyList<ProfilingPlatformCapabilities> Platforms,
    IReadOnlyList<ProfilingScenarioDefinition> Scenarios
);
