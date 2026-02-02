using NuGet.Versioning;

namespace MauiSherpa.Workloads.NuGet;

/// <summary>
/// Client for interacting with NuGet feeds to download workload packages.
/// </summary>
public interface INuGetClient
{
    /// <summary>
    /// Gets all available versions of a NuGet package.
    /// </summary>
    /// <param name="packageId">The NuGet package ID.</param>
    /// <param name="includePrerelease">Whether to include prerelease versions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<NuGetVersion>> GetPackageVersionsAsync(
        string packageId,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads and extracts a NuGet package to a temporary directory.
    /// </summary>
    /// <param name="packageId">The NuGet package ID.</param>
    /// <param name="version">The package version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Path to the extracted package directory.</returns>
    Task<string> DownloadPackageAsync(
        string packageId,
        NuGetVersion version,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the contents of a specific file from a NuGet package without extracting the entire package.
    /// </summary>
    /// <param name="packageId">The NuGet package ID.</param>
    /// <param name="version">The package version.</param>
    /// <param name="filePath">The path of the file within the package.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The file contents as a string, or null if the file doesn't exist.</returns>
    Task<string?> GetPackageFileContentAsync(
        string packageId,
        NuGetVersion version,
        string filePath,
        CancellationToken cancellationToken = default);
}
