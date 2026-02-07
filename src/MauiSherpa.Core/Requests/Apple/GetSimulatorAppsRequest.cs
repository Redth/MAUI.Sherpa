using Shiny.Mediator;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Requests.Apple;

/// <summary>
/// Request to get installed apps on a booted simulator
/// </summary>
public record GetSimulatorAppsRequest(string Udid) : IRequest<IReadOnlyList<SimulatorApp>>, IContractKey
{
    public string GetKey() => $"apple:simulator:apps:{Udid}";
}
