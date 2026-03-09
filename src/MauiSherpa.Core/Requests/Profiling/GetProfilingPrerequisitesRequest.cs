using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Profiling;
using Shiny.Mediator;

namespace MauiSherpa.Core.Requests.Profiling;

public record GetProfilingPrerequisitesRequest(
    ProfilingTargetPlatform Platform,
    IReadOnlyList<ProfilingCaptureKind>? CaptureKinds = null) : IRequest<ProfilingPrerequisiteReport>, IContractKey
{
    public string GetKey()
    {
        var normalizedCaptureKinds = CaptureKinds is { Count: > 0 }
            ? string.Join(",", CaptureKinds.Distinct().OrderBy(kind => kind))
            : "default";

        return $"profiling:prerequisites:{Platform}:{normalizedCaptureKinds}";
    }
}
