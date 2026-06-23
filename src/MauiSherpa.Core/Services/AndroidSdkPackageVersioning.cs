using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Describes how an Android SDK package group encodes its "version", which
/// determines whether a newer release is a genuine in-place update or just a
/// distinct, coexisting package.
/// </summary>
public enum SdkPackageVersioningStrategy
{
    /// <summary>
    /// The package Path is constant and the release version lives only in the
    /// Version field (e.g. <c>emulator</c>, <c>platform-tools</c>). A newer
    /// Version on the same Path is a real in-place update.
    /// </summary>
    FixedPath,

    /// <summary>
    /// The Path identifies a fixed target/variant (API level, ABI, etc.) and the
    /// Version field is an integer revision that bumps in place
    /// (e.g. <c>platforms;android-33</c>, <c>system-images;...</c>). A newer
    /// revision on the same Path is a real in-place update.
    /// </summary>
    Revision,

    /// <summary>
    /// The release version is embedded in the Path and multiple versions coexist
    /// side-by-side (e.g. <c>build-tools;36.1.0</c>, <c>ndk;27.x</c>,
    /// <c>cmake;3.22.1</c>). A newer release is a separate package, NOT an update.
    /// </summary>
    SideBySide
}

/// <summary>
/// Central, testable logic for classifying Android SDK package groups and
/// detecting genuine in-place updates.
///
/// An "update" is the same exact package Path with a newer stable Version. This
/// matches the Android SDK manager's own "Available Updates" computation:
/// FixedPath and Revision groups share a Path across versions (so a newer Version
/// is an update), while SideBySide groups encode the version in the Path (so a
/// newer release has a different Path and is intentionally never an update).
/// </summary>
public static class AndroidSdkPackageVersioning
{
    // Groups whose version lives only in the Version field (constant Path).
    private static readonly HashSet<string> FixedPathGroups = new(StringComparer.OrdinalIgnoreCase)
    {
        "emulator",
        "platform-tools",
        "tools",
        "docs",
        "ndk-bundle",
        "extras",
        "build",      // e.g. build;templates
        "patcher",
    };

    // Groups whose version is embedded in the Path (coexisting, side-by-side).
    private static readonly HashSet<string> SideBySideGroups = new(StringComparer.OrdinalIgnoreCase)
    {
        "build-tools",
        "ndk",
        "cmake",
    };

    // Groups where the Path is a fixed target/variant and the Version is an
    // updatable revision number.
    private static readonly HashSet<string> RevisionGroups = new(StringComparer.OrdinalIgnoreCase)
    {
        "platforms",
        "sources",
        "system-images",
        "skiaparser",
        "add-ons",
    };

    /// <summary>
    /// Returns the leading group segment of a package path (text before the
    /// first <c>;</c>), e.g. <c>system-images;android-33;...</c> → <c>system-images</c>.
    /// </summary>
    public static string GetGroup(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var separatorIndex = path.IndexOf(';');
        return separatorIndex < 0 ? path : path[..separatorIndex];
    }

    /// <summary>
    /// Classifies a package path into its versioning strategy.
    /// </summary>
    public static SdkPackageVersioningStrategy Classify(string? path)
    {
        var group = GetGroup(path);

        if (SideBySideGroups.Contains(group))
            return SdkPackageVersioningStrategy.SideBySide;

        if (FixedPathGroups.Contains(group))
            return SdkPackageVersioningStrategy.FixedPath;

        if (RevisionGroups.Contains(group))
            return SdkPackageVersioningStrategy.Revision;

        // cmdline-tools is special: "cmdline-tools;latest" is a fixed-path,
        // in-place package, while numbered ("cmdline-tools;13.0") entries are
        // side-by-side. Default the group to FixedPath but override numbered ones.
        if (string.Equals(group, "cmdline-tools", StringComparison.OrdinalIgnoreCase))
        {
            return IsCmdlineToolsLatest(path)
                ? SdkPackageVersioningStrategy.FixedPath
                : SdkPackageVersioningStrategy.SideBySide;
        }

        // Unknown groups: treat conservatively as Revision so exact-path updates
        // are still detected (same Path + newer Version), without ever surfacing
        // distinct paths as updates.
        return SdkPackageVersioningStrategy.Revision;
    }

    private static bool IsCmdlineToolsLatest(string? path)
    {
        var remainder = GetSubPath(path);
        return string.Equals(remainder, "latest", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetSubPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var separatorIndex = path.IndexOf(';');
        return separatorIndex < 0 ? string.Empty : path[(separatorIndex + 1)..];
    }

    /// <summary>
    /// True when a package group can ever produce in-place updates
    /// (FixedPath or Revision). SideBySide groups never can.
    /// </summary>
    public static bool SupportsInPlaceUpdate(string? path)
        => Classify(path) != SdkPackageVersioningStrategy.SideBySide;

    /// <summary>
    /// Finds the best available in-place update for an installed package, or null
    /// if none. An update is the same exact Path with a newer (stable, by default)
    /// Version. SideBySide packages never return an update.
    /// </summary>
    public static SdkPackageInfo? GetInPlaceUpdate(
        SdkPackageInfo installed,
        IEnumerable<SdkPackageInfo> availablePackages,
        bool requireStable = true)
    {
        if (installed is null || availablePackages is null)
            return null;

        if (string.IsNullOrWhiteSpace(installed.Version))
            return null;

        if (!SupportsInPlaceUpdate(installed.Path))
            return null;

        return availablePackages
            .Where(a => a is not null)
            .Where(a => string.Equals(a.Path, installed.Path, StringComparison.OrdinalIgnoreCase))
            .Where(a => !string.IsNullOrWhiteSpace(a.Version))
            .Where(a => CompareVersions(a.Version, installed.Version) > 0)
            .Where(a => !requireStable || IsStableVersion(a.Version))
            .OrderByDescending(a => a.Version, VersionComparer.Instance)
            .FirstOrDefault();
    }

    /// <summary>
    /// True when an installed package has a genuine in-place update available.
    /// </summary>
    public static bool HasInPlaceUpdate(
        SdkPackageInfo installed,
        IEnumerable<SdkPackageInfo> availablePackages,
        bool requireStable = true)
        => GetInPlaceUpdate(installed, availablePackages, requireStable) is not null;

    /// <summary>
    /// Finds a newer side-by-side release for an installed package, or null.
    ///
    /// Side-by-side groups (build-tools, ndk, cmake, numbered cmdline-tools) embed
    /// the version in the Path, so a newer release is a distinct, coexisting
    /// package rather than an in-place update. Detection is therefore anchored on
    /// the <em>newest installed</em> package in the group: this method returns the
    /// newest available (stable, by default) release in the same group, but only
    /// when <paramref name="installed"/> is that newest-installed anchor and the
    /// newest available is strictly newer. For every other (older, coexisting)
    /// installed package in the group it returns null, so only the anchor surfaces
    /// the hint and acts as the update target.
    /// </summary>
    public static SdkPackageInfo? GetSideBySideUpdate(
        SdkPackageInfo installed,
        IEnumerable<SdkPackageInfo> installedPackages,
        IEnumerable<SdkPackageInfo> availablePackages,
        bool requireStable = true)
    {
        if (installed is null || installedPackages is null || availablePackages is null)
            return null;

        if (string.IsNullOrWhiteSpace(installed.Version))
            return null;

        if (Classify(installed.Path) != SdkPackageVersioningStrategy.SideBySide)
            return null;

        var group = GetGroup(installed.Path);

        var newestInstalled = installedPackages
            .Where(p => p is not null)
            .Where(p => !string.IsNullOrWhiteSpace(p.Version))
            .Where(p => string.Equals(GetGroup(p.Path), group, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.Version, VersionComparer.Instance)
            .FirstOrDefault();

        if (newestInstalled is null)
            return null;

        // Only the newest installed package in the group is the anchor.
        if (!string.Equals(installed.Path, newestInstalled.Path, StringComparison.OrdinalIgnoreCase))
            return null;

        var newestAvailable = availablePackages
            .Where(a => a is not null)
            .Where(a => !string.IsNullOrWhiteSpace(a.Version))
            .Where(a => string.Equals(GetGroup(a.Path), group, StringComparison.OrdinalIgnoreCase))
            .Where(a => !requireStable || IsStableVersion(a.Version))
            .OrderByDescending(a => a.Version, VersionComparer.Instance)
            .FirstOrDefault();

        if (newestAvailable is not null
            && CompareVersions(newestAvailable.Version, newestInstalled.Version) > 0)
            return newestAvailable;

        return null;
    }

    /// <summary>
    /// Returns the available package to install in order to update
    /// <paramref name="installed"/>, honoring its versioning strategy: an in-place
    /// update for FixedPath/Revision groups, or the newest side-by-side release for
    /// SideBySide groups. Null when no update is available.
    /// </summary>
    public static SdkPackageInfo? GetUpdate(
        SdkPackageInfo installed,
        IEnumerable<SdkPackageInfo> installedPackages,
        IEnumerable<SdkPackageInfo> availablePackages,
        bool requireStable = true)
    {
        if (installed is null)
            return null;

        return Classify(installed.Path) == SdkPackageVersioningStrategy.SideBySide
            ? GetSideBySideUpdate(installed, installedPackages, availablePackages, requireStable)
            : GetInPlaceUpdate(installed, availablePackages, requireStable);
    }

    /// <summary>
    /// True when an installed package has any update available (in-place or a newer
    /// side-by-side release).
    /// </summary>
    public static bool HasUpdate(
        SdkPackageInfo installed,
        IEnumerable<SdkPackageInfo> installedPackages,
        IEnumerable<SdkPackageInfo> availablePackages,
        bool requireStable = true)
        => GetUpdate(installed, installedPackages, availablePackages, requireStable) is not null;

    /// <summary>
    /// Returns all installed packages that have an update available, honoring each
    /// package group's versioning strategy. In-place groups yield the installed
    /// package whose Path has a newer Version; side-by-side groups yield the newest
    /// installed package in the group when a newer release is available.
    /// </summary>
    public static IEnumerable<SdkPackageInfo> GetUpdates(
        IEnumerable<SdkPackageInfo> installedPackages,
        IEnumerable<SdkPackageInfo> availablePackages,
        bool requireStable = true)
    {
        if (installedPackages is null || availablePackages is null)
            return [];

        var installed = installedPackages.ToList();
        var available = availablePackages.ToList();
        return installed.Where(p => GetUpdate(p, installed, available, requireStable) is not null).ToList();
    }

    /// <summary>
    /// Compares two Android SDK version strings. Handles pure integer revisions
    /// (e.g. <c>3</c> vs <c>10</c>) and dotted semver (e.g. <c>36.4.10</c> vs
    /// <c>36.6.11</c>). Returns &gt;0 when <paramref name="version1"/> is newer.
    /// </summary>
    public static int CompareVersions(string? version1, string? version2)
    {
        var v1Empty = string.IsNullOrWhiteSpace(version1);
        var v2Empty = string.IsNullOrWhiteSpace(version2);
        if (v1Empty && v2Empty) return 0;
        if (v1Empty) return -1;
        if (v2Empty) return 1;

        var parts1 = version1!.Split(['.', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
        var parts2 = version2!.Split(['.', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
        var maxParts = Math.Max(parts1.Length, parts2.Length);

        for (var i = 0; i < maxParts; i++)
        {
            var p1 = i < parts1.Length ? parts1[i] : "0";
            var p2 = i < parts2.Length ? parts2[i] : "0";

            if (int.TryParse(p1, out var n1) && int.TryParse(p2, out var n2))
            {
                if (n1 != n2)
                    return n1 > n2 ? 1 : -1;
                continue;
            }

            var comparison = string.Compare(p1, p2, StringComparison.OrdinalIgnoreCase);
            if (comparison != 0)
                return comparison;
        }

        return 0;
    }

    private static readonly string[] PrereleaseMarkers =
    [
        "preview", "alpha", "beta", "rc", "dev", "canary", "nightly"
    ];

    /// <summary>
    /// True when a version string looks like a stable release. Prerelease markers
    /// are matched as whole, token-delimited segments (split on <c>. - _ space</c>)
    /// so they don't false-match substrings inside ordinary version numbers.
    /// </summary>
    public static bool IsStableVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return false;

        var tokens = version.Split(['.', '-', '_', ' '], StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            foreach (var marker in PrereleaseMarkers)
            {
                // Match a whole token ("rc") or a token that starts with the
                // marker followed by a digit ("rc1", "beta2", "alpha03").
                if (token.Equals(marker, StringComparison.OrdinalIgnoreCase))
                    return false;

                if (token.Length > marker.Length
                    && token.StartsWith(marker, StringComparison.OrdinalIgnoreCase)
                    && char.IsDigit(token[marker.Length]))
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// <see cref="IComparer{T}"/> over version strings using <see cref="CompareVersions"/>.
    /// </summary>
    public sealed class VersionComparer : IComparer<string?>
    {
        public static VersionComparer Instance { get; } = new();

        public int Compare(string? x, string? y) => CompareVersions(x, y);
    }
}
