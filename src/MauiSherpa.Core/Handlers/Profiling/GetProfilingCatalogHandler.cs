using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Profiling;
using MauiSherpa.Core.Requests.Profiling;
using Shiny.Mediator;
using Shiny.Mediator.Caching;

namespace MauiSherpa.Core.Handlers.Profiling;

/// <summary>
/// Handler for retrieving the built-in profiling catalog with medium-lived caching.
/// </summary>
public partial class GetProfilingCatalogHandler : IRequestHandler<GetProfilingCatalogRequest, ProfilingCatalog>
{
    private readonly IProfilingCatalogService _profilingCatalogService;

    public GetProfilingCatalogHandler(IProfilingCatalogService profilingCatalogService)
    {
        _profilingCatalogService = profilingCatalogService;
    }

    [Cache(AbsoluteExpirationSeconds = 300)]
    [OfflineAvailable]
    public async Task<ProfilingCatalog> Handle(
        GetProfilingCatalogRequest request,
        IMediatorContext context,
        CancellationToken ct)
    {
        return await _profilingCatalogService.GetCatalogAsync(ct);
    }
}
