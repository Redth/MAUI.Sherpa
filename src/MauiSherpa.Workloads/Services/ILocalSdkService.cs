using System.Text.Json;
using MauiSherpa.Workloads.Models;

namespace MauiSherpa.Workloads.Services;

/// <summary>
/// Service for inspecting the local .NET SDK installation.
/// </summary>
public interface ILocalSdkService
{
    /// <summary>
    /// Gets the path to the local .NET SDK installation directory.
    /// </summary>
    string? GetDotNetSdkPath();

    /// <summary>
    /// Gets all installed SDK versions on the local machine.
    /// </summary>
    IReadOnlyList<SdkVersion> GetInstalledSdkVersions();

    /// <summary>
    /// Gets all installed workload manifests for a specific SDK band.
    /// </summary>
    /// <param name="featureBand">The SDK feature band (e.g., "9.0.100").</param>
    IReadOnlyList<string> GetInstalledWorkloadManifests(string featureBand);

    /// <summary>
    /// Reads an installed workload manifest from the local SDK.
    /// </summary>
    /// <param name="featureBand">The SDK feature band (e.g., "9.0.100").</param>
    /// <param name="manifestId">The manifest ID (e.g., "microsoft.net.sdk.maui").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<WorkloadManifest?> GetInstalledManifestAsync(string featureBand, string manifestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the installed workload set for a specific SDK band, if using workload-set mode.
    /// </summary>
    /// <param name="featureBand">The SDK feature band (e.g., "9.0.100").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<WorkloadSet?> GetInstalledWorkloadSetAsync(string featureBand, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a comprehensive JSON document containing all installed SDK information.
    /// Useful for AI tool calling to get complete state in one request.
    /// </summary>
    /// <param name="includeManifestDetails">Whether to include full workload/pack details from each manifest.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A JsonDocument containing all installed SDK information.</returns>
    Task<JsonDocument> GetInstalledSdkInfoAsJsonAsync(bool includeManifestDetails = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a comprehensive JSON string containing all installed SDK information.
    /// Useful for AI tool calling to get complete state in one request.
    /// </summary>
    /// <param name="includeManifestDetails">Whether to include full workload/pack details from each manifest.</param>
    /// <param name="indented">Whether to format the JSON with indentation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A JSON string containing all installed SDK information.</returns>
    Task<string> GetInstalledSdkInfoAsJsonStringAsync(bool includeManifestDetails = true, bool indented = false, CancellationToken cancellationToken = default);
}
