using Shiny.Mediator;
using Shiny.Mediator.Caching;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests.Apple;

namespace MauiSherpa.Core.Handlers.Apple;

/// <summary>
/// Handler for GetRuntimeStorageRequest with short caching (storage can change)
/// </summary>
public partial class GetRuntimeStorageHandler : IRequestHandler<GetRuntimeStorageRequest, IReadOnlyList<SimulatorRuntimeStorage>>
{
    private readonly ISimulatorService _simulatorService;

    public GetRuntimeStorageHandler(ISimulatorService simulatorService)
    {
        _simulatorService = simulatorService;
    }

    [Cache(AbsoluteExpirationSeconds = 60)]
    public async Task<IReadOnlyList<SimulatorRuntimeStorage>> Handle(
        GetRuntimeStorageRequest request,
        IMediatorContext context,
        CancellationToken ct)
    {
        return await _simulatorService.GetRuntimeStorageAsync();
    }
}
