using Shiny.Mediator;
using Shiny.Mediator.Caching;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests.Android;

namespace MauiSherpa.Core.Handlers.Android;

/// <summary>
/// Handler for GetJdkPathRequest with long-lived caching.
/// JDK path detection is expensive and rarely changes during app lifetime.
/// Cache for 24 hours - will be invalidated on app start or when path is changed.
/// </summary>
public partial class GetJdkPathHandler : IRequestHandler<GetJdkPathRequest, string?>
{
    private readonly IOpenJdkSettingsService _settingsService;

    public GetJdkPathHandler(IOpenJdkSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    [Cache(AbsoluteExpirationSeconds = 86400)]
    [OfflineAvailable]
    public async Task<string?> Handle(
        GetJdkPathRequest request,
        IMediatorContext context,
        CancellationToken ct)
    {
        await _settingsService.InitializeAsync();
        return await _settingsService.GetEffectiveJdkPathAsync();
    }
}
