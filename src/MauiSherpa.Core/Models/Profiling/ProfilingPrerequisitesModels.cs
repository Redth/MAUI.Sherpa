using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Models.Profiling;

public enum ProfilingPrerequisiteKind
{
    HostPlatform,
    DotNetSdk,
    DotNetTool,
    AndroidToolchain,
    AppleToolchain,
    WindowsToolchain,
    Other
}

public record ProfilingPrerequisiteContext(
    ProfilingTargetPlatform Platform,
    IReadOnlyList<ProfilingCaptureKind> CaptureKinds,
    string? WorkingDirectory,
    string? DotNetExecutablePath,
    DoctorContext DoctorContext
);

public record ProfilingPrerequisiteStatus(
    string Name,
    ProfilingPrerequisiteKind Kind,
    DependencyStatusType Status,
    bool IsRequired,
    string? RequiredVersion,
    string? RecommendedVersion,
    string? InstalledVersion,
    string? Message,
    string? DiscoveredBy = null,
    string? ExecutablePath = null,
    bool IsFixable = false,
    string? FixAction = null,
    string? SuggestedCommand = null
);

public record ProfilingPrerequisiteReport(
    ProfilingPrerequisiteContext Context,
    IReadOnlyList<ProfilingPrerequisiteStatus> Checks,
    DateTimeOffset Timestamp
)
{
    public bool IsReady => Checks
        .Where(check => check.IsRequired)
        .All(check => check.Status != DependencyStatusType.Error);

    public bool HasErrors => Checks.Any(check => check.Status == DependencyStatusType.Error);
    public bool HasWarnings => Checks.Any(check => check.Status == DependencyStatusType.Warning);
    public int OkCount => Checks.Count(check => check.Status == DependencyStatusType.Ok || check.Status == DependencyStatusType.Info);
    public int WarningCount => Checks.Count(check => check.Status == DependencyStatusType.Warning);
    public int ErrorCount => Checks.Count(check => check.Status == DependencyStatusType.Error);
}
