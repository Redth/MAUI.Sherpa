using MauiSherpa.Workloads.Models;
using NuGet.Versioning;

namespace MauiSherpa.Workloads.Services;

/// <summary>
/// Service for querying workload sets from NuGet.
/// </summary>
public interface IWorkloadSetService
{
    /// <summary>
    /// Gets all available workload set versions for a given SDK feature band.
    /// </summary>
    /// <param name="featureBand">The SDK feature band (e.g., "9.0.100").</param>
    /// <param name="includePrerelease">Whether to include prerelease versions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<NuGetVersion>> GetAvailableWorkloadSetVersionsAsync(
        string featureBand,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a workload set for a specific feature band and version.
    /// </summary>
    /// <param name="featureBand">The SDK feature band (e.g., "9.0.100").</param>
    /// <param name="version">The workload set version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<WorkloadSet?> GetWorkloadSetAsync(
        string featureBand,
        NuGetVersion version,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the latest workload set for a specific feature band.
    /// </summary>
    /// <param name="featureBand">The SDK feature band (e.g., "9.0.100").</param>
    /// <param name="includePrerelease">Whether to include prerelease versions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<WorkloadSet?> GetLatestWorkloadSetAsync(
        string featureBand,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default);
}
