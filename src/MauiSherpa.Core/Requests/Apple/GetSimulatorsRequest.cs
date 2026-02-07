using Shiny.Mediator;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Requests.Apple;

/// <summary>
/// Request to get all simulators from xcrun simctl
/// </summary>
public record GetSimulatorsRequest() : IRequest<IReadOnlyList<SimulatorDevice>>, IContractKey
{
    public string GetKey() => "apple:simulators";
}
