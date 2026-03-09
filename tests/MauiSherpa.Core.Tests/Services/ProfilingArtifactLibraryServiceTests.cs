using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Profiling;
using MauiSherpa.Core.Services;
using Moq;

namespace MauiSherpa.Core.Tests.Services;

public class ProfilingArtifactLibraryServiceTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _externalRoot;
    private readonly InMemoryEncryptedSettingsService _settingsService = new();
    private readonly Mock<ILoggingService> _logger = new();
    private readonly ProfilingArtifactLibraryService _service;

    public ProfilingArtifactLibraryServiceTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"maui-sherpa-profiling-artifacts-{Guid.NewGuid()}");
        _externalRoot = Path.Combine(Path.GetTempPath(), $"maui-sherpa-profiling-external-{Guid.NewGuid()}");
        _service = new ProfilingArtifactLibraryService(_settingsService, _logger.Object, _testRoot);
    }

    [Fact]
    public async Task SaveArtifactAsync_PersistsArtifactMetadataAndResolvesExternalPath()
    {
        var externalFile = CreateExternalFile("captures/session-1-trace.speedscope.json", "trace");
        var metadata = CreateMetadata("session-1-trace", "session-1", ProfilingArtifactKind.Trace, "session-1-trace.speedscope.json");

        var saved = await _service.SaveArtifactAsync(new ProfilingArtifactLibrarySaveRequest(
            Metadata: metadata,
            ArtifactPath: externalFile));

        saved.IsManagedPath.Should().BeFalse();
        saved.Metadata.RelativePath.Should().Be(Path.GetFullPath(externalFile));
        saved.Metadata.SizeBytes.Should().Be(new FileInfo(externalFile).Length);
        saved.SourcePath.Should().BeNull();

        var artifacts = await _service.GetArtifactsAsync();
        artifacts.Should().ContainSingle()
            .Which.Metadata.Id.Should().Be(metadata.Id);

        var resolvedPath = await _service.GetArtifactPathAsync(metadata.Id);
        resolvedPath.Should().Be(Path.GetFullPath(externalFile));
        _settingsService.Current.ProfilingArtifacts.Should().ContainSingle();
    }

    [Fact]
    public async Task SaveArtifactAsync_CopyToLibrary_CreatesManagedCopy()
    {
        var externalFile = CreateExternalFile("imports/session-2-logs.txt", "hello logs");
        var metadata = CreateMetadata("session-2-logs", "session-2", ProfilingArtifactKind.Logs, "session-2-logs.txt");

        var saved = await _service.SaveArtifactAsync(new ProfilingArtifactLibrarySaveRequest(
            Metadata: metadata,
            ArtifactPath: externalFile,
            CopyToLibrary: true));

        saved.IsManagedPath.Should().BeTrue();
        saved.SourcePath.Should().Be(Path.GetFullPath(externalFile));
        Path.IsPathRooted(saved.Metadata.RelativePath).Should().BeFalse();

        var managedPath = await _service.GetArtifactPathAsync(metadata.Id);
        managedPath.Should().NotBeNull();
        File.Exists(managedPath!).Should().BeTrue();
        File.ReadAllText(managedPath!).Should().Be("hello logs");
    }

    [Fact]
    public async Task SaveArtifactAsync_UpdatesExistingArtifactWithoutDuplicatingEntry()
    {
        var externalFile = CreateExternalFile("captures/session-3-report.json", "{}");
        var original = await _service.SaveArtifactAsync(new ProfilingArtifactLibrarySaveRequest(
            Metadata: CreateMetadata("session-3-report", "session-3", ProfilingArtifactKind.Report, "session-3-report.json"),
            ArtifactPath: externalFile));

        var updatedMetadata = original.Metadata with
        {
            DisplayName = "Updated report",
            Properties = new Dictionary<string, string> { ["scenario"] = "launch" }
        };

        var updated = await _service.SaveArtifactAsync(new ProfilingArtifactLibrarySaveRequest(
            Metadata: updatedMetadata,
            ArtifactPath: externalFile));

        updated.AddedAt.Should().Be(original.AddedAt);
        updated.UpdatedAt.Should().BeOnOrAfter(original.UpdatedAt);
        updated.Metadata.DisplayName.Should().Be("Updated report");
        updated.Metadata.Properties.Should().ContainKey("scenario").WhoseValue.Should().Be("launch");

        var artifacts = await _service.GetArtifactsAsync();
        artifacts.Should().ContainSingle();
    }

    [Fact]
    public async Task DeleteArtifactAsync_RemovesMetadataAndManagedFile()
    {
        var externalFile = CreateExternalFile("imports/session-4-memory.gcdump", "gcdump");
        var saved = await _service.SaveArtifactAsync(new ProfilingArtifactLibrarySaveRequest(
            Metadata: CreateMetadata("session-4-memory", "session-4", ProfilingArtifactKind.Metrics, "session-4-memory.gcdump"),
            ArtifactPath: externalFile,
            CopyToLibrary: true));
        var managedPath = await _service.GetArtifactPathAsync(saved.Metadata.Id);

        await _service.DeleteArtifactAsync(saved.Metadata.Id, deleteFile: true);

        (await _service.GetArtifactsAsync()).Should().BeEmpty();
        File.Exists(managedPath!).Should().BeFalse();
    }

    [Fact]
    public async Task GetArtifactsAsync_FiltersBySessionKindAndExistingFiles()
    {
        var existingTrace = CreateExternalFile("captures/session-5-trace.speedscope.json", "trace");
        var missingLogs = Path.Combine(_externalRoot, "missing", "session-6-logs.txt");

        await _service.SaveArtifactAsync(new ProfilingArtifactLibrarySaveRequest(
            Metadata: CreateMetadata("session-5-trace", "session-5", ProfilingArtifactKind.Trace, "session-5-trace.speedscope.json"),
            ArtifactPath: existingTrace));

        await _service.SaveArtifactAsync(new ProfilingArtifactLibrarySaveRequest(
            Metadata: CreateMetadata("session-6-logs", "session-6", ProfilingArtifactKind.Logs, "session-6-logs.txt"),
            ArtifactPath: missingLogs));

        var bySession = await _service.GetArtifactsAsync(new ProfilingArtifactLibraryQuery(SessionId: "session-5"));
        bySession.Should().ContainSingle().Which.Metadata.Kind.Should().Be(ProfilingArtifactKind.Trace);

        var existingOnly = await _service.GetArtifactsAsync(new ProfilingArtifactLibraryQuery(Kind: ProfilingArtifactKind.Logs, IncludeMissing: false));
        existingOnly.Should().BeEmpty();
    }

    [Fact]
    public void GetDefaultArtifactDirectory_ReturnsSessionScopedLibraryPath()
    {
        var path = _service.GetDefaultArtifactDirectory("session-7");

        path.Should().Be(Path.Combine(_testRoot, "session-7"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }

        if (Directory.Exists(_externalRoot))
        {
            Directory.Delete(_externalRoot, recursive: true);
        }
    }

    private string CreateFile(string relativePath, string contents)
        => CreateFile(_testRoot, relativePath, contents);

    private string CreateExternalFile(string relativePath, string contents)
        => CreateFile(_externalRoot, relativePath, contents);

    private static string CreateFile(string root, string relativePath, string contents)
    {
        var fullPath = Path.Combine(root, relativePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, contents);
        return fullPath;
    }

    private static ProfilingArtifactMetadata CreateMetadata(
        string id,
        string sessionId,
        ProfilingArtifactKind kind,
        string fileName) =>
        new(
            Id: id,
            SessionId: sessionId,
            Kind: kind,
            DisplayName: fileName,
            FileName: fileName,
            RelativePath: null,
            ContentType: "application/octet-stream",
            CreatedAt: DateTimeOffset.UtcNow);

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
