using Shiny.Mediator;
using Shiny.Mediator.Caching;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests.Apple;

namespace MauiSherpa.Core.Handlers.Apple;

/// <summary>
/// Handler for GetSimulatorAppsRequest with 60 second caching
/// </summary>
public partial class GetSimulatorAppsHandler : IRequestHandler<GetSimulatorAppsRequest, IReadOnlyList<SimulatorApp>>
{
    private readonly ISimulatorService _simulatorService;

    public GetSimulatorAppsHandler(ISimulatorService simulatorService)
    {
        _simulatorService = simulatorService;
    }

    [Cache(AbsoluteExpirationSeconds = 60)]
    public async Task<IReadOnlyList<SimulatorApp>> Handle(
        GetSimulatorAppsRequest request,
        IMediatorContext context,
        CancellationToken ct)
    {
        return await _simulatorService.GetInstalledAppsAsync(request.Udid);
    }
}
