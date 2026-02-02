using MauiSherpa.Workloads.Models;

namespace MauiSherpa.Workloads.Services;

/// <summary>
/// Service for querying available .NET SDK versions.
/// </summary>
public interface ISdkVersionService
{
    /// <summary>
    /// Gets all available SDK versions from the .NET releases feed.
    /// </summary>
    /// <param name="includePreview">Whether to include preview/RC releases.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<SdkVersion>> GetAvailableSdkVersionsAsync(
        bool includePreview = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all available SDK versions for a specific runtime version (e.g., "9.0").
    /// </summary>
    /// <param name="runtimeVersion">The runtime version (e.g., "9.0").</param>
    /// <param name="includePreview">Whether to include preview/RC releases.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<SdkVersion>> GetSdkVersionsForRuntimeAsync(
        string runtimeVersion,
        bool includePreview = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the feature band for an SDK version string.
    /// </summary>
    /// <param name="sdkVersion">The full SDK version (e.g., "9.0.105").</param>
    /// <returns>The feature band (e.g., "9.0.100").</returns>
    string GetFeatureBand(string sdkVersion);
}
