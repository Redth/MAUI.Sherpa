using Shiny.Mediator;
using Shiny.Mediator.Caching;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests.Android;

namespace MauiSherpa.Core.Handlers.Android;

/// <summary>
/// Handler for GetAndroidDevicesRequest with 1 minute caching (devices connect/disconnect)
/// </summary>
public partial class GetAndroidDevicesHandler : IRequestHandler<GetAndroidDevicesRequest, IReadOnlyList<DeviceInfo>>
{
    private readonly IAndroidSdkService _sdkService;

    public GetAndroidDevicesHandler(IAndroidSdkService sdkService)
    {
        _sdkService = sdkService;
    }

    [Cache(AbsoluteExpirationSeconds = 60)] // 1 min cache - devices can connect/disconnect
    public async Task<IReadOnlyList<DeviceInfo>> Handle(
        GetAndroidDevicesRequest request,
        IMediatorContext context,
        CancellationToken ct)
    {
        return await _sdkService.GetDevicesAsync();
    }
}
