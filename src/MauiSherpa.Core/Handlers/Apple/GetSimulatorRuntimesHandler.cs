using Shiny.Mediator;
using Shiny.Mediator.Caching;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests.Apple;

namespace MauiSherpa.Core.Handlers.Apple;

/// <summary>
/// Handler for GetSimulatorRuntimesRequest with 10 minute caching (runtimes rarely change)
/// </summary>
public partial class GetSimulatorRuntimesHandler : IRequestHandler<GetSimulatorRuntimesRequest, IReadOnlyList<SimulatorRuntime>>
{
    private readonly ISimulatorService _simulatorService;

    public GetSimulatorRuntimesHandler(ISimulatorService simulatorService)
    {
        _simulatorService = simulatorService;
    }

    [Cache(AbsoluteExpirationSeconds = 600)]
    [OfflineAvailable]
    public async Task<IReadOnlyList<SimulatorRuntime>> Handle(
        GetSimulatorRuntimesRequest request,
        IMediatorContext context,
        CancellationToken ct)
    {
        return await _simulatorService.GetRuntimesAsync();
    }
}
