using MauiSherpa.Bundles;

namespace MauiSherpa.Core.Interfaces;

public sealed record SherpaBundleDefinition
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string PublishProfileId { get; init; }
    public required SherpaBundle Template { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
}

public interface ISherpaBundleService
{
    event Action? OnBundlesChanged;

    Task<IReadOnlyList<SherpaBundleDefinition>> GetBundlesAsync(CancellationToken cancellationToken = default);

    Task<SherpaBundleDefinition?> GetBundleAsync(
        string id,
        CancellationToken cancellationToken = default);

    Task SaveBundleAsync(
        SherpaBundleDefinition definition,
        CancellationToken cancellationToken = default);

    Task DeleteBundleAsync(
        string id,
        CancellationToken cancellationToken = default);
}

public interface ISherpaBundleExportService
{
    Task<byte[]> ExportAsync(
        SherpaBundleDefinition definition,
        string password,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
