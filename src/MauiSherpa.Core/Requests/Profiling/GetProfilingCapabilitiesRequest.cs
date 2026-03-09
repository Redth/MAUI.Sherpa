using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Profiling;
using Shiny.Mediator;

namespace MauiSherpa.Core.Requests.Profiling;

public record GetProfilingCapabilitiesRequest(ProfilingTargetPlatform Platform) : IRequest<ProfilingPlatformCapabilities>, IContractKey
{
    public string GetKey() => $"profiling:capabilities:{Platform}";
}
