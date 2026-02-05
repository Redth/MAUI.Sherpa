using Shiny.Mediator;
using Shiny.Mediator.Caching;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests.Android;

namespace MauiSherpa.Core.Handlers.Android;

/// <summary>
/// Handler for GetSdkPathRequest with long-lived caching.
/// SDK path detection is expensive and rarely changes during app lifetime.
/// Cache for 24 hours - will be invalidated on app start or when path is changed.
/// </summary>
public partial class GetSdkPathHandler : IRequestHandler<GetSdkPathRequest, string?>
{
    private readonly IAndroidSdkSettingsService _settingsService;

    public GetSdkPathHandler(IAndroidSdkSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    [Cache(AbsoluteExpirationSeconds = 86400)]
    [OfflineAvailable] // 24 hour cache
    public async Task<string?> Handle(
        GetSdkPathRequest request,
        IMediatorContext context,
        CancellationToken ct)
    {
        await _settingsService.InitializeAsync();
        return await _settingsService.GetEffectiveSdkPathAsync();
    }
}
