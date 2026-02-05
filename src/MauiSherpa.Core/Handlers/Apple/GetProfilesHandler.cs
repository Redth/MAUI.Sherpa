using Shiny.Mediator;
using Shiny.Mediator.Caching;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests.Apple;

namespace MauiSherpa.Core.Handlers.Apple;

/// <summary>
/// Handler for GetProfilesRequest with 5 minute caching
/// </summary>
public partial class GetProfilesHandler : IRequestHandler<GetProfilesRequest, IReadOnlyList<AppleProfile>>
{
    private readonly IAppleConnectService _appleService;

    public GetProfilesHandler(IAppleConnectService appleService)
    {
        _appleService = appleService;
    }

    [Cache(AbsoluteExpirationSeconds = 300)]
    [OfflineAvailable] // 5 min cache
    public async Task<IReadOnlyList<AppleProfile>> Handle(
        GetProfilesRequest request,
        IMediatorContext context,
        CancellationToken ct)
    {
        return await _appleService.GetProfilesAsync();
    }
}
