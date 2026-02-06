using System.Text.Json;
using Shiny.Mediator;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests.Apple;

namespace MauiSherpa.Core.Handlers.Apple;

/// <summary>
/// Handler to track bundle ID usage for sorting by recency, per Apple identity
/// </summary>
public class TrackBundleIdUsageHandler : ICommandHandler<TrackBundleIdUsageRequest>
{
    private const string StorageKeyPrefix = "recent_bundle_ids:";
    private const int MaxRecentItems = 10;
    
    private readonly ISecureStorageService _storage;
    private readonly ILoggingService _logger;
    private readonly IMediator _mediator;

    public TrackBundleIdUsageHandler(ISecureStorageService storage, ILoggingService logger, IMediator mediator)
    {
        _storage = storage;
        _logger = logger;
        _mediator = mediator;
    }

    public async Task Handle(TrackBundleIdUsageRequest request, IMediatorContext context, CancellationToken ct)
    {
        try
        {
            var storageKey = $"{StorageKeyPrefix}{request.IdentityId}";
            var recentIds = await GetRecentIdsAsync(storageKey);
            
            // Remove if already exists (we'll add to front)
            recentIds.Remove(request.BundleIdIdentifier);
            
            // Add to front
            recentIds.Insert(0, request.BundleIdIdentifier);
            
            // Trim to max size
            while (recentIds.Count > MaxRecentItems)
            {
                recentIds.RemoveAt(recentIds.Count - 1);
            }
            
            // Save
            var json = JsonSerializer.Serialize(recentIds);
            await _storage.SetAsync(storageKey, json);
            
            // Invalidate cache for this identity's recent bundle IDs
            await _mediator.FlushStores($"apple:recent-bundleids:{request.IdentityId}");
            
            _logger.LogDebug($"Tracked bundle ID usage: {request.BundleIdIdentifier} for identity {request.IdentityId}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to track bundle ID usage: {ex.Message}", ex);
        }
    }
    
    private async Task<List<string>> GetRecentIdsAsync(string storageKey)
    {
        var json = await _storage.GetAsync(storageKey);
        if (string.IsNullOrEmpty(json))
            return new List<string>();
            
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }
}
