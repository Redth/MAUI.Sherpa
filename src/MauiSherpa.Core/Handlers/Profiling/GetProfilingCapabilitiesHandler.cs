using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Profiling;
using MauiSherpa.Core.Requests.Profiling;
using Shiny.Mediator;
using Shiny.Mediator.Caching;

namespace MauiSherpa.Core.Handlers.Profiling;

/// <summary>
/// Handler for retrieving platform-specific profiling capabilities with medium-lived caching.
/// </summary>
public partial class GetProfilingCapabilitiesHandler : IRequestHandler<GetProfilingCapabilitiesRequest, ProfilingPlatformCapabilities>
{
    private readonly IProfilingCatalogService _profilingCatalogService;

    public GetProfilingCapabilitiesHandler(IProfilingCatalogService profilingCatalogService)
    {
        _profilingCatalogService = profilingCatalogService;
    }

    [Cache(AbsoluteExpirationSeconds = 300)]
    [OfflineAvailable]
    public async Task<ProfilingPlatformCapabilities> Handle(
        GetProfilingCapabilitiesRequest request,
        IMediatorContext context,
        CancellationToken ct)
    {
        return await _profilingCatalogService.GetCapabilitiesAsync(request.Platform, ct);
    }
}
