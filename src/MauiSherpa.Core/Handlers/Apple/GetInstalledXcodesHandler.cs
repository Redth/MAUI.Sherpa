using Shiny.Mediator;
using Shiny.Mediator.Caching;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests.Apple;

namespace MauiSherpa.Core.Handlers.Apple;

/// <summary>
/// Handler for GetInstalledXcodesRequest with short caching (installations can change)
/// </summary>
public partial class GetInstalledXcodesHandler : IRequestHandler<GetInstalledXcodesRequest, IReadOnlyList<XcodeInstallation>>
{
    private readonly IXcodeService _xcodeService;

    public GetInstalledXcodesHandler(IXcodeService xcodeService)
    {
        _xcodeService = xcodeService;
    }

    [Cache(AbsoluteExpirationSeconds = 30)]
    public async Task<IReadOnlyList<XcodeInstallation>> Handle(
        GetInstalledXcodesRequest request,
        IMediatorContext context,
        CancellationToken ct)
    {
        return await _xcodeService.GetInstalledXcodesAsync();
    }
}
