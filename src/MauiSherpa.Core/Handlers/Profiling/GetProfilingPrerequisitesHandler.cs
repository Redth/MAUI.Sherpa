using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Profiling;
using MauiSherpa.Core.Requests.Profiling;
using Shiny.Mediator;
using Shiny.Mediator.Caching;

namespace MauiSherpa.Core.Handlers.Profiling;

public partial class GetProfilingPrerequisitesHandler : IRequestHandler<GetProfilingPrerequisitesRequest, ProfilingPrerequisiteReport>
{
    private readonly IProfilingPrerequisitesService _profilingPrerequisitesService;

    public GetProfilingPrerequisitesHandler(IProfilingPrerequisitesService profilingPrerequisitesService)
    {
        _profilingPrerequisitesService = profilingPrerequisitesService;
    }

    [Cache(AbsoluteExpirationSeconds = 120)]
    [OfflineAvailable]
    public async Task<ProfilingPrerequisiteReport> Handle(
        GetProfilingPrerequisitesRequest request,
        IMediatorContext context,
        CancellationToken ct)
    {
        return await _profilingPrerequisitesService.GetPrerequisitesAsync(
            request.Platform,
            request.CaptureKinds,
            ct: ct);
    }
}
