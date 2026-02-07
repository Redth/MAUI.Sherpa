using Shiny.Mediator;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Requests.Apple;

/// <summary>
/// Request to get available simulator device types
/// </summary>
public record GetSimulatorDeviceTypesRequest() : IRequest<IReadOnlyList<SimulatorDeviceType>>, IContractKey
{
    public string GetKey() => "apple:simulator:devicetypes";
}
