using Shiny.Mediator;
using Shiny.Mediator.Caching;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests.Apple;

namespace MauiSherpa.Core.Handlers.Apple;

/// <summary>
/// Handler for GetSimulatorsRequest with 2 minute caching
/// </summary>
public partial class GetSimulatorsHandler : IRequestHandler<GetSimulatorsRequest, IReadOnlyList<SimulatorDevice>>
{
    private readonly ISimulatorService _simulatorService;

    public GetSimulatorsHandler(ISimulatorService simulatorService)
    {
        _simulatorService = simulatorService;
    }

    [Cache(AbsoluteExpirationSeconds = 120)]
    [OfflineAvailable]
    public async Task<IReadOnlyList<SimulatorDevice>> Handle(
        GetSimulatorsRequest request,
        IMediatorContext context,
        CancellationToken ct)
    {
        return await _simulatorService.GetSimulatorsAsync();
    }
}
