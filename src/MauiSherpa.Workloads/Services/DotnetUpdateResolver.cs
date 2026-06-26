using System.Text.RegularExpressions;
using MauiSherpa.Workloads.Models;
using Microsoft.Deployment.DotNet.Releases;

namespace MauiSherpa.Workloads.Services;

/// <summary>
/// Resolves dotnetup channels (e.g. <c>latest</c>, <c>lts</c>, <c>9.0.3xx</c>, <c>10.0</c>) to their
/// newest available version using <c>Microsoft.Deployment.DotNet.Releases</c>, and compares against
/// installed versions to compute an update preview. Performs no installs.
/// </summary>
public class DotnetUpdateResolver : IDotnetUpdateResolver
{
    private static readonly Regex ExactVersion = new(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$", RegexOptions.Compiled);
    private static readonly Regex FeatureBand = new(@"^(\d+)\.(\d+)\.(\d+)xx$", RegexOptions.Compiled);
    private static readonly Regex MajorMinor = new(@"^(\d+)\.(\d+)$", RegexOptions.Compiled);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DotnetUpdatePreview>> GetUpdatePreviewAsync(
        DotnetUpListResult list,
        CancellationToken cancellationToken = default)
    {
        var ctx = await ResolveContext.CreateAsync();

        var results = new List<DotnetUpdatePreview>();
        foreach (var spec in list.InstallSpecs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (available, installed, isPinned) = await ResolveCoreAsync(ctx, spec.Component, spec.VersionOrChannel, list);
            results.Add(new DotnetUpdatePreview
            {
                Component = spec.Component,
                Channel = spec.VersionOrChannel.Trim(),
                InstalledVersion = installed,
                AvailableVersion = available,
                UpdateAvailable = !isPinned && available != null && IsNewer(available, installed),
                IsPinned = isPinned,
            });
        }
        return results;
    }

    /// <inheritdoc />
    public async Task<(string? Available, string? Installed)> ResolveChannelAsync(
        DotnetUpComponent component,
        string channel,
        DotnetUpListResult list,
        CancellationToken cancellationToken = default)
    {
        var ctx = await ResolveContext.CreateAsync();
        var (available, installed, _) = await ResolveCoreAsync(ctx, component, channel, list);
        return (available, installed);
    }

    private static async Task<(string? Available, string? Installed, bool IsPinned)> ResolveCoreAsync(
        ResolveContext ctx, DotnetUpComponent component, string channelRaw, DotnetUpListResult list)
    {
        var channel = channelRaw.Trim();
        var lower = channel.ToLowerInvariant();
        var isSdk = component == DotnetUpComponent.Sdk;

        // Pinned to an exact version — no channel update applies.
        if (ExactVersion.IsMatch(channel))
        {
            var installedExact = list.Installations
                .Where(i => i.Component == component && VersionsEqual(i.Version, channel))
                .Select(i => i.Version)
                .FirstOrDefault();
            return (channel, installedExact ?? channel, true);
        }

        // Resolve the target product (major.minor) and optional feature band.
        Product? product = null;
        string? featureBand = null;

        if (lower == "latest")
        {
            product = ctx.Products
                .Where(p => string.IsNullOrEmpty(p.LatestSdkVersion.Prerelease))
                .OrderByDescending(p => p.LatestSdkVersion)
                .FirstOrDefault();
        }
        else if (lower == "lts")
        {
            product = ctx.Products
                .Where(p => p.ReleaseType == ReleaseType.LTS && string.IsNullOrEmpty(p.LatestSdkVersion.Prerelease))
                .OrderByDescending(p => p.LatestSdkVersion)
                .FirstOrDefault();
        }
        else if (lower == "sts")
        {
            product = ctx.Products
                .Where(p => p.ReleaseType == ReleaseType.STS && string.IsNullOrEmpty(p.LatestSdkVersion.Prerelease))
                .OrderByDescending(p => p.LatestSdkVersion)
                .FirstOrDefault();
        }
        else if (lower == "preview")
        {
            product = ctx.Products
                .Where(p => !string.IsNullOrEmpty(p.LatestSdkVersion.Prerelease))
                .OrderByDescending(p => p.LatestSdkVersion)
                .FirstOrDefault();
        }
        else if (FeatureBand.Match(channel) is { Success: true } fb)
        {
            var mm = $"{fb.Groups[1].Value}.{fb.Groups[2].Value}";
            featureBand = $"{mm}.{int.Parse(fb.Groups[3].Value) * 100}";
            product = ctx.Products.FirstOrDefault(p => p.ProductVersion == mm);
        }
        else if (MajorMinor.IsMatch(channel))
        {
            product = ctx.Products.FirstOrDefault(p => p.ProductVersion == channel);
        }
        else if (int.TryParse(channel, out var major))
        {
            product = ctx.Products
                .Where(p => ProductMajor(p) == major)
                .OrderByDescending(p => p.LatestSdkVersion)
                .FirstOrDefault();
        }

        if (product == null)
        {
            // Couldn't resolve — surface installed but no available version.
            return (null, BestInstalled(list, component, null, null), false);
        }

        // Resolve the newest available version for this component.
        string? available;
        if (isSdk)
        {
            if (featureBand != null)
            {
                var sdks = await ctx.AllSdksAsync(product);
                var match = sdks.Where(v => FeatureBandKey(v) == featureBand).ToList();
                available = match.Count > 0 ? match.Max()!.ToString() : null;
            }
            else
            {
                available = product.LatestSdkVersion.ToString();
            }
        }
        else if (component == DotnetUpComponent.AspNetCore)
        {
            var aspNet = await ctx.AllAspNetAsync(product);
            available = aspNet.Count > 0 ? aspNet.Max()!.ToString() : product.LatestRuntimeVersion.ToString();
        }
        else
        {
            // Runtime / WindowsDesktop track the shared runtime version.
            available = product.LatestRuntimeVersion.ToString();
        }

        var installed = BestInstalled(list, component, product.ProductVersion, featureBand);
        return (available, installed, false);
    }

    /// <summary>Holds the fetched product collection and lazily-loaded per-product release versions.</summary>
    private sealed class ResolveContext
    {
        public required ProductCollection Products { get; init; }
        private readonly Dictionary<string, List<ReleaseVersion>> _sdkCache = new();
        private readonly Dictionary<string, List<ReleaseVersion>> _aspNetCache = new();

        public static async Task<ResolveContext> CreateAsync() =>
            new() { Products = await ProductCollection.GetAsync() };

        public async Task<List<ReleaseVersion>> AllSdksAsync(Product p)
        {
            if (!_sdkCache.TryGetValue(p.ProductVersion, out var versions))
            {
                var releases = await p.GetReleasesAsync();
                versions = releases.SelectMany(r => r.Sdks).Select(s => s.Version).ToList();
                _sdkCache[p.ProductVersion] = versions;
            }
            return versions;
        }

        public async Task<List<ReleaseVersion>> AllAspNetAsync(Product p)
        {
            if (!_aspNetCache.TryGetValue(p.ProductVersion, out var versions))
            {
                var releases = await p.GetReleasesAsync();
                versions = releases.Where(r => r.AspNetCoreRuntime != null)
                    .Select(r => r.AspNetCoreRuntime!.Version)
                    .ToList();
                _aspNetCache[p.ProductVersion] = versions;
            }
            return versions;
        }
    }

    /// <summary>Newest installed version of <paramref name="component"/> matching a major.minor (and optional feature band).</summary>
    private static string? BestInstalled(DotnetUpListResult list, DotnetUpComponent component, string? majorMinor, string? featureBand)
    {
        return list.Installations
            .Where(i => i.Component == component)
            .Where(i => ReleaseVersion.TryParse(i.Version, out _))
            .Where(i => majorMinor == null || MajorMinorKey(i.Version) == majorMinor)
            .Where(i => featureBand == null || FeatureBandKey(new ReleaseVersion(i.Version)) == featureBand)
            .OrderByDescending(i => new ReleaseVersion(i.Version))
            .Select(i => i.Version)
            .FirstOrDefault();
    }

    private static int ProductMajor(Product p) =>
        int.TryParse(p.ProductVersion.Split('.')[0], out var m) ? m : 0;

    private static string FeatureBandKey(ReleaseVersion v) => $"{v.Major}.{v.Minor}.{v.SdkFeatureBand}";

    private static string MajorMinorKey(string version) =>
        ReleaseVersion.TryParse(version, out var v) && v != null ? $"{v.Major}.{v.Minor}" : version;

    private static bool VersionsEqual(string a, string b) =>
        ReleaseVersion.TryParse(a, out var va) && ReleaseVersion.TryParse(b, out var vb) && va != null && vb != null
            ? va.CompareTo(vb) == 0
            : string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool IsNewer(string available, string? installed)
    {
        if (string.IsNullOrEmpty(installed)) return true;
        if (ReleaseVersion.TryParse(available, out var a) && ReleaseVersion.TryParse(installed, out var i) && a != null && i != null)
            return a.CompareTo(i) > 0;
        return !string.Equals(available, installed, StringComparison.OrdinalIgnoreCase);
    }
}
