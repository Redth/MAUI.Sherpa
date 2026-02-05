using Shiny.Mediator;
using Shiny.Mediator.Caching;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests.Apple;

namespace MauiSherpa.Core.Handlers.Apple;

/// <summary>
/// Handler for GetCertificatesRequest with 5 minute caching and offline support
/// </summary>
public partial class GetCertificatesHandler : IRequestHandler<GetCertificatesRequest, IReadOnlyList<AppleCertificate>>
{
    private readonly IAppleConnectService _appleService;

    public GetCertificatesHandler(IAppleConnectService appleService)
    {
        _appleService = appleService;
    }

    [Cache(AbsoluteExpirationSeconds = 300)] // 5 min cache
    [OfflineAvailable]
    public async Task<IReadOnlyList<AppleCertificate>> Handle(
        GetCertificatesRequest request,
        IMediatorContext context,
        CancellationToken ct)
    {
        return await _appleService.GetCertificatesAsync();
    }
}
