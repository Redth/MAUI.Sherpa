using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Models.Profiling;

public enum ProfilingCaptureLaunchMode
{
    Launch,
    Attach
}

public enum ProfilingCommandStepKind
{
    Prepare,
    Build,
    Launch,
    DiscoverProcess,
    Connect,
    Capture,
    CollectArtifacts,
    Cleanup
}

public enum ProfilingDiagnosticListenMode
{
    Connect,
    Listen
}

public enum ProfilingDsRouterMode
{
    None,
    ServerServer,
    ServerClient
}

public enum ProfilingStopTrigger
{
    None,           // Step exits on its own
    ManualStop,     // User must explicitly stop it
    OnPipelineStop  // Stopped when the pipeline stops
}

public record ProfilingCapturePlanOptions(
    string? ProjectPath = null,
    string Configuration = "Release",
    string? TargetFramework = null,
    string? WorkingDirectory = null,
    string? OutputDirectory = null,
    ProfilingCaptureLaunchMode LaunchMode = ProfilingCaptureLaunchMode.Launch,
    int DiagnosticPort = 9000,
    bool SuspendAtStartup = false,
    int? ProcessId = null,
    IReadOnlyDictionary<string, string>? AdditionalBuildProperties = null
);

public record ProfilingPlanValidation(
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings
)
{
    public bool IsValid => Errors.Count == 0;
}

public record ProfilingDiagnosticConfiguration(
    string Address,
    int Port,
    ProfilingDiagnosticListenMode ListenMode,
    bool SuspendOnStartup,
    bool RequiresDsRouter,
    ProfilingDsRouterMode DsRouterMode,
    string? DsRouterPortForwardPlatform,
    string IpcAddress,
    string TcpEndpoint
);

public record ProfilingRuntimeBinding(
    string Token,
    string Description,
    bool IsRequired = true,
    string? ExampleValue = null
);

public record ProfilingCommandStep(
    string Id,
    ProfilingCommandStepKind Kind,
    string DisplayName,
    string Description,
    string Command,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string>? Environment = null,
    IReadOnlyList<string>? DependsOn = null,
    IReadOnlyList<string>? RequiredRuntimeBindings = null,
    IReadOnlyDictionary<string, string>? Metadata = null,
    bool IsOptional = false,
    bool IsLongRunning = false,
    bool RequiresManualStop = false,
    bool CanRunParallel = false,
    ProfilingStopTrigger StopTrigger = ProfilingStopTrigger.None,
    string? ReadyOutputPattern = null)
{
    public string CommandLine => Arguments.Count > 0
        ? $"{Command} {string.Join(" ", Arguments)}"
        : Command;

    public ProcessRequest ToProcessRequest(string? title = null, string? description = null) => new(
        Command,
        Arguments.ToArray(),
        WorkingDirectory,
        Environment: Environment?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase),
        Title: title ?? DisplayName,
        Description: description ?? Description);
}

public record ProfilingCapturePlan(
    ProfilingSessionDefinition Session,
    ProfilingPlatformCapabilities Capabilities,
    ProfilingCapturePlanOptions Options,
    string HostPlatform,
    string TargetFramework,
    string OutputDirectory,
    string? WorkingDirectory,
    bool IsTargetCurrentlyAvailable,
    ProfilingDiagnosticConfiguration? Diagnostics,
    ProfilingPrerequisiteReport? Prerequisites,
    ProfilingPlanValidation Validation,
    IReadOnlyList<ProfilingRuntimeBinding> RuntimeBindings,
    IReadOnlyList<ProfilingCommandStep> Commands,
    IReadOnlyList<ProfilingArtifactMetadata> ExpectedArtifacts,
    IReadOnlyDictionary<string, string> Metadata)
{
    public bool RequiresRuntimeInputs => RuntimeBindings.Any(binding => binding.IsRequired);
    public bool CanExecute => Validation.IsValid && !RequiresRuntimeInputs;
}
