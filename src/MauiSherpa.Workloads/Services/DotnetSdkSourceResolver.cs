using System.Runtime.InteropServices;
using MauiSherpa.Workloads.Models;

namespace MauiSherpa.Workloads.Services;

/// <summary>
/// The .NET install root that surfaces such as Doctor should treat as authoritative, along with
/// the SDKs it contains.
/// </summary>
public sealed record DotnetSdkSource
{
    /// <summary>SDK versions found in <see cref="InstallRoot"/>, newest first.</summary>
    public IReadOnlyList<SdkVersion> Sdks { get; init; } = [];

    /// <summary>The install root the SDKs belong to, or null when none could be resolved.</summary>
    public string? InstallRoot { get; init; }

    /// <summary>The architecture of the install root (e.g. <c>arm64</c>).</summary>
    public string? Architecture { get; init; }

    /// <summary>True when the root is managed by dotnetup rather than discovered on the machine.</summary>
    public bool IsDotnetUpManaged { get; init; }
}

/// <summary>
/// Decides which .NET install root is authoritative.
///
/// When the user has opted into dotnetup (the tool is installed and manages at least one valid
/// SDK), the dotnetup-managed root wins outright: dotnetup's Terminal Mode points <c>PATH</c> and
/// <c>DOTNET_ROOT</c> at it, so it is the SDK the user actually builds with. Mixing it with a
/// machine-wide install at <c>/usr/local/share/dotnet</c> produces an SDK/feature-band pair that
/// matches neither root, which in turn breaks workload-state lookups.
/// </summary>
public static class DotnetSdkSourceResolver
{
    /// <summary>
    /// Resolves the authoritative source from a machine scan and dotnetup's reported state.
    /// </summary>
    /// <param name="localSdks">SDKs discovered by scanning the machine's default install root.</param>
    /// <param name="localInstallRoot">The machine's default install root, if one was found.</param>
    /// <param name="dotnetUpList">dotnetup's <c>list --format Json</c> result, if available.</param>
    /// <param name="preferredArchitecture">
    /// Architecture to prefer when dotnetup manages more than one; defaults to the process architecture.
    /// </param>
    public static DotnetSdkSource Resolve(
        IReadOnlyList<SdkVersion> localSdks,
        string? localInstallRoot,
        DotnetUpListResult? dotnetUpList,
        string? preferredArchitecture = null)
    {
        var managed = ResolveManaged(dotnetUpList, preferredArchitecture);
        if (managed != null)
            return managed;

        return new DotnetSdkSource
        {
            Sdks = SdkVersion.SortDescending(localSdks),
            InstallRoot = localInstallRoot,
            Architecture = preferredArchitecture ?? CurrentArchitecture,
            IsDotnetUpManaged = false
        };
    }

    private static DotnetSdkSource? ResolveManaged(
        DotnetUpListResult? dotnetUpList, string? preferredArchitecture)
    {
        if (dotnetUpList == null)
            return null;

        var wanted = string.IsNullOrWhiteSpace(preferredArchitecture)
            ? CurrentArchitecture
            : preferredArchitecture;

        var groups = dotnetUpList.Installations
            .Where(installation =>
                installation.Component == DotnetUpComponent.Sdk &&
                installation.IsValid &&
                !string.IsNullOrWhiteSpace(installation.InstallRoot) &&
                SdkVersion.TryParse(installation.Version, out _))
            .GroupBy(
                installation => (
                    Root: installation.InstallRoot,
                    Architecture: installation.Architecture ?? string.Empty),
                TupleComparer)
            .Select(group => new DotnetSdkSource
            {
                Sdks = SdkVersion.SortDescending(
                    group
                        .Select(installation =>
                            SdkVersion.TryParse(installation.Version, out var parsed) ? parsed : null)
                        .Where(version => version != null)
                        .Select(version => version!)
                        .DistinctBy(version => version.Version, StringComparer.OrdinalIgnoreCase)),
                InstallRoot = group.Key.Root,
                Architecture = string.IsNullOrWhiteSpace(group.Key.Architecture)
                    ? wanted
                    : group.Key.Architecture,
                IsDotnetUpManaged = true
            })
            .Where(source => source.Sdks.Count > 0)
            .ToList();

        if (groups.Count == 0)
            return null;

        return groups
            .OrderByDescending(source => string.Equals(
                source.Architecture, wanted, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(source => source.Sdks[0].SemanticVersion)
            .ThenBy(source => source.InstallRoot, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static string CurrentArchitecture =>
        RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

    private static readonly IEqualityComparer<(string Root, string Architecture)> TupleComparer =
        new RootArchitectureComparer();

    private sealed class RootArchitectureComparer : IEqualityComparer<(string Root, string Architecture)>
    {
        public bool Equals((string Root, string Architecture) x, (string Root, string Architecture) y) =>
            StringComparer.OrdinalIgnoreCase.Equals(x.Root, y.Root) &&
            StringComparer.OrdinalIgnoreCase.Equals(x.Architecture, y.Architecture);

        public int GetHashCode((string Root, string Architecture) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Root),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Architecture));
    }
}
