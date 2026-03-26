using Shiny.Mediator;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Requests.Apple;

/// <summary>
/// Request to get downloadable simulator runtimes from Apple's runtime catalog.
/// </summary>
public record GetDownloadableSimulatorRuntimesRequest() : IRequest<IReadOnlyList<DownloadableSimulatorRuntime>>, IContractKey
{
    public string GetKey() => "apple:simulator:downloadable-runtimes";
}
