using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Profiling;
using MauiSherpa.Core.Requests.Profiling;
using Shiny.Mediator;

namespace MauiSherpa.Core.Handlers.Profiling;

/// <summary>
/// Handler for building validation-friendly profiling capture command plans.
/// </summary>
public partial class PlanProfilingCaptureHandler : IRequestHandler<PlanProfilingCaptureRequest, ProfilingCapturePlan>
{
    private readonly IProfilingCaptureOrchestrationService _profilingCaptureOrchestrationService;

    public PlanProfilingCaptureHandler(IProfilingCaptureOrchestrationService profilingCaptureOrchestrationService)
    {
        _profilingCaptureOrchestrationService = profilingCaptureOrchestrationService;
    }

    public async Task<ProfilingCapturePlan> Handle(
        PlanProfilingCaptureRequest request,
        IMediatorContext context,
        CancellationToken ct)
    {
        return await _profilingCaptureOrchestrationService.PlanCaptureAsync(request.Definition, request.Options, ct);
    }
}
