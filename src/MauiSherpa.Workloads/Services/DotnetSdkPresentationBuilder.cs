using System.Text.RegularExpressions;
using MauiSherpa.Workloads.Models;
using NuGet.Versioning;

namespace MauiSherpa.Workloads.Services;

public static partial class DotnetSdkPresentationBuilder
{
    public static DotnetSdkManagerSummary Build(
        DotnetUpListResult? list,
        IReadOnlyList<DotnetWorkloadInventory>? workloadInventories = null,
        IReadOnlyList<DotnetUpdatePreview>? updatePreviews = null,
        GlobalJsonResolution? projectResolution = null)
    {
        var safeList = list ?? new DotnetUpListResult();
        var safeInventories = workloadInventories ?? [];
        var safePreviews = updatePreviews ?? [];

        return new DotnetSdkManagerSummary
        {
            InstalledGroups = BuildInstalledGroups(safeList.Installations, safeInventories),
            TrackedSdkChannels = BuildTrackedSdkChannels(safeList.InstallSpecs),
            TrackedNonSdkSpecs = BuildTrackedNonSdkSpecs(safeList.InstallSpecs),
            Updates = BuildUpdateSummary(updatePreviews),
            ProjectWorkloads = BuildProjectWorkloads(projectResolution, safeInventories)
        };
    }

    public static IReadOnlyList<DotnetWorkloadInventory> FilterProjectWorkloadInventories(
        GlobalJsonResolution? projectResolution,
        IReadOnlyList<DotnetWorkloadInventory>? workloadInventories)
    {
        if (projectResolution?.InstalledVersion is not { Length: > 0 } installedVersion ||
            workloadInventories == null ||
            !SdkFeatureBand.TryParse(installedVersion, out var featureBand))
        {
            return [];
        }

        return workloadInventories
            .Where(inventory => inventory.Target.FeatureBand.Equals(featureBand))
            .Where(inventory =>
                string.IsNullOrWhiteSpace(projectResolution.InstalledSdkInstallRoot) ||
                string.Equals(
                    inventory.Target.InstallRoot,
                    projectResolution.InstalledSdkInstallRoot,
                    StringComparison.OrdinalIgnoreCase))
            .Where(inventory =>
                string.IsNullOrWhiteSpace(projectResolution.InstalledSdkArchitecture) ||
                string.Equals(
                    inventory.Target.Architecture,
                    projectResolution.InstalledSdkArchitecture,
                    StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(inventory => inventory.Target.FeatureBand)
            .ThenBy(inventory => inventory.Target.InstallRoot, StringComparer.OrdinalIgnoreCase)
            .ThenBy(inventory => inventory.Target.Architecture, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Finds the single tracked SDK spec that owns an installed SDK. dotnetup uninstalls specs,
    /// rather than concrete SDK versions, so the UI must use this result for SDK removal.
    /// </summary>
    public static DotnetUpInstallSpec? FindTrackedSdkSpec(
        DotnetUpInstallation installation,
        IReadOnlyList<DotnetUpInstallSpec>? installSpecs)
    {
        ArgumentNullException.ThrowIfNull(installation);

        if (installation.Component != DotnetUpComponent.Sdk || installSpecs == null)
            return null;

        var matches = installSpecs
            .Where(spec => spec.Component == DotnetUpComponent.Sdk)
            .Where(spec => string.Equals(
                spec.InstallRoot,
                installation.InstallRoot,
                StringComparison.OrdinalIgnoreCase))
            .Where(spec => string.IsNullOrWhiteSpace(spec.Architecture) ||
                           string.Equals(
                               spec.Architecture,
                               installation.Architecture,
                               StringComparison.OrdinalIgnoreCase))
            .Where(spec => SdkSpecMatchesVersion(spec.VersionOrChannel, installation.Version))
            .Take(2)
            .ToList();

        return matches.Count == 1 ? matches[0] : null;
    }

    public static DotnetMajorMinorGroupSummary? BuildProjectInstalledGroup(
        GlobalJsonResolution? projectResolution,
        IReadOnlyList<DotnetUpInstallation>? installations,
        IReadOnlyList<DotnetWorkloadInventory>? workloadInventories)
    {
        if (projectResolution?.InstalledVersion is not { Length: > 0 } installedVersion)
            return null;

        var safeInstallations = installations ?? [];
        var sdkTargets = safeInstallations
            .Where(installation =>
                installation.Component == DotnetUpComponent.Sdk &&
                VersionsEqual(installation.Version, installedVersion))
            .Where(installation =>
                string.IsNullOrWhiteSpace(projectResolution.InstalledSdkInstallRoot) ||
                string.Equals(
                    installation.InstallRoot,
                    projectResolution.InstalledSdkInstallRoot,
                    StringComparison.OrdinalIgnoreCase))
            .Where(installation =>
                string.IsNullOrWhiteSpace(projectResolution.InstalledSdkArchitecture) ||
                string.Equals(
                    installation.Architecture,
                    projectResolution.InstalledSdkArchitecture,
                    StringComparison.OrdinalIgnoreCase))
            .Select(installation => (installation.InstallRoot, installation.Architecture))
            .Distinct(TargetKeyComparer.Instance)
            .ToList();

        if (sdkTargets.Count != 1)
            return null;

        var target = sdkTargets[0];
        var majorMinor = ToMajorMinor(installedVersion);
        var matchingInstallations = safeInstallations
            .Where(installation =>
                string.Equals(
                    installation.InstallRoot,
                    target.InstallRoot,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    installation.Architecture,
                    target.Architecture,
                    StringComparison.OrdinalIgnoreCase))
            .Where(installation => string.Equals(
                ToMajorMinor(installation.Version),
                majorMinor,
                StringComparison.OrdinalIgnoreCase))
            .Where(installation =>
                installation.Component != DotnetUpComponent.Sdk ||
                VersionsEqual(installation.Version, installedVersion))
            .OrderBy(installation => ComponentSortOrder(installation.Component))
            .ThenByDescending(installation => installation.Version, VersionStringComparer.Instance)
            .ToList();

        var matchingInventories = SdkFeatureBand.TryParse(installedVersion, out var featureBand)
            ? (workloadInventories ?? [])
                .Where(inventory => inventory.Target.FeatureBand.Equals(featureBand))
                .Where(inventory => string.Equals(
                    inventory.Target.InstallRoot,
                    target.InstallRoot,
                    StringComparison.OrdinalIgnoreCase))
                .Where(inventory => string.Equals(
                    inventory.Target.Architecture,
                    target.Architecture,
                    StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(
                    inventory => inventory.Target.RepresentativeSdkVersion,
                    VersionStringComparer.Instance)
                .ToList()
            : [];

        return new DotnetMajorMinorGroupSummary
        {
            MajorMinor = majorMinor,
            NewestSdkVersion = installedVersion,
            Counts = CountComponents(matchingInstallations),
            HasInvalidComponents = matchingInstallations.Any(installation => !installation.IsValid),
            WorkloadFeatureBandCount = matchingInventories
                .Select(inventory => inventory.Target.FeatureBand)
                .Distinct()
                .Count(),
            Installations = matchingInstallations,
            WorkloadInventories = matchingInventories
        };
    }

    private static IReadOnlyList<DotnetMajorMinorGroupSummary> BuildInstalledGroups(
        IReadOnlyList<DotnetUpInstallation> installations,
        IReadOnlyList<DotnetWorkloadInventory> workloadInventories)
    {
        return installations
            .GroupBy(installation => ToMajorMinor(installation.Version), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => ParseMajorMinor(group.Key), MajorMinorSortComparer.Instance)
            .ThenByDescending(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var orderedInstallations = group
                    .OrderBy(installation => ComponentSortOrder(installation.Component))
                    .ThenByDescending(installation => installation.Version, VersionStringComparer.Instance)
                    .ToList();

                var matchingWorkloads = workloadInventories
                    .Where(inventory => string.Equals(
                        inventory.Target.RuntimeVersion,
                        group.Key,
                        StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(inventory => inventory.Target.FeatureBand)
                    .ThenBy(inventory => inventory.Target.InstallRoot, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(inventory => inventory.Target.Architecture, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return new DotnetMajorMinorGroupSummary
                {
                    MajorMinor = group.Key,
                    NewestSdkVersion = orderedInstallations
                        .Where(installation => installation.Component == DotnetUpComponent.Sdk)
                        .Select(installation => installation.Version)
                        .FirstOrDefault(),
                    Counts = CountComponents(orderedInstallations),
                    HasInvalidComponents = orderedInstallations.Any(installation => !installation.IsValid),
                    WorkloadFeatureBandCount = matchingWorkloads
                        .Select(inventory => inventory.Target.FeatureBand)
                        .Distinct()
                        .Count(),
                    Installations = orderedInstallations,
                    WorkloadInventories = matchingWorkloads
                };
            })
            .ToList();
    }

    private static IReadOnlyList<DotnetTrackedSdkChannelSummary> BuildTrackedSdkChannels(
        IReadOnlyList<DotnetUpInstallSpec> installSpecs)
    {
        return installSpecs
            .Where(spec => spec.Component == DotnetUpComponent.Sdk)
            .Select(spec => new DotnetTrackedSdkChannelSummary
            {
                Channel = spec.VersionOrChannel,
                Source = spec.Source,
                GlobalJsonPath = spec.GlobalJsonPath,
                InstallRoot = spec.InstallRoot,
                Architecture = spec.Architecture,
                IsPinned = IsPinnedVersion(spec.VersionOrChannel)
            })
            .ToList();
    }

    private static IReadOnlyList<DotnetTrackedNonSdkSpecSummary> BuildTrackedNonSdkSpecs(
        IReadOnlyList<DotnetUpInstallSpec> installSpecs)
    {
        return installSpecs
            .Where(spec => spec.Component != DotnetUpComponent.Sdk)
            .OrderBy(spec => ComponentSortOrder(spec.Component))
            .ThenByDescending(spec => spec.VersionOrChannel, StringComparer.OrdinalIgnoreCase)
            .Select(spec => new DotnetTrackedNonSdkSpecSummary
            {
                Component = spec.Component,
                VersionOrChannel = spec.VersionOrChannel,
                Source = spec.Source,
                GlobalJsonPath = spec.GlobalJsonPath,
                InstallRoot = spec.InstallRoot,
                Architecture = spec.Architecture,
                IsPinned = IsPinnedVersion(spec.VersionOrChannel)
            })
            .ToList();
    }

    private static DotnetUpdateAggregateSummary BuildUpdateSummary(
        IReadOnlyList<DotnetUpdatePreview>? updatePreviews)
    {
        if (updatePreviews == null)
            return new DotnetUpdateAggregateSummary();

        var available = updatePreviews.Where(preview => preview.UpdateAvailable).ToList();
        var unresolvedCount = updatePreviews.Count(preview => !preview.IsPinned && preview.AvailableVersion == null);

        return new DotnetUpdateAggregateSummary
        {
            IsChecked = true,
            PreviewCount = updatePreviews.Count,
            AvailableUpdateCount = available.Count,
            SdkUpdateCount = available.Count(preview => preview.Component == DotnetUpComponent.Sdk),
            RuntimeUpdateCount = available.Count(preview => preview.Component != DotnetUpComponent.Sdk),
            UnresolvedCount = unresolvedCount,
            Previews = updatePreviews
        };
    }

    private static DotnetProjectWorkloadMatchSummary? BuildProjectWorkloads(
        GlobalJsonResolution? projectResolution,
        IReadOnlyList<DotnetWorkloadInventory> workloadInventories)
    {
        if (projectResolution?.InstalledVersion is not { Length: > 0 } installedVersion ||
            !SdkFeatureBand.TryParse(installedVersion, out var featureBand))
        {
            return null;
        }

        var matchingInventories = FilterProjectWorkloadInventories(projectResolution, workloadInventories);

        return new DotnetProjectWorkloadMatchSummary
        {
            InstalledVersion = installedVersion,
            FeatureBand = featureBand,
            InstallRoot = projectResolution.InstalledSdkInstallRoot,
            Architecture = projectResolution.InstalledSdkArchitecture,
            MatchingInventories = matchingInventories,
            SelectedInventory = matchingInventories.Count == 1 ? matchingInventories[0] : null
        };
    }

    private static DotnetInstalledComponentCounts CountComponents(IReadOnlyList<DotnetUpInstallation> installations)
    {
        return new DotnetInstalledComponentCounts
        {
            Total = installations.Count,
            Sdk = installations.Count(installation => installation.Component == DotnetUpComponent.Sdk),
            Runtime = installations.Count(installation => installation.Component == DotnetUpComponent.Runtime),
            AspNetCore = installations.Count(installation => installation.Component == DotnetUpComponent.AspNetCore),
            WindowsDesktop = installations.Count(installation => installation.Component == DotnetUpComponent.WindowsDesktop),
            Unknown = installations.Count(installation => installation.Component == DotnetUpComponent.Unknown)
        };
    }

    private static string ToMajorMinor(string version)
    {
        if (NuGetVersion.TryParse(version, out var parsed))
            return $"{parsed.Major}.{parsed.Minor}";

        var parts = version.Split('.');
        return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : version;
    }

    private static (int Major, int Minor) ParseMajorMinor(string majorMinor)
    {
        var parts = majorMinor.Split('.');
        return (
            parts.Length > 0 && int.TryParse(parts[0], out var major) ? major : -1,
            parts.Length > 1 && int.TryParse(parts[1], out var minor) ? minor : -1);
    }

    private static int ComponentSortOrder(DotnetUpComponent component) => component switch
    {
        DotnetUpComponent.Sdk => 0,
        DotnetUpComponent.Runtime => 1,
        DotnetUpComponent.AspNetCore => 2,
        DotnetUpComponent.WindowsDesktop => 3,
        _ => 4
    };

    private static bool IsPinnedVersion(string versionOrChannel) => ExactVersionPattern().IsMatch(versionOrChannel.Trim());

    private static bool VersionsEqual(string left, string right) =>
        NuGetVersion.TryParse(left, out var leftVersion) &&
        NuGetVersion.TryParse(right, out var rightVersion)
            ? leftVersion == rightVersion
            : string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool SdkSpecMatchesVersion(string versionOrChannel, string installedVersion)
    {
        var spec = versionOrChannel.Trim();
        if (VersionsEqual(spec, installedVersion))
            return true;

        if (!NuGetVersion.TryParse(installedVersion, out var installed))
            return false;

        if (SdkFeatureBandPattern().Match(spec) is { Success: true } featureBand &&
            int.TryParse(featureBand.Groups[1].Value, out var featureMajor) &&
            int.TryParse(featureBand.Groups[2].Value, out var featureMinor) &&
            int.TryParse(featureBand.Groups[3].Value, out var featureBandHundreds))
        {
            return installed.Major == featureMajor &&
                   installed.Minor == featureMinor &&
                   installed.Patch / 100 == featureBandHundreds;
        }

        if (MajorMinorPattern().Match(spec) is { Success: true } majorMinor &&
            int.TryParse(majorMinor.Groups[1].Value, out var major) &&
            int.TryParse(majorMinor.Groups[2].Value, out var minor))
        {
            return installed.Major == major && installed.Minor == minor;
        }

        return int.TryParse(spec, out var majorOnly) && installed.Major == majorOnly;
    }

    private sealed class MajorMinorSortComparer : IComparer<(int Major, int Minor)>
    {
        public static MajorMinorSortComparer Instance { get; } = new();

        public int Compare((int Major, int Minor) x, (int Major, int Minor) y)
        {
            var major = x.Major.CompareTo(y.Major);
            return major != 0 ? major : x.Minor.CompareTo(y.Minor);
        }
    }

    private sealed class VersionStringComparer : IComparer<string>
    {
        public static VersionStringComparer Instance { get; } = new();

        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y))
                return 0;
            if (x is null)
                return -1;
            if (y is null)
                return 1;

            if (NuGetVersion.TryParse(x, out var left) && NuGetVersion.TryParse(y, out var right))
                return left.CompareTo(right);

            return StringComparer.OrdinalIgnoreCase.Compare(x, y);
        }
    }

    private sealed class TargetKeyComparer : IEqualityComparer<(string InstallRoot, string Architecture)>
    {
        public static TargetKeyComparer Instance { get; } = new();

        public bool Equals(
            (string InstallRoot, string Architecture) x,
            (string InstallRoot, string Architecture) y) =>
            string.Equals(x.InstallRoot, y.InstallRoot, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Architecture, y.Architecture, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string InstallRoot, string Architecture) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.InstallRoot),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Architecture));
    }

    [GeneratedRegex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$", RegexOptions.Compiled)]
    private static partial Regex ExactVersionPattern();

    [GeneratedRegex(@"^(\d+)\.(\d+)\.(\d+)xx$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex SdkFeatureBandPattern();

    [GeneratedRegex(@"^(\d+)\.(\d+)$", RegexOptions.Compiled)]
    private static partial Regex MajorMinorPattern();
}
