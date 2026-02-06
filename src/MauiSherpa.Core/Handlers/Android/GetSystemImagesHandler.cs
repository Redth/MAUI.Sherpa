using Shiny.Mediator;
using Shiny.Mediator.Caching;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests.Android;

namespace MauiSherpa.Core.Handlers.Android;

/// <summary>
/// Handler for GetSystemImagesRequest with 60 minute caching (system images rarely change)
/// </summary>
public partial class GetSystemImagesHandler : IRequestHandler<GetSystemImagesRequest, IReadOnlyList<string>>
{
    private readonly IAndroidSdkService _sdkService;

    public GetSystemImagesHandler(IAndroidSdkService sdkService)
    {
        _sdkService = sdkService;
    }

    [Cache(AbsoluteExpirationSeconds = 3600)]
    [OfflineAvailable] // 60 min cache - system images rarely change
    public async Task<IReadOnlyList<string>> Handle(
        GetSystemImagesRequest request,
        IMediatorContext context,
        CancellationToken ct)
    {
        return await _sdkService.GetSystemImagesAsync();
    }
}
