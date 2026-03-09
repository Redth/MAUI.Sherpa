using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Profiling;
using MauiSherpa.Core.Services;
using Moq;

namespace MauiSherpa.Core.Tests.Services;

public class ProfilingArtifactAnalysisServiceTests : IDisposable
{
    private readonly string _libraryRoot;
    private readonly string _externalRoot;
    private readonly InMemoryEncryptedSettingsService _settingsService = new();
    private readonly Mock<ILoggingService> _logger = new();
    private readonly ProfilingArtifactLibraryService _artifactLibraryService;
    private readonly ProfilingArtifactAnalysisService _analysisService;

    public ProfilingArtifactAnalysisServiceTests()
    {
        _libraryRoot = Path.Combine(Path.GetTempPath(), $"maui-sherpa-profiling-analysis-library-{Guid.NewGuid():N}");
        _externalRoot = Path.Combine(Path.GetTempPath(), $"maui-sherpa-profiling-analysis-external-{Guid.NewGuid():N}");
        _artifactLibraryService = new ProfilingArtifactLibraryService(_settingsService, _logger.Object, _libraryRoot);
        _analysisService = new ProfilingArtifactAnalysisService(_artifactLibraryService, _logger.Object);
    }

    [Fact]
    public async Task AnalyzeArtifactAsync_SpeedscopeTrace_ReturnsHotspotSummary()
    {
        var tracePath = CreateExternalFile("session-1-trace.speedscope.json", """
            {
              "$schema": "https://www.speedscope.app/file-format-schema.json",
              "shared": {
                "frames": [
                  { "name": "RenderFrame", "file": "Render.cs", "line": 42 },
                  { "name": "LayoutCycle", "file": "Layout.cs", "line": 15 }
                ]
              },
              "profiles": [
                {
                  "type": "sampled",
                  "name": "UI Thread",
                  "unit": "milliseconds",
                  "startValue": 0,
                  "endValue": 100,
                  "samples": [[0], [0], [1], [0]],
                  "weights": [40, 30, 10, 20]
                }
              ]
            }
            """);

        await SaveArtifactAsync(
            id: "session-1-trace",
            sessionId: "session-1",
            kind: ProfilingArtifactKind.Trace,
            fileName: "session-1-trace.speedscope.json",
            artifactPath: tracePath,
            contentType: "application/json");

        var result = await _analysisService.AnalyzeArtifactAsync("session-1-trace");

        result.Analysis.Should().NotBeNull();
        result.Analysis!.Kind.Should().Be(ProfilingAnalysisKind.Speedscope);
        result.Analysis.Summary.Should().Contain("RenderFrame");
        result.Analysis.Hotspots.Should().NotBeEmpty();
        result.Analysis.Hotspots[0].Name.Should().Be("RenderFrame");
        result.Analysis.Hotspots[0].PercentOfTrace.Should().BeApproximately(90, 0.1);
        result.Analysis.Metrics.Should().Contain(metric => metric.Key == "durationMs" && metric.NumericValue == 100);
        result.Analysis.Insights.Should().Contain(insight => insight.Title == "Single hotspot dominates the trace");
    }

    [Fact]
    public async Task AnalyzeArtifactAsync_LogArtifact_ReturnsCountsAndRecurringHotspots()
    {
        var logPath = CreateExternalFile("session-2-logs.txt", """
            2025-01-01T12:00:00Z INFO Starting capture
            2025-01-01T12:00:01Z WARN Slow request to /api/items
            2025-01-01T12:00:02Z ERROR Timeout talking to backend
            2025-01-01T12:00:03Z ERROR Timeout talking to backend
            2025-01-01T12:00:04Z INFO Capture complete
            """);

        await SaveArtifactAsync(
            id: "session-2-logs",
            sessionId: "session-2",
            kind: ProfilingArtifactKind.Logs,
            fileName: "session-2-logs.txt",
            artifactPath: logPath,
            contentType: "text/plain");

        var result = await _analysisService.AnalyzeArtifactAsync("session-2-logs");

        result.Analysis.Should().NotBeNull();
        result.Analysis!.Kind.Should().Be(ProfilingAnalysisKind.Logs);
        result.Analysis.Summary.Should().Contain("2 error");
        result.Analysis.Hotspots.Should().ContainSingle();
        result.Analysis.Hotspots[0].Name.Should().Be("ERROR Timeout talking to backend");
        result.Analysis.Metrics.Should().Contain(metric => metric.Key == "warningCount" && metric.NumericValue == 1);
        result.Analysis.Metrics.Should().Contain(metric => metric.Key == "errorCount" && metric.NumericValue == 2);
        result.Analysis.Insights.Should().Contain(insight => insight.Title == "Errors detected in captured logs");
    }

    [Fact]
    public async Task AnalyzeArtifactAsync_UnsupportedExport_FallsBackToMetadataSummary()
    {
        var exportPath = CreateExternalFile("session-3-memory.gcdump", "gcdump-binary-placeholder");

        await SaveArtifactAsync(
            id: "session-3-memory",
            sessionId: "session-3",
            kind: ProfilingArtifactKind.Export,
            fileName: "session-3-memory.gcdump",
            artifactPath: exportPath,
            contentType: "application/octet-stream");

        var result = await _analysisService.AnalyzeArtifactAsync("session-3-memory");

        result.Analysis.Should().NotBeNull();
        result.Analysis!.Kind.Should().Be(ProfilingAnalysisKind.Metadata);
        result.Analysis.Summary.Should().Contain("metadata-only analysis");
        result.Analysis.Notes.Should().Contain(note => note.Contains("specialized tool", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AnalyzeArtifactsAsync_UsesArtifactLibraryQuery()
    {
        var tracePath = CreateExternalFile("session-4-trace.json", "{ \"value\": 1 }");
        var logPath = CreateExternalFile("session-5-log.txt", "2025-01-01T00:00:00Z INFO Hello");

        await SaveArtifactAsync("session-4-trace", "session-4", ProfilingArtifactKind.Report, "session-4-trace.json", tracePath, "application/json");
        await SaveArtifactAsync("session-5-log", "session-5", ProfilingArtifactKind.Logs, "session-5-log.txt", logPath, "text/plain");

        var results = await _analysisService.AnalyzeArtifactsAsync(new ProfilingArtifactLibraryQuery(SessionId: "session-4"));

        results.Should().ContainSingle();
        results[0].Artifact.Id.Should().Be("session-4-trace");
        results[0].Kind.Should().Be(ProfilingAnalysisKind.Json);
    }

    public void Dispose()
    {
        if (Directory.Exists(_libraryRoot))
        {
            Directory.Delete(_libraryRoot, recursive: true);
        }

        if (Directory.Exists(_externalRoot))
        {
            Directory.Delete(_externalRoot, recursive: true);
        }
    }

    private async Task SaveArtifactAsync(
        string id,
        string sessionId,
        ProfilingArtifactKind kind,
        string fileName,
        string artifactPath,
        string contentType)
    {
        await _artifactLibraryService.SaveArtifactAsync(new ProfilingArtifactLibrarySaveRequest(
            CreateMetadata(id, sessionId, kind, fileName, contentType),
            ArtifactPath: artifactPath));
    }

    private string CreateExternalFile(string relativePath, string contents)
    {
        var path = Path.Combine(_externalRoot, relativePath);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, contents);
        return path;
    }

    private static ProfilingArtifactMetadata CreateMetadata(
        string id,
        string sessionId,
        ProfilingArtifactKind kind,
        string fileName,
        string contentType) =>
        new(
            Id: id,
            SessionId: sessionId,
            Kind: kind,
            DisplayName: fileName,
            FileName: fileName,
            RelativePath: null,
            ContentType: contentType,
            CreatedAt: DateTimeOffset.UtcNow,
            Properties: new Dictionary<string, string>
            {
                ["targetPlatform"] = "MacCatalyst",
                ["scenario"] = "Launch",
                ["category"] = kind.ToString()
            });

    private sealed class InMemoryEncryptedSettingsService : IEncryptedSettingsService
    {
        public MauiSherpaSettings Current { get; private set; } = new();

        public event Action? OnSettingsChanged;

        public Task<MauiSherpaSettings> GetSettingsAsync() => Task.FromResult(Current);

        public Task SaveSettingsAsync(MauiSherpaSettings settings)
        {
            Current = settings;
            OnSettingsChanged?.Invoke();
            return Task.CompletedTask;
        }

        public Task UpdateSettingsAsync(Func<MauiSherpaSettings, MauiSherpaSettings> transform) =>
            SaveSettingsAsync(transform(Current));

        public Task<bool> SettingsExistAsync() => Task.FromResult(true);
    }
}
