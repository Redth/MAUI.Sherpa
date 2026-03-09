using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Profiling;

namespace MauiSherpa.Core.Services;

public class ProfilingArtifactLibraryService : IProfilingArtifactLibraryService
{
    private readonly IEncryptedSettingsService _settingsService;
    private readonly ILoggingService _loggingService;
    private readonly string _artifactLibraryRoot;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public event Action? OnArtifactsChanged;

    public ProfilingArtifactLibraryService(
        IEncryptedSettingsService settingsService,
        ILoggingService loggingService)
        : this(
            settingsService,
            loggingService,
            Path.Combine(AppDataPath.GetAppDataDirectory(), "profiling-artifacts"))
    {
    }

    internal ProfilingArtifactLibraryService(
        IEncryptedSettingsService settingsService,
        ILoggingService loggingService,
        string artifactLibraryRoot)
    {
        _settingsService = settingsService;
        _loggingService = loggingService;
        _artifactLibraryRoot = Path.GetFullPath(artifactLibraryRoot);
    }

    public async Task<IReadOnlyList<ProfilingArtifactLibraryEntry>> GetArtifactsAsync(
        ProfilingArtifactLibraryQuery? query = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var settings = await _settingsService.GetSettingsAsync();
        IEnumerable<ProfilingArtifactLibraryEntry> artifacts = settings.ProfilingArtifacts;

        if (!string.IsNullOrWhiteSpace(query?.SessionId))
        {
            artifacts = artifacts.Where(a =>
                a.Metadata.SessionId.Equals(query.SessionId, StringComparison.OrdinalIgnoreCase));
        }

        if (query?.Kind is ProfilingArtifactKind kind)
        {
            artifacts = artifacts.Where(a => a.Metadata.Kind == kind);
        }

        if (query is { IncludeMissing: false })
        {
            artifacts = artifacts.Where(a =>
            {
                var path = ResolveArtifactPath(a);
                return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
            });
        }

        return artifacts
            .OrderByDescending(a => a.UpdatedAt)
            .ThenByDescending(a => a.Metadata.CreatedAt)
            .ToArray();
    }

    public async Task<ProfilingArtifactLibraryEntry?> GetArtifactAsync(string artifactId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ValidateRequiredText(artifactId, nameof(artifactId));

        var settings = await _settingsService.GetSettingsAsync();
        return settings.ProfilingArtifacts.FirstOrDefault(a =>
            a.Metadata.Id.Equals(artifactId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<ProfilingArtifactLibraryEntry> SaveArtifactAsync(
        ProfilingArtifactLibrarySaveRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(request.Metadata);

        await _gate.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();

            var storage = await PrepareStorageAsync(request, ct);
            var now = DateTimeOffset.UtcNow;
            var metadata = request.Metadata with
            {
                RelativePath = storage.StoredPath,
                SizeBytes = storage.SizeBytes ?? request.Metadata.SizeBytes,
                CreatedAt = request.Metadata.CreatedAt == default ? now : request.Metadata.CreatedAt
            };

            ProfilingArtifactLibraryEntry? savedEntry = null;
            await _settingsService.UpdateSettingsAsync(settings =>
            {
                var artifacts = settings.ProfilingArtifacts.ToList();
                var existingIndex = artifacts.FindIndex(a =>
                    a.Metadata.Id.Equals(metadata.Id, StringComparison.OrdinalIgnoreCase));

                if (existingIndex >= 0)
                {
                    var existing = artifacts[existingIndex];
                    savedEntry = existing with
                    {
                        Metadata = metadata,
                        IsManagedPath = storage.IsManagedPath,
                        SourcePath = storage.SourcePath ?? existing.SourcePath,
                        UpdatedAt = now
                    };
                    artifacts[existingIndex] = savedEntry;
                }
                else
                {
                    savedEntry = new ProfilingArtifactLibraryEntry(
                        Metadata: metadata,
                        IsManagedPath: storage.IsManagedPath,
                        SourcePath: storage.SourcePath,
                        AddedAt: now,
                        UpdatedAt: now);
                    artifacts.Add(savedEntry);
                }

                return settings with { ProfilingArtifacts = artifacts };
            });

            OnArtifactsChanged?.Invoke();
            return savedEntry!;
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Failed to save profiling artifact '{request.Metadata.Id}': {ex.Message}", ex);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteArtifactAsync(string artifactId, bool deleteFile = false, CancellationToken ct = default)
    {
        ValidateRequiredText(artifactId, nameof(artifactId));

        await _gate.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();

            var entry = await GetArtifactAsync(artifactId, ct);
            if (entry is null)
            {
                return;
            }

            await _settingsService.UpdateSettingsAsync(settings => settings with
            {
                ProfilingArtifacts = settings.ProfilingArtifacts
                    .Where(a => !a.Metadata.Id.Equals(artifactId, StringComparison.OrdinalIgnoreCase))
                    .ToList()
            });

            if (deleteFile)
            {
                var path = ResolveArtifactPath(entry);
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    File.Delete(path);
                    DeleteEmptyManagedDirectories(path, entry.IsManagedPath);
                }
            }

            OnArtifactsChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Failed to delete profiling artifact '{artifactId}': {ex.Message}", ex);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> GetArtifactPathAsync(string artifactId, CancellationToken ct = default)
    {
        var artifact = await GetArtifactAsync(artifactId, ct);
        return artifact is null ? null : ResolveArtifactPath(artifact);
    }

    public string GetDefaultArtifactDirectory(string sessionId)
    {
        ValidateRequiredText(sessionId, nameof(sessionId));
        return Path.Combine(_artifactLibraryRoot, SanitizePathSegment(sessionId));
    }

    private async Task<ResolvedArtifactStorage> PrepareStorageAsync(
        ProfilingArtifactLibrarySaveRequest request,
        CancellationToken ct)
    {
        var candidatePath = request.ArtifactPath ?? request.Metadata.RelativePath;
        var normalizedCandidatePath = string.IsNullOrWhiteSpace(candidatePath)
            ? null
            : Path.GetFullPath(candidatePath.Trim());

        if (request.CopyToLibrary)
        {
            if (string.IsNullOrWhiteSpace(normalizedCandidatePath))
            {
                throw new InvalidOperationException("A source path is required when copying a profiling artifact into the library.");
            }

            if (!File.Exists(normalizedCandidatePath))
            {
                throw new FileNotFoundException("The profiling artifact source file could not be found.", normalizedCandidatePath);
            }

            var managedRelativePath = BuildManagedRelativePath(request.Metadata);
            var managedAbsolutePath = Path.Combine(_artifactLibraryRoot, managedRelativePath);
            var managedDirectory = Path.GetDirectoryName(managedAbsolutePath);
            if (!string.IsNullOrWhiteSpace(managedDirectory))
            {
                Directory.CreateDirectory(managedDirectory);
            }

            if (!string.Equals(
                    Path.GetFullPath(normalizedCandidatePath),
                    Path.GetFullPath(managedAbsolutePath),
                    GetPathComparison()))
            {
                File.Copy(normalizedCandidatePath, managedAbsolutePath, overwrite: true);
            }

            ct.ThrowIfCancellationRequested();
            return new ResolvedArtifactStorage(
                managedRelativePath,
                IsManagedPath: true,
                SourcePath: normalizedCandidatePath,
                SizeBytes: new FileInfo(managedAbsolutePath).Length);
        }

        if (string.IsNullOrWhiteSpace(normalizedCandidatePath))
        {
            return new ResolvedArtifactStorage(
                BuildManagedRelativePath(request.Metadata),
                IsManagedPath: true,
                SourcePath: null,
                SizeBytes: null);
        }

        if (IsPathUnderRoot(normalizedCandidatePath, _artifactLibraryRoot))
        {
            return new ResolvedArtifactStorage(
                Path.GetRelativePath(_artifactLibraryRoot, normalizedCandidatePath),
                IsManagedPath: true,
                SourcePath: null,
                SizeBytes: TryGetFileSize(normalizedCandidatePath));
        }

        return new ResolvedArtifactStorage(
            normalizedCandidatePath,
            IsManagedPath: false,
            SourcePath: null,
            SizeBytes: TryGetFileSize(normalizedCandidatePath));
    }

    private string? ResolveArtifactPath(ProfilingArtifactLibraryEntry entry)
    {
        var storedPath = entry.Metadata.RelativePath;
        if (string.IsNullOrWhiteSpace(storedPath))
        {
            return null;
        }

        return entry.IsManagedPath
            ? Path.GetFullPath(Path.Combine(_artifactLibraryRoot, storedPath))
            : Path.GetFullPath(storedPath);
    }

    private void DeleteEmptyManagedDirectories(string deletedPath, bool isManagedPath)
    {
        if (!isManagedPath)
        {
            return;
        }

        var current = Path.GetDirectoryName(Path.GetFullPath(deletedPath));
        while (!string.IsNullOrWhiteSpace(current) &&
               IsPathUnderRoot(current, _artifactLibraryRoot) &&
               !string.Equals(current, _artifactLibraryRoot, GetPathComparison()))
        {
            if (Directory.EnumerateFileSystemEntries(current).Any())
            {
                return;
            }

            Directory.Delete(current);
            current = Path.GetDirectoryName(current);
        }
    }

    private static string BuildManagedRelativePath(ProfilingArtifactMetadata metadata)
    {
        var sessionSegment = SanitizePathSegment(metadata.SessionId);
        var idSegment = SanitizePathSegment(metadata.Id);
        var fileName = Path.GetFileName(string.IsNullOrWhiteSpace(metadata.FileName) ? $"{idSegment}.bin" : metadata.FileName);
        var safeFileName = SanitizeFileName(fileName);
        return Path.Combine(sessionSegment, $"{idSegment}-{safeFileName}");
    }

    private static string SanitizePathSegment(string value)
    {
        ValidateRequiredText(value, nameof(value));
        return new string(value.Trim().Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '-' : ch).ToArray());
    }

    private static string SanitizeFileName(string value)
    {
        var fileName = new string(value.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '-' : ch).ToArray());
        return string.IsNullOrWhiteSpace(fileName) ? "artifact.bin" : fileName;
    }

    private static long? TryGetFileSize(string path) => File.Exists(path) ? new FileInfo(path).Length : null;

    private static bool IsPathUnderRoot(string path, string rootPath)
    {
        var normalizedPath = EnsureTrailingSeparator(Path.GetFullPath(path));
        var normalizedRoot = EnsureTrailingSeparator(Path.GetFullPath(rootPath));
        return normalizedPath.StartsWith(normalizedRoot, GetPathComparison());
    }

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;

    private static StringComparison GetPathComparison() =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static void ValidateMetadata(ProfilingArtifactMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ValidateRequiredText(metadata.Id, nameof(metadata.Id));
        ValidateRequiredText(metadata.SessionId, nameof(metadata.SessionId));
        ValidateRequiredText(metadata.DisplayName, nameof(metadata.DisplayName));
        ValidateRequiredText(metadata.FileName, nameof(metadata.FileName));
        ValidateRequiredText(metadata.ContentType, nameof(metadata.ContentType));
    }

    private static void ValidateRequiredText(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", paramName);
        }
    }

    private sealed record ResolvedArtifactStorage(
        string StoredPath,
        bool IsManagedPath,
        string? SourcePath,
        long? SizeBytes);
}
