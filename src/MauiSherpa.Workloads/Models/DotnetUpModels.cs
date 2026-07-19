namespace MauiSherpa.Workloads.Models;

/// <summary>
/// The type of .NET component managed by dotnetup.
/// Mirrors the <c>component</c> field of <c>dotnetup list --format Json</c>.
/// </summary>
public enum DotnetUpComponent
{
    Sdk,
    Runtime,
    AspNetCore,
    WindowsDesktop,
    Unknown
}

/// <summary>
/// How a dotnetup install spec was registered.
/// Mirrors the <c>source</c> field of an install spec.
/// </summary>
public enum DotnetUpInstallSource
{
    Explicit,
    GlobalJson,
    All,
    Unknown
}

/// <summary>
/// A concrete .NET version installed and managed by dotnetup.
/// Mirrors an entry in the <c>installations</c> array of <c>dotnetup list --format Json</c>.
/// </summary>
public record DotnetUpInstallation
{
    public required DotnetUpComponent Component { get; init; }

    /// <summary>Raw component string as reported by dotnetup (e.g. "SDK", "ASPNETCore").</summary>
    public required string ComponentRaw { get; init; }

    public required string Version { get; init; }

    public required string InstallRoot { get; init; }

    public string? Architecture { get; init; }

    public bool IsValid { get; init; } = true;

    /// <summary>Framework name for runtimes (e.g. "Microsoft.NETCore.App"). Null for SDKs.</summary>
    public string? FrameworkName { get; init; }
}

/// <summary>
/// A tracked dotnetup install spec (channel) that determines what gets installed/updated.
/// Mirrors an entry in the <c>installSpecs</c> array of <c>dotnetup list --format Json</c>.
/// </summary>
public record DotnetUpInstallSpec
{
    public required DotnetUpComponent Component { get; init; }

    /// <summary>Raw component string as reported by dotnetup (e.g. "SDK", "ASPNETCore").</summary>
    public required string ComponentRaw { get; init; }

    /// <summary>The channel or pinned version (e.g. "latest", "10.0.1xx", "9.0.304").</summary>
    public required string VersionOrChannel { get; init; }

    public DotnetUpInstallSource Source { get; init; } = DotnetUpInstallSource.Explicit;

    /// <summary>Path to the global.json that introduced this spec, when <see cref="Source"/> is GlobalJson.</summary>
    public string? GlobalJsonPath { get; init; }

    public required string InstallRoot { get; init; }

    public string? Architecture { get; init; }
}

/// <summary>
/// Parsed result of <c>dotnetup list --format Json</c>.
/// </summary>
public record DotnetUpListResult
{
    public IReadOnlyList<DotnetUpInstallSpec> InstallSpecs { get; init; } = Array.Empty<DotnetUpInstallSpec>();

    public IReadOnlyList<DotnetUpInstallation> Installations { get; init; } = Array.Empty<DotnetUpInstallation>();

    /// <summary>
    /// The dotnetup-managed install root(s) discovered from the listed entries.
    /// </summary>
    public IReadOnlyList<string> InstallRoots =>
        InstallSpecs.Select(s => s.InstallRoot)
            .Concat(Installations.Select(i => i.InstallRoot))
            .Where(r => !string.IsNullOrEmpty(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}

/// <summary>
/// Diagnostic information about the dotnetup tool itself, parsed from <c>dotnetup --info</c>.
/// </summary>
public record DotnetUpToolInfo
{
    public required string Version { get; init; }

    public string? Commit { get; init; }

    public string? Architecture { get; init; }

    public string? Rid { get; init; }
}

/// <summary>
/// Metadata for the latest dotnetup artifact published through aka.ms.
/// </summary>
public record DotnetUpPublishedArtifact
{
    public required string Version { get; init; }

    public required string Sha512 { get; init; }
}

/// <summary>
/// Read-only comparison of the installed dotnetup binary with the latest published artifact.
/// </summary>
public record DotnetUpToolUpdateInfo
{
    public required string InstalledVersion { get; init; }

    public required string AvailableVersion { get; init; }

    public required bool UpdateAvailable { get; init; }
}

/// <summary>
/// Preview of whether a tracked channel has a newer version available, computed by resolving the
/// channel against the official .NET release metadata — <b>without</b> running any install/update.
/// </summary>
public record DotnetUpdatePreview
{
    public required DotnetUpComponent Component { get; init; }

    /// <summary>The tracked channel or pinned version this preview was computed for (e.g. "latest", "9.0.3xx").</summary>
    public required string Channel { get; init; }

    /// <summary>The newest installed version that currently satisfies this channel, or null when none matched.</summary>
    public string? InstalledVersion { get; init; }

    /// <summary>The newest available version the channel resolves to, or null when it couldn't be resolved (offline/unknown).</summary>
    public string? AvailableVersion { get; init; }

    /// <summary>True when <see cref="AvailableVersion"/> is newer than <see cref="InstalledVersion"/>.</summary>
    public bool UpdateAvailable { get; init; }

    /// <summary>True when the spec is a pinned exact version, so no channel update applies.</summary>
    public bool IsPinned { get; init; }
}

/// <summary>
/// The outcome of resolving a folder's <c>global.json</c> context.
/// </summary>
public enum GlobalJsonStatus
{
    /// <summary>No <c>global.json</c> was found walking up from the folder.</summary>
    NoGlobalJson,

    /// <summary>A <c>global.json</c> was found but it has no <c>sdk.version</c>, so no channel is pinned.</summary>
    NoSdkVersion,

    /// <summary>A channel was derived and resolved against the release feed.</summary>
    Resolved,

    /// <summary>A channel was derived but it couldn't be resolved (offline / unknown).</summary>
    Unresolved,
}

/// <summary>
/// Read-only resolution of the .NET SDK a project folder requires via its <c>global.json</c> —
/// computed without running dotnetup. dotnetup walks up from a directory to the nearest
/// <c>global.json</c> (the same algorithm as the <c>dotnet</c> host) and maps
/// <c>sdk.version</c> + <c>sdk.rollForward</c> to a channel.
/// </summary>
public record GlobalJsonResolution
{
    /// <summary>The folder the user picked to inspect.</summary>
    public required string FolderPath { get; init; }

    /// <summary>Full path to the nearest <c>global.json</c> found walking up, or null when none exists.</summary>
    public string? GlobalJsonPath { get; init; }

    /// <summary>The <c>sdk.version</c> value from <c>global.json</c>, if present.</summary>
    public string? RequestedVersion { get; init; }

    /// <summary>The <c>sdk.rollForward</c> policy (e.g. "latestPatch"), defaulted to "latestPatch" when omitted.</summary>
    public string? RollForward { get; init; }

    /// <summary>The <c>sdk.allowPrerelease</c> value, if specified.</summary>
    public bool? AllowPrerelease { get; init; }

    /// <summary>The official <c>sdk.workloadVersion</c> value, or a legacy value when present.</summary>
    public string? WorkloadVersion { get; init; }

    public bool UsesLegacyWorkloadSetProperty { get; init; }

    /// <summary>The dotnetup channel derived from version + rollForward (e.g. "10.0.1xx", "latest", "10.0.100").</summary>
    public string? Channel { get; init; }

    /// <summary>True when the derived channel is a pinned exact version that never auto-updates.</summary>
    public bool IsPinned { get; init; }

    /// <summary>The newest version the channel resolves to from the release feed, or null when unresolved.</summary>
    public string? ResolvedVersion { get; init; }

    /// <summary>The newest installed SDK that satisfies the channel, or null when none is installed.</summary>
    public string? InstalledVersion { get; init; }

    /// <summary>The install root containing <see cref="InstalledVersion"/>, when resolved.</summary>
    public string? InstalledSdkInstallRoot { get; init; }

    /// <summary>The architecture of the resolved installed SDK, when reported by dotnetup.</summary>
    public string? InstalledSdkArchitecture { get; init; }

    /// <summary>True when an installed SDK satisfies this project's requirement.</summary>
    public bool Satisfied { get; init; }

    /// <summary>True when the project's rolling channel resolves to a newer SDK than the installed match.</summary>
    public bool UpdateAvailable { get; init; }

    /// <summary>True when dotnetup already tracks a spec sourced from this <c>global.json</c> (or matching channel).</summary>
    public bool AlreadyTracked { get; init; }

    /// <summary>Overall resolution status.</summary>
    public required GlobalJsonStatus Status { get; init; }
}
