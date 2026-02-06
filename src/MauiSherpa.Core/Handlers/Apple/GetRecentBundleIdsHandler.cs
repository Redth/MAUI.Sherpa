using System.Text.Json;
using Shiny.Mediator;
using Shiny.Mediator.Caching;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests.Apple;

namespace MauiSherpa.Core.Handlers.Apple;

/// <summary>
/// Handler to get recently used bundle IDs for a specific identity with caching
/// </summary>
public partial class GetRecentBundleIdsHandler : IRequestHandler<GetRecentBundleIdsRequest, IReadOnlyList<string>>
{
    private const string StorageKeyPrefix = "recent_bundle_ids:";
    
    private readonly ISecureStorageService _storage;
    private readonly ILoggingService _logger;

    public GetRecentBundleIdsHandler(ISecureStorageService storage, ILoggingService logger)
    {
        _storage = storage;
        _logger = logger;
    }

    [Cache(AbsoluteExpirationSeconds = 60)] // Short cache, will be invalidated on track
    [OfflineAvailable]
    public async Task<IReadOnlyList<string>> Handle(
        GetRecentBundleIdsRequest request,
        IMediatorContext context,
        CancellationToken ct)
    {
        try
        {
            var storageKey = $"{StorageKeyPrefix}{request.IdentityId}";
            var json = await _storage.GetAsync(storageKey);
            if (string.IsNullOrEmpty(json))
                return Array.Empty<string>();
                
            var recentIds = JsonSerializer.Deserialize<List<string>>(json);
            return recentIds?.AsReadOnly() ?? (IReadOnlyList<string>)Array.Empty<string>();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to get recent bundle IDs: {ex.Message}", ex);
            return Array.Empty<string>();
        }
    }
}
