using MauiSherpa.Workloads.Models;

namespace MauiSherpa.Workloads.Services;

/// <summary>
/// Resolves dotnetup's tracked channels against the official .NET release metadata to preview
/// which tracked channels have a newer version available — without running any install/update.
/// </summary>
public interface IDotnetUpdateResolver
{
    /// <summary>
    /// For every tracked install spec in <paramref name="list"/>, resolves the channel to its newest
    /// available version and compares it against the newest installed version that satisfies it.
    /// </summary>
    Task<IReadOnlyList<DotnetUpdatePreview>> GetUpdatePreviewAsync(
        DotnetUpListResult list,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a single channel for a component to its newest available version (from the release
    /// feed) and the newest installed version that satisfies it. Used by both the update preview and
    /// the global.json project inspector.
    /// </summary>
    Task<(string? Available, string? Installed)> ResolveChannelAsync(
        DotnetUpComponent component,
        string channel,
        DotnetUpListResult list,
        CancellationToken cancellationToken = default);
}
