using Shiny.Mediator;
using Shiny.Mediator.Caching;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests.Apple;

namespace MauiSherpa.Core.Handlers.Apple;

/// <summary>
/// Handler for GetInstalledCertsRequest with 10 minute caching (keychain queries are slow)
/// </summary>
public partial class GetInstalledCertsHandler : IRequestHandler<GetInstalledCertsRequest, IReadOnlyList<InstalledCertInfo>>
{
    private readonly IAppleRootCertService _certService;

    public GetInstalledCertsHandler(IAppleRootCertService certService)
    {
        _certService = certService;
    }

    [Cache(AbsoluteExpirationSeconds = 600)]
    [OfflineAvailable] // 10 min cache
    public async Task<IReadOnlyList<InstalledCertInfo>> Handle(
        GetInstalledCertsRequest request,
        IMediatorContext context,
        CancellationToken ct)
    {
        return await _certService.GetInstalledAppleCertsAsync();
    }
}
