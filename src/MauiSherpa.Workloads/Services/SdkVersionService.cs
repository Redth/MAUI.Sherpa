using MauiSherpa.Workloads.Models;
using Microsoft.Deployment.DotNet.Releases;

namespace MauiSherpa.Workloads.Services;

/// <summary>
/// Service for querying available .NET SDK versions using Microsoft.Deployment.DotNet.Releases.
/// </summary>
public class SdkVersionService : ISdkVersionService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<SdkVersion>> GetAvailableSdkVersionsAsync(
        bool includePreview = false,
        CancellationToken cancellationToken = default)
    {
        var products = await ProductCollection.GetAsync();
        var sdkVersions = new List<SdkVersion>();

        foreach (var product in products)
        {
            var releases = await product.GetReleasesAsync();
            foreach (var release in releases)
            {
                if (!includePreview && release.IsPreview)
                    continue;

                foreach (var sdk in release.Sdks)
                {
                    try
                    {
                        var sdkVersion = SdkVersion.Parse(sdk.Version.ToString());
                        sdkVersions.Add(sdkVersion);
                    }
                    catch
                    {
                        // Skip malformed versions
                    }
                }
            }
        }

        return sdkVersions
            .OrderByDescending(v => v.Major)
            .ThenByDescending(v => v.Minor)
            .ThenByDescending(v => v.Patch)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SdkVersion>> GetSdkVersionsForRuntimeAsync(
        string runtimeVersion,
        bool includePreview = false,
        CancellationToken cancellationToken = default)
    {
        var parts = runtimeVersion.Split('.');
        if (parts.Length < 2)
            throw new ArgumentException($"Invalid runtime version format: {runtimeVersion}", nameof(runtimeVersion));

        var major = int.Parse(parts[0]);
        var minor = int.Parse(parts[1]);

        var products = await ProductCollection.GetAsync();
        var product = products.FirstOrDefault(p =>
            p.ProductVersion.StartsWith($"{major}.{minor}"));

        if (product == null)
            return [];

        var releases = await product.GetReleasesAsync();
        var sdkVersions = new List<SdkVersion>();

        foreach (var release in releases)
        {
            if (!includePreview && release.IsPreview)
                continue;

            foreach (var sdk in release.Sdks)
            {
                try
                {
                    var sdkVersion = SdkVersion.Parse(sdk.Version.ToString());
                    sdkVersions.Add(sdkVersion);
                }
                catch
                {
                    // Skip malformed versions
                }
            }
        }

        return sdkVersions
            .OrderByDescending(v => v.Patch)
            .ToList();
    }

    /// <inheritdoc />
    public string GetFeatureBand(string sdkVersion)
    {
        return SdkVersion.Parse(sdkVersion).FeatureBand;
    }
}
