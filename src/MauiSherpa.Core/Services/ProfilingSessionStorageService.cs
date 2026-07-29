using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Profiling;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Manages persistent profiling sessions stored under AppDataPath/profiling/.
/// Each session is a folder containing session.json + artifact files.
/// </summary>
public class ProfilingSessionStorageService : IProfilingSessionStorageService
{
    private readonly string _profilingRoot;
    private readonly ILoggingService _logger;
    private readonly IProfilingArtifactLibraryService _artifactLibrary;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private const string ManifestFileName = "session.json";

    public ProfilingSessionStorageService(
        ILoggingService logger,
        IProfilingArtifactLibraryService artifactLibrary)
        : this(
            logger,
            artifactLibrary,
            Path.Combine(AppDataPath.GetAppDataDirectory(), "profiling"))
    {
    }

    internal ProfilingSessionStorageService(
        ILoggingService logger,
        IProfilingArtifactLibraryService artifactLibrary,
        string profilingRoot)
    {
        _logger = logger;
        _artifactLibrary = artifactLibrary;
        _profilingRoot = Path.GetFullPath(profilingRoot);
        Directory.CreateDirectory(_profilingRoot);
    }

    public async Task<IReadOnlyList<ProfilingSessionManifest>> GetSessionsAsync(CancellationToken ct = default)
    {
        var sessions = new List<ProfilingSessionManifest>();

        if (!Directory.Exists(_profilingRoot))
            return sessions;

        foreach (var dir in Directory.GetDirectories(_profilingRoot))
        {
            ct.ThrowIfCancellationRequested();
            var manifestPath = Path.Combine(dir, ManifestFileName);
            if (!File.Exists(manifestPath))
                continue;

            try
            {
                var manifest = await ReadManifestAsync(manifestPath, ct);
                if (manifest is not null)
                {
                    manifest.DirectoryPath = dir;
                    sessions.Add(manifest);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to read session manifest at {manifestPath}: {ex.Message}");
            }
        }

        // Most recent first
        sessions.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
        return sessions;
    }

    public async Task<ProfilingSessionManifest?> GetSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var dir = Path.Combine(_profilingRoot, SanitizePath(sessionId));
        var manifestPath = Path.Combine(dir, ManifestFileName);

        if (!File.Exists(manifestPath))
            return null;

        var manifest = await ReadManifestAsync(manifestPath, ct);
        if (manifest is not null)
            manifest.DirectoryPath = dir;
        return manifest;
    }

    public async Task SaveSessionAsync(ProfilingSessionManifest manifest, CancellationToken ct = default)
    {
        var dir = GetSessionDirectoryPath(manifest.Id);
        var manifestPath = Path.Combine(dir, ManifestFileName);

        // Update artifact sizes from disk
        foreach (var artifact in manifest.Artifacts)
        {
            var artifactPath = Path.Combine(dir, artifact.FileName);
            if (File.Exists(artifactPath))
            {
                var info = new FileInfo(artifactPath);
                // Use reflection-free approach: create new record with updated size
                if (artifact.SizeBytes is null || artifact.SizeBytes == 0)
                {
                    var idx = manifest.Artifacts.IndexOf(artifact);
                    if (idx >= 0)
                    {
                        manifest.Artifacts[idx] = artifact with { SizeBytes = info.Length };
                    }
                }
            }
        }

        manifest.DirectoryPath = dir;

        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        var pendingManifestPath = $"{manifestPath}.pending";
        await File.WriteAllTextAsync(pendingManifestPath, json, ct);
        File.Move(pendingManifestPath, manifestPath, overwrite: true);

        await SyncArtifactLibraryAsync(manifest, dir, ct);

        _logger.LogInformation($"Session manifest saved: {manifest.Id}");
    }

    public async Task DeleteSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var dir = Path.Combine(_profilingRoot, SanitizePath(sessionId));

        var libraryEntries = await _artifactLibrary.GetArtifactsAsync(
            new ProfilingArtifactLibraryQuery(SessionId: sessionId),
            ct);
        foreach (var entry in libraryEntries)
            await _artifactLibrary.DeleteArtifactAsync(entry.Metadata.Id, deleteFile: false, ct);

        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
            _logger.LogInformation($"Session deleted: {sessionId}");
        }

    }

    public async Task<ProfilingSessionManifest> SaveMauiProfileSessionAsync(
        string sessionId,
        MauiProfileRequest request,
        MauiProfileResult result,
        string? cliVersion = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);

        var sessionDirectory = GetSessionDirectoryPath(sessionId);
        var sourcePaths = new[] { result.OutputPath, result.RawTracePath }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => Path.GetFullPath(x!))
            .Distinct(GetPathComparer())
            .ToArray();

        if (sourcePaths.Length == 0)
            throw new InvalidOperationException("The MAUI CLI did not return an artifact path.");

        var artifacts = new List<ProfilingSessionArtifact>();
        foreach (var sourcePath in sourcePaths)
        {
            ct.ThrowIfCancellationRequested();
            var managedPath = await EnsureManagedArtifactAsync(sourcePath, sessionDirectory, ct);
            var info = new FileInfo(managedPath);
            artifacts.Add(new ProfilingSessionArtifact
            {
                FileName = info.Name,
                Kind = ProfilingArtifactClassifier.Classify(info.Name),
                SizeBytes = info.Length,
                DisplayName = ProfilingArtifactClassifier.GetDisplayName(info.Name)
            });
        }

        RemoveIntermediateFiles(sessionDirectory);

        var startedAt = result.StartedAtUtc ?? DateTimeOffset.UtcNow;        var completedAt = result.CompletedAtUtc ?? DateTimeOffset.UtcNow;
        var framework = string.IsNullOrWhiteSpace(result.Framework) ? null : result.Framework;
        var format = ParseOutputFormat(result.Format) ?? request.Format;
        var rawTraceFileName = result.RawTracePath is null
            ? null
            : artifacts.FirstOrDefault(x =>
                x.FileName.Equals(Path.GetFileName(result.RawTracePath), GetPathComparison()))
                ?.FileName;
        var targetKind = request.Platform == ProfilingTargetPlatform.iOS
            ? ProfilingTargetKind.Simulator
            : request.IsEmulator
                ? ProfilingTargetKind.Emulator
                : ProfilingTargetKind.PhysicalDevice;

        var manifest = new ProfilingSessionManifest
        {
            SchemaVersion = 2,
            Id = sessionId,
            Name = $"{result.ProjectName} — {GetModeDisplayName(request.Mode)} on {result.DeviceName}",
            Status = ProfilingSessionStatus.Completed,
            CreatedAt = startedAt,
            CompletedAt = completedAt,
            Target = new ProfilingSessionTarget
            {
                Platform = request.Platform,
                Kind = targetKind,
                Identifier = result.DeviceId,
                DisplayName = result.DeviceName
            },
            Project = new ProfilingSessionProject
            {
                Path = result.ProjectPath,
                Name = result.ProjectName,
                Configuration = result.Configuration,
                TargetFramework = framework
            },
            CaptureKinds = request.Mode == MauiProfileMode.Startup
                ? [ProfilingCaptureKind.Startup]
                : [ProfilingCaptureKind.Interaction],
            Options = new ProfilingSessionOptions
            {
                LaunchMode = ProfilingCaptureLaunchMode.Launch,
                DiagnosticPort = result.DiagnosticPort ?? 9000,
                SuspendAtStartup = request.Mode == MauiProfileMode.Startup,
                Scenario = request.Mode == MauiProfileMode.Startup
                    ? ProfilingScenarioKind.Launch
                    : ProfilingScenarioKind.Interaction
            },
            Pipeline = new ProfilingSessionPipelineSummary
            {
                Success = true,
                Duration = completedAt - startedAt,
                Steps = []
            },
            MauiProfile = new MauiProfileSessionDetails
            {
                Mode = request.Mode,
                Format = format,
                CliVersion = cliVersion,
                Framework = framework,
                RawTraceFileName = rawTraceFileName,
                UsedStoppingEvent = result.UsedStoppingEvent,
                StartedAtUtc = result.StartedAtUtc,
                CompletedAtUtc = result.CompletedAtUtc
            },
            Artifacts = artifacts
        };

        await SaveSessionAsync(manifest, ct);
        return manifest;
    }

    public async Task<ProfilingSessionManifest> ImportArtifactAsync(
        string artifactPath,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);
        var sourcePath = Path.GetFullPath(artifactPath);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("The profiling artifact could not be found.", sourcePath);
        if (!ProfilingArtifactClassifier.IsSupported(sourcePath))
            throw new NotSupportedException($"'{Path.GetExtension(sourcePath)}' is not a supported profiling artifact.");

        var sessionName = ProfilingArtifactClassifier.GetBaseName(sourcePath);
        var sessionId = GenerateSessionId(sessionName);
        var sessionDirectory = GetSessionDirectoryPath(sessionId);
        var managedPath = await EnsureManagedArtifactAsync(sourcePath, sessionDirectory, ct);
        var info = new FileInfo(managedPath);
        var now = DateTimeOffset.UtcNow;

        var manifest = new ProfilingSessionManifest
        {
            SchemaVersion = 2,
            Id = sessionId,
            Name = $"{sessionName} — Imported",
            Status = ProfilingSessionStatus.Completed,
            CreatedAt = now,
            CompletedAt = now,
            Target = new ProfilingSessionTarget
            {
                Platform = ProfilingTargetPlatform.Unknown,
                Kind = ProfilingTargetKind.Unknown,
                Identifier = "imported",
                DisplayName = "Imported artifact"
            },
            CaptureKinds = [],
            Options = new ProfilingSessionOptions
            {
                LaunchMode = ProfilingCaptureLaunchMode.Attach,
                Scenario = ProfilingScenarioKind.Interaction
            },
            Artifacts =
            [
                new ProfilingSessionArtifact
                {
                    FileName = info.Name,
                    Kind = ProfilingArtifactClassifier.Classify(info.Name),
                    SizeBytes = info.Length,
                    DisplayName = ProfilingArtifactClassifier.GetDisplayName(info.Name)
                }
            ]
        };

        await SaveSessionAsync(manifest, ct);
        return manifest;
    }

    public string GetSessionDirectoryPath(string sessionId)
    {
        var dir = Path.Combine(_profilingRoot, SanitizePath(sessionId));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public string GenerateSessionId(string? projectName = null)
    {
        var datePart = DateTime.Now.ToString("yyyy-MM-dd");
        var namePart = SanitizePath(projectName ?? "session");
        var baseName = $"{datePart}_{namePart}";

        // Find next available run number
        var runNumber = 1;
        while (Directory.Exists(Path.Combine(_profilingRoot, $"{baseName}_{runNumber}")))
        {
            runNumber++;
        }

        return $"{baseName}_{runNumber}";
    }

    public async Task ExportSessionAsync(string sessionId, string outputZipPath, CancellationToken ct = default)
    {
        var dir = Path.Combine(_profilingRoot, SanitizePath(sessionId));

        if (!Directory.Exists(dir))
            throw new DirectoryNotFoundException($"Session directory not found: {dir}");

        // Delete existing zip if present (save dialog may have created empty file)
        if (File.Exists(outputZipPath))
            File.Delete(outputZipPath);

        await Task.Run(() => ZipFile.CreateFromDirectory(dir, outputZipPath), ct);
        _logger.LogInformation($"Session exported: {sessionId} → {outputZipPath}");
    }

    public async Task<ProfilingSessionManifest?> ImportSessionAsync(string zipPath, CancellationToken ct = default)
    {
        if (!File.Exists(zipPath))
            return null;

        // Extract to a temp directory first to read manifest
        var tempDir = Path.Combine(Path.GetTempPath(), $"sherpa-import-{Guid.NewGuid():N}");
        try
        {
            await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, tempDir), ct);

            var manifestPath = Path.Combine(tempDir, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                _logger.LogWarning($"Imported zip has no {ManifestFileName}");
                return null;
            }

            var manifest = await ReadManifestAsync(manifestPath, ct);
            if (manifest is null)
                return null;

            // Move to managed location (use a new ID if collision)
            var targetId = manifest.Id;
            var targetDir = Path.Combine(_profilingRoot, SanitizePath(targetId));
            if (Directory.Exists(targetDir))
            {
                // Generate new ID to avoid collision
                targetId = GenerateSessionId(manifest.Name);
                targetDir = Path.Combine(_profilingRoot, SanitizePath(targetId));
                manifest = manifest with { Id = targetId };
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetDir)!);

            // Move the extracted folder to the managed location
            if (Directory.Exists(targetDir))
                Directory.Delete(targetDir, true);
            Directory.Move(tempDir, targetDir);

            manifest.DirectoryPath = targetDir;
            await SaveSessionAsync(manifest, ct);
            _logger.LogInformation($"Session imported: {manifest.Id} from {zipPath}");
            return manifest;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to import session from {zipPath}: {ex.Message}", ex);
            return null;
        }
        finally
        {
            // Clean up temp directory if it still exists
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); }
                catch { /* best effort */ }
            }
        }
    }

    private static async Task<ProfilingSessionManifest?> ReadManifestAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ProfilingSessionManifest>(stream, JsonOptions, ct);
    }

    /// <summary>
    /// The CLI leaves a TraceEvent index (<c>.etlx</c>) beside MIBC output. It is derived from
    /// the raw trace, is often larger than the trace itself, and nothing in Sherpa reads it.
    /// </summary>
    private void RemoveIntermediateFiles(string sessionDirectory)
    {
        try
        {
            if (!Directory.Exists(sessionDirectory))
                return;

            foreach (var file in Directory.EnumerateFiles(sessionDirectory, "*.etlx"))
                File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(
                $"Could not remove intermediate profiling files in '{sessionDirectory}': {ex.Message}");
        }
    }

    private async Task<string> EnsureManagedArtifactAsync(
        string sourcePath,
        string sessionDirectory,
        CancellationToken ct)
    {
        var sourceInfo = new FileInfo(sourcePath);
        if (!sourceInfo.Exists || sourceInfo.Length == 0)
            throw new InvalidDataException($"Profiling artifact '{sourcePath}' is missing or empty.");

        var destinationPath = Path.Combine(sessionDirectory, sourceInfo.Name);
        if (!string.Equals(
                Path.GetFullPath(sourcePath),
                Path.GetFullPath(destinationPath),
                GetPathComparison()))
        {
            await using var source = File.OpenRead(sourcePath);
            await using var destination = File.Create(destinationPath);
            await source.CopyToAsync(destination, ct);
        }

        return destinationPath;
    }

    private async Task SyncArtifactLibraryAsync(
        ProfilingSessionManifest manifest,
        string sessionDirectory,
        CancellationToken ct)
    {
        foreach (var artifact in manifest.Artifacts)
        {
            var artifactPath = Path.Combine(sessionDirectory, artifact.FileName);
            if (!File.Exists(artifactPath))
                continue;

            var metadata = new ProfilingArtifactMetadata(
                Id: $"{manifest.Id}:{artifact.FileName}",
                SessionId: manifest.Id,
                Kind: artifact.Kind,
                DisplayName: artifact.DisplayName ?? ProfilingArtifactClassifier.GetDisplayName(artifact.FileName),
                FileName: artifact.FileName,
                RelativePath: artifactPath,
                ContentType: ProfilingArtifactClassifier.GetContentType(artifact.FileName),
                CreatedAt: manifest.CompletedAt ?? manifest.CreatedAt,
                SizeBytes: artifact.SizeBytes);

            await _artifactLibrary.SaveArtifactAsync(
                new ProfilingArtifactLibrarySaveRequest(
                    metadata,
                    ArtifactPath: artifactPath,
                    CopyToLibrary: false),
                ct);
        }
    }

    private static string GetModeDisplayName(MauiProfileMode mode) => mode switch
    {
        MauiProfileMode.Startup => "Startup",
        MauiProfileMode.Interaction => "Interaction",
        _ => mode.ToString()
    };

    private static MauiProfileOutputFormat? ParseOutputFormat(string? format) => format?.Trim().ToLowerInvariant() switch
    {
        "nettrace" => MauiProfileOutputFormat.NetTrace,
        "speedscope" => MauiProfileOutputFormat.Speedscope,
        "mibc" => MauiProfileOutputFormat.Mibc,
        _ => null
    };

    private static StringComparer GetPathComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static StringComparison GetPathComparison() =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string SanitizePath(string input)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new char[input.Length];
        for (int i = 0; i < input.Length; i++)
        {
            sanitized[i] = Array.IndexOf(invalid, input[i]) >= 0 ? '_' : input[i];
        }
        return new string(sanitized).Trim('.');
    }
}
