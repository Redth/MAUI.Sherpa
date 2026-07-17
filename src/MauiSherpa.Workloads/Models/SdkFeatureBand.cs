using NuGet.Versioning;

namespace MauiSherpa.Workloads.Models;

/// <summary>
/// Identifies the SDK feature band that owns workload state. Prerelease SDKs are
/// isolated by their first two prerelease labels, matching the .NET SDK resolver.
/// </summary>
public readonly record struct SdkFeatureBand : IComparable<SdkFeatureBand>
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public string? Prerelease { get; }

    public bool IsPrerelease => Prerelease != null;

    public SdkFeatureBand(string version)
    {
        if (!NuGetVersion.TryParse(version, out var parsed))
            throw new ArgumentException($"Invalid SDK version: {version}", nameof(version));

        Major = parsed.Major;
        Minor = parsed.Minor;
        Patch = (parsed.Patch / 100) * 100;

        var labels = parsed.ReleaseLabels.ToArray();
        var release = string.Join('.', labels);
        Prerelease = labels.Length == 0 ||
                     release.Contains("dev", StringComparison.OrdinalIgnoreCase) ||
                     release.Contains("ci", StringComparison.OrdinalIgnoreCase) ||
                     release.Contains("rtm", StringComparison.OrdinalIgnoreCase)
            ? null
            : string.Join('.', labels.Take(2));
    }

    public static bool TryParse(string? version, out SdkFeatureBand featureBand)
    {
        if (!string.IsNullOrWhiteSpace(version) && NuGetVersion.TryParse(version, out _))
        {
            featureBand = new SdkFeatureBand(version);
            return true;
        }

        featureBand = default;
        return false;
    }

    public string ToStringWithoutPrerelease() => $"{Major}.{Minor}.{Patch}";

    public override string ToString() =>
        Prerelease == null
            ? ToStringWithoutPrerelease()
            : $"{ToStringWithoutPrerelease()}-{Prerelease}";

    public int CompareTo(SdkFeatureBand other)
    {
        var core = Major.CompareTo(other.Major);
        if (core != 0) return core;
        core = Minor.CompareTo(other.Minor);
        if (core != 0) return core;
        core = Patch.CompareTo(other.Patch);
        if (core != 0) return core;

        if (Prerelease == null && other.Prerelease != null) return 1;
        if (Prerelease != null && other.Prerelease == null) return -1;
        return StringComparer.OrdinalIgnoreCase.Compare(Prerelease, other.Prerelease);
    }
}
