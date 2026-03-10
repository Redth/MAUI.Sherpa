using Shiny.Mediator;
using Shiny.Mediator.Caching;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests.Apple;

namespace MauiSherpa.Core.Handlers.Apple;

/// <summary>
/// Handler for GetAvailableXcodesRequest with longer caching (remote API data)
/// </summary>
public partial class GetAvailableXcodesHandler : IRequestHandler<GetAvailableXcodesRequest, IReadOnlyList<XcodeRelease>>
{
    private readonly IXcodeService _xcodeService;

    public GetAvailableXcodesHandler(IXcodeService xcodeService)
    {
        _xcodeService = xcodeService;
    }

    [Cache(AbsoluteExpirationSeconds = 600)]
    [OfflineAvailable]
    public async Task<IReadOnlyList<XcodeRelease>> Handle(
        GetAvailableXcodesRequest request,
        IMediatorContext context,
        CancellationToken ct)
    {
        return await _xcodeService.GetAvailableReleasesAsync();
    }
}
