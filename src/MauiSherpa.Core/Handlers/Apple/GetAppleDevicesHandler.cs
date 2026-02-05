using Shiny.Mediator;
using Shiny.Mediator.Caching;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests.Apple;

namespace MauiSherpa.Core.Handlers.Apple;

/// <summary>
/// Handler for GetAppleDevicesRequest with 10 minute caching (devices rarely change)
/// </summary>
public partial class GetAppleDevicesHandler : IRequestHandler<GetAppleDevicesRequest, IReadOnlyList<AppleDevice>>
{
    private readonly IAppleConnectService _appleService;

    public GetAppleDevicesHandler(IAppleConnectService appleService)
    {
        _appleService = appleService;
    }

    [Cache(AbsoluteExpirationSeconds = 600)]
    [OfflineAvailable] // 10 min cache - devices rarely change
    public async Task<IReadOnlyList<AppleDevice>> Handle(
        GetAppleDevicesRequest request,
        IMediatorContext context,
        CancellationToken ct)
    {
        return await _appleService.GetDevicesAsync();
    }
}
