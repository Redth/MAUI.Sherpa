namespace MauiSherpa.Workloads.Models;

public enum DotnetWorkloadUpdateMode
{
    Unknown,
    WorkloadSet,
    Manifests
}

public enum DotnetWorkloadVersionSource
{
    Unknown,
    MachineDefault,
    GlobalJson,
    LooseManifests
}

public record DotnetWorkloadTarget
{
    public required string InstallRoot { get; init; }
    public required string DotnetPath { get; init; }
    public required string Architecture { get; init; }
    public required SdkFeatureBand FeatureBand { get; init; }
    public required string RepresentativeSdkVersion { get; init; }
    public bool IsManagedByDotnetUp { get; init; }
    public bool CanWrite { get; init; }

    public string RuntimeVersion => $"{FeatureBand.Major}.{FeatureBand.Minor}";
    public string Key => $"{InstallRoot}|{Architecture}|{FeatureBand}";
}

public record DotnetInstalledWorkload
{
    public required string Id { get; init; }
    public string? ManifestVersion { get; init; }
    public string? InstallationSource { get; init; }
}

public record DotnetWorkloadUpdate
{
    public required string WorkloadId { get; init; }
    public string? Description { get; init; }
    public string? ExistingManifestVersion { get; init; }
    public string? AvailableManifestVersion { get; init; }
}

public record DotnetWorkloadListResult
{
    public IReadOnlyList<DotnetInstalledWorkload> Installed { get; init; } = [];
    public IReadOnlyList<DotnetWorkloadUpdate> Updates { get; init; } = [];
    public bool UsedMachineReadableOutput { get; init; }
}

public record DotnetWorkloadSetVersion
{
    public required string Version { get; init; }
    public bool IsPrerelease { get; init; }
}

public record DotnetManifestVersion
{
    public required string ManifestId { get; init; }
    public required string Version { get; init; }
    public required string FeatureBand { get; init; }
}

public record ResolvedPackDefinition
{
    public required string Id { get; init; }
    public required string Version { get; init; }
    public required string Kind { get; init; }
    public string? ResolvedPackageId { get; init; }
}

public record ResolvedWorkloadDefinition
{
    public required string Id { get; init; }
    public string? Description { get; init; }
    public bool IsAbstract { get; init; }
    public string Kind { get; init; } = "dev";
    public IReadOnlyList<string> Platforms { get; init; } = [];
    public IReadOnlyList<string> DirectExtends { get; init; } = [];
    public IReadOnlyList<string> TransitiveIncludes { get; init; } = [];
    public IReadOnlyList<ResolvedPackDefinition> Packs { get; init; } = [];
    public string? RedirectTarget { get; init; }
}

public record DotnetWorkloadCapabilities
{
    public bool MachineReadableList { get; init; }
    public bool WorkloadVersion { get; init; }
    public bool JsonVersionSearch { get; init; }
}

public record DotnetWorkloadInventory
{
    public required DotnetWorkloadTarget Target { get; init; }
    public DotnetWorkloadUpdateMode UpdateMode { get; init; }
    public DotnetWorkloadVersionSource VersionSource { get; init; }
    public string? ActiveWorkloadVersion { get; init; }
    public IReadOnlyList<DotnetInstalledWorkload> InstalledWorkloads { get; init; } = [];
    public IReadOnlyList<ResolvedWorkloadDefinition> AvailableWorkloads { get; init; } = [];
    public IReadOnlyList<DotnetManifestVersion> ManifestVersions { get; init; } = [];
    public IReadOnlyList<DotnetWorkloadSetVersion> AvailableSetVersions { get; init; } = [];
    public IReadOnlyList<DotnetWorkloadUpdate> WorkloadUpdates { get; init; } = [];
    public DotnetWorkloadCapabilities Capabilities { get; init; } = new();
    public IReadOnlyList<string> Diagnostics { get; init; } = [];

    public string? LatestAvailableSetVersion => AvailableSetVersions
        .FirstOrDefault(version => Target.FeatureBand.IsPrerelease || !version.IsPrerelease)
        ?.Version;
    public bool UpdateAvailable =>
        UpdateMode == DotnetWorkloadUpdateMode.WorkloadSet &&
        LatestAvailableSetVersion is { } latest &&
        ActiveWorkloadVersion is { } active &&
        !string.Equals(latest, active, StringComparison.OrdinalIgnoreCase);
}

public record DotnetManifestVersionChange
{
    public required string ManifestId { get; init; }
    public string? CurrentVersion { get; init; }
    public string? TargetVersion { get; init; }
}

public record DotnetWorkloadSetPreview
{
    public required DotnetWorkloadTarget Target { get; init; }
    public string? CurrentVersion { get; init; }
    public required string TargetVersion { get; init; }
    public IReadOnlyList<DotnetManifestVersionChange> ManifestChanges { get; init; } = [];
    public IReadOnlyList<string> InstalledWorkloadIds { get; init; } = [];
    public required string CommandLine { get; init; }
}

public record WorkloadSelectionResult(
    IReadOnlyList<string> InstallIds,
    IReadOnlyList<string> UninstallIds);

public record GlobalJsonWorkloadPinPreview
{
    public required string Path { get; init; }
    public required string OriginalContent { get; init; }
    public required string UpdatedContent { get; init; }
    public string? WorkloadVersion { get; init; }
    public bool Changed => !string.Equals(OriginalContent, UpdatedContent, StringComparison.Ordinal);

    public string Diff =>
        WorkloadVersion == null
            ? $"- sdk.workloadVersion\n  {Path}"
            : $"+ sdk.workloadVersion: {WorkloadVersion}\n  {Path}";
}
