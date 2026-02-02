using Shiny.Mediator;
using Shiny.Mediator.Caching;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests.Android;

namespace MauiSherpa.Core.Handlers.Android;

/// <summary>
/// Handler for GetInstalledPackagesRequest with 5 minute caching
/// </summary>
public partial class GetInstalledPackagesHandler : IRequestHandler<GetInstalledPackagesRequest, IReadOnlyList<SdkPackageInfo>>
{
    private readonly IAndroidSdkService _sdkService;

    public GetInstalledPackagesHandler(IAndroidSdkService sdkService)
    {
        _sdkService = sdkService;
    }

    [Cache(AbsoluteExpirationSeconds = 300)] // 5 min cache
    public async Task<IReadOnlyList<SdkPackageInfo>> Handle(
        GetInstalledPackagesRequest request,
        IMediatorContext context,
        CancellationToken ct)
    {
        return await _sdkService.GetInstalledPackagesAsync();
    }
}

/// <summary>
/// Handler for GetAvailablePackagesRequest with 30 minute caching (network call)
/// </summary>
public partial class GetAvailablePackagesHandler : IRequestHandler<GetAvailablePackagesRequest, IReadOnlyList<SdkPackageInfo>>
{
    private readonly IAndroidSdkService _sdkService;

    public GetAvailablePackagesHandler(IAndroidSdkService sdkService)
    {
        _sdkService = sdkService;
    }

    [Cache(AbsoluteExpirationSeconds = 1800)] // 30 min cache - this is a slow network call
    public async Task<IReadOnlyList<SdkPackageInfo>> Handle(
        GetAvailablePackagesRequest request,
        IMediatorContext context,
        CancellationToken ct)
    {
        return await _sdkService.GetAvailablePackagesAsync();
    }
}
