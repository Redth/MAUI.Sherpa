using System.Text.Json;
using MauiSherpa.Workloads.Models;
using NuGet.Versioning;

namespace MauiSherpa.Workloads.Services;

/// <summary>
/// Service for querying workload manifests from NuGet.
/// </summary>
public interface IWorkloadManifestService
{
    /// <summary>
    /// Gets all available versions of a workload manifest.
    /// </summary>
    /// <param name="manifestId">The manifest ID (e.g., "microsoft.net.sdk.maui").</param>
    /// <param name="sdkBand">The SDK band for the manifest package (e.g., "9.0.100").</param>
    /// <param name="includePrerelease">Whether to include prerelease versions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<NuGetVersion>> GetAvailableManifestVersionsAsync(
        string manifestId,
        string sdkBand,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a workload manifest for a specific version.
    /// </summary>
    /// <param name="manifestId">The manifest ID (e.g., "microsoft.net.sdk.maui").</param>
    /// <param name="sdkBand">The SDK band for the manifest package (e.g., "9.0.100").</param>
    /// <param name="version">The manifest version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<WorkloadManifest?> GetManifestAsync(
        string manifestId,
        string sdkBand,
        NuGetVersion version,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the raw WorkloadManifest.json content as a JsonDocument for custom parsing.
    /// </summary>
    /// <param name="manifestId">The manifest ID.</param>
    /// <param name="sdkBand">The SDK band for the manifest package.</param>
    /// <param name="version">The manifest version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<JsonDocument?> GetRawManifestAsync(
        string manifestId,
        string sdkBand,
        NuGetVersion version,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the latest workload manifest for a specific SDK band.
    /// </summary>
    /// <param name="manifestId">The manifest ID.</param>
    /// <param name="sdkBand">The SDK band for the manifest package.</param>
    /// <param name="includePrerelease">Whether to include prerelease versions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<WorkloadManifest?> GetLatestManifestAsync(
        string manifestId,
        string sdkBand,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the WorkloadDependencies.json content as a strongly-typed model.
    /// </summary>
    /// <param name="manifestId">The manifest ID.</param>
    /// <param name="sdkBand">The SDK band for the manifest package.</param>
    /// <param name="version">The manifest version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<WorkloadDependencies?> GetDependenciesAsync(
        string manifestId,
        string sdkBand,
        NuGetVersion version,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the raw WorkloadDependencies.json content as a JsonDocument for custom parsing.
    /// </summary>
    /// <param name="manifestId">The manifest ID.</param>
    /// <param name="sdkBand">The SDK band for the manifest package.</param>
    /// <param name="version">The manifest version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<JsonDocument?> GetRawDependenciesAsync(
        string manifestId,
        string sdkBand,
        NuGetVersion version,
        CancellationToken cancellationToken = default);
}
