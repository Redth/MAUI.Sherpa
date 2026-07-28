using System.IO.Compression;
using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Profiling;
using MauiSherpa.Core.Services;
using Moq;

namespace MauiSherpa.Core.Tests.Services;

public class ProfilingSessionStorageServiceTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _sessionRoot;
    private readonly string _externalRoot;
    private readonly InMemoryEncryptedSettingsService _settings = new();
    private readonly ProfilingArtifactLibraryService _artifactLibrary;
    private readonly ProfilingSessionStorageService _service;

    public ProfilingSessionStorageServiceTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"maui-sherpa-profile-sessions-{Guid.NewGuid():N}");
        _sessionRoot = Path.Combine(_testRoot, "sessions");
        _externalRoot = Path.Combine(_testRoot, "external");
        var libraryRoot = Path.Combine(_testRoot, "library");
        var logger = new Mock<ILoggingService>();

        _artifactLibrary = new ProfilingArtifactLibraryService(_settings, logger.Object, libraryRoot);
        _service = new ProfilingSessionStorageService(logger.Object, _artifactLibrary, _sessionRoot);
    }

    [Fact]
    public async Task SaveMauiProfileSessionAsync_UsesCliArtifactsAndSynchronizesLibrary()
    {
        var primaryPath = CreateExternalArtifact("capture.speedscope.json");
        var rawTracePath = CreateExternalArtifact("capture.nettrace");
        CreateExternalArtifact("unreported.gcdump");
        var startedAt = DateTimeOffset.Parse("2026-02-20T10:00:00Z");
        var completedAt = startedAt.AddSeconds(12);
        var request = new MauiProfileRequest
        {
            ProjectPath = "/work/My App.csproj",
            Platform = ProfilingTargetPlatform.Android,
            DeviceId = "emulator-5554",
            DeviceName = "Pixel 9",
            IsEmulator = true,
            Mode = MauiProfileMode.Interaction,
            Format = MauiProfileOutputFormat.Speedscope,
            OutputPath = Path.Combine(_externalRoot, "requested.nettrace")
        };
        var result = new MauiProfileResult
        {
            ProjectPath = request.ProjectPath,
            ProjectName = "My App",
            Framework = "net10.0-android",
            Platform = "android",
            DeviceId = request.DeviceId,
            DeviceName = request.DeviceName!,
            Configuration = "Release",
            Format = "speedscope",
            OutputPath = primaryPath,
            RawTracePath = rawTracePath,
            DiagnosticPort = 9300,
            StartedAtUtc = startedAt,
            CompletedAtUtc = completedAt
        };

        var manifest = await _service.SaveMauiProfileSessionAsync(
            "session-1",
            request,
            result,
            cliVersion: "1.2.3");

        manifest.SchemaVersion.Should().Be(2);
        manifest.Status.Should().Be(ProfilingSessionStatus.Completed);
        manifest.CaptureKinds.Should().Equal(ProfilingCaptureKind.Interaction);
        manifest.Target.Kind.Should().Be(ProfilingTargetKind.Emulator);
        manifest.MauiProfile.Should().NotBeNull();
        manifest.MauiProfile!.Mode.Should().Be(MauiProfileMode.Interaction);
        manifest.MauiProfile.Format.Should().Be(MauiProfileOutputFormat.Speedscope);
        manifest.MauiProfile.CliVersion.Should().Be("1.2.3");
        manifest.MauiProfile.RawTraceFileName.Should().Be("capture.nettrace");
        manifest.Artifacts.Select(x => x.FileName).Should().BeEquivalentTo(
            ["capture.speedscope.json", "capture.nettrace"]);
        manifest.Artifacts.Should().OnlyContain(x => x.Kind == ProfilingArtifactKind.Trace);
        File.Exists(Path.Combine(manifest.DirectoryPath!, "capture.speedscope.json")).Should().BeTrue();
        File.Exists(Path.Combine(manifest.DirectoryPath!, "capture.nettrace")).Should().BeTrue();
        File.Exists(Path.Combine(manifest.DirectoryPath!, "unreported.gcdump")).Should().BeFalse();
        File.Exists(Path.Combine(manifest.DirectoryPath!, "session.json.pending")).Should().BeFalse();

        var libraryEntries = await _artifactLibrary.GetArtifactsAsync();
        libraryEntries.Should().HaveCount(2);
        libraryEntries.Select(x => x.Metadata.Id).Should().BeEquivalentTo(
            ["session-1:capture.speedscope.json", "session-1:capture.nettrace"]);
        libraryEntries.Should().OnlyContain(x => x.Metadata.SessionId == "session-1");
    }

    [Fact]
    public async Task SaveMauiProfileSessionAsync_AcceptsRecoveredResultAndDropsIntermediateFiles()
    {
        var sessionDirectory = _service.GetSessionDirectoryPath("recovered-1");
        Directory.CreateDirectory(sessionDirectory);
        var primaryPath = Path.Combine(sessionDirectory, "capture.mibc");
        var rawTracePath = Path.Combine(sessionDirectory, "capture.nettrace");
        var intermediatePath = Path.Combine(sessionDirectory, "capture.etlx");
        await File.WriteAllTextAsync(primaryPath, "mibc");
        await File.WriteAllTextAsync(rawTracePath, "trace");
        await File.WriteAllTextAsync(intermediatePath, "index");

        var request = new MauiProfileRequest
        {
            ProjectPath = "/work/MauiApp.csproj",
            Platform = ProfilingTargetPlatform.Android,
            DeviceId = "emulator-5554",
            DeviceName = "emulator-5554",
            IsEmulator = true,
            Mode = MauiProfileMode.Startup,
            Format = MauiProfileOutputFormat.Mibc,
            OutputPath = rawTracePath
        };
        var result = MauiProfileArtifactRecovery.TryRecover(
            request,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow);

        result.Should().NotBeNull();

        var manifest = await _service.SaveMauiProfileSessionAsync("recovered-1", request, result!);

        manifest.Status.Should().Be(ProfilingSessionStatus.Completed);
        manifest.MauiProfile!.Format.Should().Be(MauiProfileOutputFormat.Mibc);
        manifest.MauiProfile.Framework.Should().BeNull();
        manifest.Project!.TargetFramework.Should().BeNull();
        manifest.MauiProfile.RawTraceFileName.Should().Be("capture.nettrace");
        manifest.Artifacts.Select(x => x.FileName).Should().BeEquivalentTo(
            ["capture.mibc", "capture.nettrace"]);
        File.Exists(intermediatePath).Should().BeFalse();
        File.Exists(primaryPath).Should().BeTrue();
        File.Exists(rawTracePath).Should().BeTrue();
    }

    [Theory]
    [InlineData("standalone.nettrace", ProfilingArtifactKind.Trace)]
    [InlineData("standalone.speedscope.json", ProfilingArtifactKind.Trace)]
    [InlineData("standalone.mibc", ProfilingArtifactKind.Mibc)]
    [InlineData("standalone.gcdump", ProfilingArtifactKind.GcDump)]
    public async Task ImportArtifactAsync_ClassifiesAndIndexesSupportedArtifacts(
        string fileName,
        ProfilingArtifactKind expectedKind)
    {
        var sourcePath = CreateExternalArtifact(fileName);

        var manifest = await _service.ImportArtifactAsync(sourcePath);

        manifest.SchemaVersion.Should().Be(2);
        manifest.MauiProfile.Should().BeNull();
        manifest.Artifacts.Should().ContainSingle();
        manifest.Artifacts[0].Kind.Should().Be(expectedKind);
        File.Exists(Path.Combine(manifest.DirectoryPath!, fileName)).Should().BeTrue();

        var libraryEntries = await _artifactLibrary.GetArtifactsAsync();
        libraryEntries.Should().ContainSingle();
        libraryEntries[0].Metadata.Kind.Should().Be(expectedKind);
        libraryEntries[0].Metadata.SessionId.Should().Be(manifest.Id);
    }

    [Fact]
    public async Task GetSessionAsync_LoadsLegacyManifestWithoutCliMetadata()
    {
        const string sessionId = "legacy-session";
        var sessionDirectory = Path.Combine(_sessionRoot, sessionId);
        Directory.CreateDirectory(sessionDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(sessionDirectory, "session.json"),
            LegacyManifestJson(sessionId, "Legacy Session"));

        var manifest = await _service.GetSessionAsync(sessionId);

        manifest.Should().NotBeNull();
        manifest!.SchemaVersion.Should().Be(1);
        manifest.MauiProfile.Should().BeNull();
        manifest.Target.Platform.Should().Be(ProfilingTargetPlatform.MacCatalyst);
        manifest.Options.LaunchMode.Should().Be(ProfilingCaptureLaunchMode.Launch);
        manifest.CaptureKinds.Should().Equal(ProfilingCaptureKind.Cpu, ProfilingCaptureKind.Memory);
    }

    [Fact]
    public async Task ImportSessionAsync_PreservesLegacyArchiveAndAllocatesNewIdOnCollision()
    {
        const string collidingId = "shared-session";
        await _service.SaveSessionAsync(CreateManifest(collidingId, "Existing Session"));

        var archiveSource = Path.Combine(_testRoot, "legacy-archive");
        Directory.CreateDirectory(archiveSource);
        await File.WriteAllTextAsync(
            Path.Combine(archiveSource, "session.json"),
            LegacyManifestJson(collidingId, "Legacy Archive"));
        await File.WriteAllBytesAsync(
            Path.Combine(archiveSource, "capture.nettrace"),
            [1, 2, 3, 4]);
        var archivePath = Path.Combine(_testRoot, "legacy-session.zip");
        ZipFile.CreateFromDirectory(archiveSource, archivePath);

        var imported = await _service.ImportSessionAsync(archivePath);

        imported.Should().NotBeNull();
        imported!.Id.Should().NotBe(collidingId);
        imported.SchemaVersion.Should().Be(1);
        imported.MauiProfile.Should().BeNull();
        File.Exists(Path.Combine(imported.DirectoryPath!, "capture.nettrace")).Should().BeTrue();

        var sessions = await _service.GetSessionsAsync();
        sessions.Select(x => x.Id).Should().Contain([collidingId, imported.Id]);
        var libraryEntries = await _artifactLibrary.GetArtifactsAsync(
            new ProfilingArtifactLibraryQuery(SessionId: imported.Id));
        libraryEntries.Should().ContainSingle();
        libraryEntries[0].Metadata.Id.Should().Be($"{imported.Id}:capture.nettrace");
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
            Directory.Delete(_testRoot, recursive: true);
    }

    private string CreateExternalArtifact(string fileName)
    {
        Directory.CreateDirectory(_externalRoot);
        var path = Path.Combine(_externalRoot, fileName);
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        return path;
    }

    private static ProfilingSessionManifest CreateManifest(string id, string name) => new()
    {
        Id = id,
        Name = name,
        Status = ProfilingSessionStatus.Completed,
        CreatedAt = DateTimeOffset.Parse("2026-02-20T10:00:00Z"),
        CompletedAt = DateTimeOffset.Parse("2026-02-20T10:00:01Z"),
        Target = new ProfilingSessionTarget
        {
            Platform = ProfilingTargetPlatform.Android,
            Kind = ProfilingTargetKind.Emulator,
            Identifier = "emulator-5554",
            DisplayName = "Pixel"
        },
        CaptureKinds = [ProfilingCaptureKind.Startup],
        Options = new ProfilingSessionOptions()
    };

    private static string LegacyManifestJson(string id, string name) => $$"""
        {
          "id": "{{id}}",
          "name": "{{name}}",
          "status": "completed",
          "createdAt": "2026-02-20T10:00:00+00:00",
          "completedAt": "2026-02-20T10:00:01+00:00",
          "target": {
            "platform": "macCatalyst",
            "kind": "desktop",
            "identifier": "legacy-host",
            "displayName": "Legacy Host"
          },
          "captureKinds": [ "cpu", "memory" ],
          "options": {
            "launchMode": "launch",
            "diagnosticPort": 9000,
            "suspendAtStartup": true,
            "scenario": "launch"
          },
          "artifacts": [
            {
              "fileName": "capture.nettrace",
              "kind": "trace",
              "sizeBytes": 4,
              "displayName": "Trace"
            }
          ]
        }
        """;

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
