using Shiny.Mediator;
using Shiny.Mediator.Caching;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests.Android;

namespace MauiSherpa.Core.Handlers.Android;

/// <summary>
/// Handler for GetDeviceDefinitionsRequest with 60 minute caching (static data)
/// </summary>
public partial class GetDeviceDefinitionsHandler : IRequestHandler<GetDeviceDefinitionsRequest, IReadOnlyList<AvdDeviceDefinition>>
{
    private readonly IAndroidSdkService _sdkService;

    public GetDeviceDefinitionsHandler(IAndroidSdkService sdkService)
    {
        _sdkService = sdkService;
    }

    [Cache(AbsoluteExpirationSeconds = 3600)] // 60 min cache - rarely changes
    public async Task<IReadOnlyList<AvdDeviceDefinition>> Handle(
        GetDeviceDefinitionsRequest request,
        IMediatorContext context,
        CancellationToken ct)
    {
        return await _sdkService.GetAvdDeviceDefinitionsAsync();
    }
}

/// <summary>
/// Handler for GetAvdSkinsRequest with 60 minute caching (static data)
/// </summary>
public partial class GetAvdSkinsHandler : IRequestHandler<GetAvdSkinsRequest, IReadOnlyList<string>>
{
    private readonly IAndroidSdkService _sdkService;

    public GetAvdSkinsHandler(IAndroidSdkService sdkService)
    {
        _sdkService = sdkService;
    }

    [Cache(AbsoluteExpirationSeconds = 3600)] // 60 min cache - rarely changes
    public async Task<IReadOnlyList<string>> Handle(
        GetAvdSkinsRequest request,
        IMediatorContext context,
        CancellationToken ct)
    {
        return await _sdkService.GetAvdSkinsAsync();
    }
}
