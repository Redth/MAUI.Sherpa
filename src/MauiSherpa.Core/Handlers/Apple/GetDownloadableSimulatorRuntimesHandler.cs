using Shiny.Mediator;
using Shiny.Mediator.Caching;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests.Apple;

namespace MauiSherpa.Core.Handlers.Apple;

/// <summary>
/// Handler for GetDownloadableSimulatorRuntimesRequest with short caching.
/// </summary>
public partial class GetDownloadableSimulatorRuntimesHandler : IRequestHandler<GetDownloadableSimulatorRuntimesRequest, IReadOnlyList<DownloadableSimulatorRuntime>>
{
    private readonly ISimulatorService _simulatorService;

    public GetDownloadableSimulatorRuntimesHandler(ISimulatorService simulatorService)
    {
        _simulatorService = simulatorService;
    }

    [Cache(AbsoluteExpirationSeconds = 900)]
    public async Task<IReadOnlyList<DownloadableSimulatorRuntime>> Handle(
        GetDownloadableSimulatorRuntimesRequest request,
        IMediatorContext context,
        CancellationToken ct)
    {
        return await _simulatorService.GetDownloadableRuntimesAsync();
    }
}
