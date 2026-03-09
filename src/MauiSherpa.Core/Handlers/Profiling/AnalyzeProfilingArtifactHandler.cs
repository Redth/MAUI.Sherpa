using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Profiling;
using MauiSherpa.Core.Requests.Profiling;
using Shiny.Mediator;

namespace MauiSherpa.Core.Handlers.Profiling;

public partial class AnalyzeProfilingArtifactHandler : IRequestHandler<AnalyzeProfilingArtifactRequest, ProfilingArtifactAnalysisResult>
{
    private readonly IProfilingArtifactAnalysisService _profilingArtifactAnalysisService;

    public AnalyzeProfilingArtifactHandler(IProfilingArtifactAnalysisService profilingArtifactAnalysisService)
    {
        _profilingArtifactAnalysisService = profilingArtifactAnalysisService;
    }

    public async Task<ProfilingArtifactAnalysisResult> Handle(
        AnalyzeProfilingArtifactRequest request,
        IMediatorContext context,
        CancellationToken ct)
    {
        return await _profilingArtifactAnalysisService.AnalyzeArtifactAsync(request.ArtifactId, ct);
    }
}
