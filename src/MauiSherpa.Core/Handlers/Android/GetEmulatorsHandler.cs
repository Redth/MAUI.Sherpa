using Shiny.Mediator;
using Shiny.Mediator.Caching;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests.Android;

namespace MauiSherpa.Core.Handlers.Android;

/// <summary>
/// Handler for GetEmulatorsRequest with 2 minute caching (emulator state can change)
/// </summary>
public partial class GetEmulatorsHandler : IRequestHandler<GetEmulatorsRequest, IReadOnlyList<AvdInfo>>
{
    private readonly IAndroidSdkService _sdkService;

    public GetEmulatorsHandler(IAndroidSdkService sdkService)
    {
        _sdkService = sdkService;
    }

    [Cache(AbsoluteExpirationSeconds = 120)] // 2 min cache - emulator state changes
    public async Task<IReadOnlyList<AvdInfo>> Handle(
        GetEmulatorsRequest request,
        IMediatorContext context,
        CancellationToken ct)
    {
        return await _sdkService.GetAvdsAsync();
    }
}
