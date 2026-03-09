using MauiSherpa.Core.Models.Profiling;
using Shiny.Mediator;

namespace MauiSherpa.Core.Requests.Profiling;

public record AnalyzeProfilingArtifactRequest(string ArtifactId) : IRequest<ProfilingArtifactAnalysisResult>;
