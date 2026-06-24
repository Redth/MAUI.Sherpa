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
