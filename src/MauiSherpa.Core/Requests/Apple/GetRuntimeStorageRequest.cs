using Shiny.Mediator;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Requests.Apple;

/// <summary>
/// Request to get detailed simulator runtime storage info
/// </summary>
public record GetRuntimeStorageRequest() : IRequest<IReadOnlyList<SimulatorRuntimeStorage>>, IContractKey
{
    public string GetKey() => "apple:simulator:runtime-storage";
}
