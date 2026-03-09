using MauiSherpa.Core.Models.Profiling;
using Shiny.Mediator;

namespace MauiSherpa.Core.Requests.Profiling;

public record PlanProfilingCaptureRequest(
    ProfilingSessionDefinition Definition,
    ProfilingCapturePlanOptions? Options = null) : IRequest<ProfilingCapturePlan>;
