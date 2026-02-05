using Shiny.Mediator;
using Shiny.Mediator.Caching;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests.Apple;

namespace MauiSherpa.Core.Handlers.Apple;

/// <summary>
/// Handler for GetBundleIdsRequest with 10 minute caching (bundle IDs are stable)
/// </summary>
public partial class GetBundleIdsHandler : IRequestHandler<GetBundleIdsRequest, IReadOnlyList<AppleBundleId>>
{
    private readonly IAppleConnectService _appleService;

    public GetBundleIdsHandler(IAppleConnectService appleService)
    {
        _appleService = appleService;
    }

    [Cache(AbsoluteExpirationSeconds = 600)]
    [OfflineAvailable] // 10 min cache - bundle IDs rarely change
    public async Task<IReadOnlyList<AppleBundleId>> Handle(
        GetBundleIdsRequest request,
        IMediatorContext context,
        CancellationToken ct)
    {
        return await _appleService.GetBundleIdsAsync();
    }
}
