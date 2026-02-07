using Shiny.Mediator;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Requests.Apple;

/// <summary>
/// Request to get available simulator runtimes
/// </summary>
public record GetSimulatorRuntimesRequest() : IRequest<IReadOnlyList<SimulatorRuntime>>, IContractKey
{
    public string GetKey() => "apple:simulator:runtimes";
}
