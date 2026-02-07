using Shiny.Mediator;
using Shiny.Mediator.Caching;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests.Apple;

namespace MauiSherpa.Core.Handlers.Apple;

/// <summary>
/// Handler for GetSimulatorDeviceTypesRequest with 10 minute caching (device types rarely change)
/// </summary>
public partial class GetSimulatorDeviceTypesHandler : IRequestHandler<GetSimulatorDeviceTypesRequest, IReadOnlyList<SimulatorDeviceType>>
{
    private readonly ISimulatorService _simulatorService;

    public GetSimulatorDeviceTypesHandler(ISimulatorService simulatorService)
    {
        _simulatorService = simulatorService;
    }

    [Cache(AbsoluteExpirationSeconds = 600)]
    [OfflineAvailable]
    public async Task<IReadOnlyList<SimulatorDeviceType>> Handle(
        GetSimulatorDeviceTypesRequest request,
        IMediatorContext context,
        CancellationToken ct)
    {
        return await _simulatorService.GetDeviceTypesAsync();
    }
}
