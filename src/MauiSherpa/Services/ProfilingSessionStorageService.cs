using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Profiling;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Services;

/// <summary>
/// Manages persistent profiling sessions stored under AppDataPath/profiling/.
/// Each session is a folder containing session.json + artifact files.
/// </summary>
public class ProfilingSessionStorageService : IProfilingSessionStorageService
{
    private readonly string _profilingRoot;
    private readonly ILoggingService _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private const string ManifestFileName = "session.json";

    public ProfilingSessionStorageService(ILoggingService logger)
    {
        _logger = logger;
        _profilingRoot = Path.Combine(AppDataPath.GetAppDataDirectory(), "profiling");
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
        await File.WriteAllTextAsync(manifestPath, json, ct);

        _logger.LogInformation($"Session manifest saved: {manifest.Id}");
    }

    public Task DeleteSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var dir = Path.Combine(_profilingRoot, SanitizePath(sessionId));

        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
            _logger.LogInformation($"Session deleted: {sessionId}");
        }

        return Task.CompletedTask;
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
            var targetDir = Path.Combine(_profilingRoot, SanitizePath(manifest.Id));
            if (Directory.Exists(targetDir))
            {
                // Generate new ID to avoid collision
                var newId = GenerateSessionId(manifest.Name);
                targetDir = Path.Combine(_profilingRoot, SanitizePath(newId));
                // We don't change manifest.Id since the record is immutable — just store under new folder
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetDir)!);

            // Move the extracted folder to the managed location
            if (Directory.Exists(targetDir))
                Directory.Delete(targetDir, true);
            Directory.Move(tempDir, targetDir);

            manifest.DirectoryPath = targetDir;
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
