using System.Text.Json;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public class PushProjectService : IPushProjectService
{
    private readonly IFileSystemService _fileSystem;
    private readonly IEncryptedSettingsService _settingsService;
    private readonly ILoggingService _logger;
    private readonly string _storagePath;
    private List<PushProject>? _projects;
    private const int MaxHistoryEntries = 50;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public event Action? OnProjectsChanged;

    public PushProjectService(
        IFileSystemService fileSystem,
        IEncryptedSettingsService settingsService,
        ILoggingService logger)
    {
        _fileSystem = fileSystem;
        _settingsService = settingsService;
        _logger = logger;
        _storagePath = Path.Combine(
            AppDataPath.GetAppDataDirectory(),
            "push-projects.json");
    }

    public async Task<IReadOnlyList<PushProject>> GetProjectsAsync(PushProjectPlatform? platform = null)
    {
        await LoadProjectsAsync();
        var results = platform.HasValue
            ? _projects!.Where(p => p.Platform == platform.Value).ToList()
            : _projects!.ToList();
        return results.OrderByDescending(p => p.LastModified).ToList().AsReadOnly();
    }

    public async Task<PushProject?> GetProjectAsync(string id)
    {
        await LoadProjectsAsync();
        return _projects!.FirstOrDefault(p => p.Id == id);
    }

    public async Task SaveProjectAsync(PushProject project)
    {
        await LoadProjectsAsync();
        var index = _projects!.FindIndex(p => p.Id == project.Id);
        var updated = project with { LastModified = DateTime.UtcNow };

        if (index >= 0)
            _projects[index] = updated;
        else
            _projects.Add(updated);

        await PersistProjectsAsync();
        OnProjectsChanged?.Invoke();
    }

    public async Task DeleteProjectAsync(string id)
    {
        await LoadProjectsAsync();
        _projects!.RemoveAll(p => p.Id == id);
        await PersistProjectsAsync();
        OnProjectsChanged?.Invoke();
    }

    public async Task<PushProject> DuplicateProjectAsync(string id)
    {
        await LoadProjectsAsync();
        var source = _projects!.FirstOrDefault(p => p.Id == id)
            ?? throw new InvalidOperationException($"Project {id} not found");

        var copy = source with
        {
            Id = Guid.NewGuid().ToString(),
            Name = source.Name + " (Copy)",
            History = new List<PushSendHistoryEntry>(),
            CreatedAt = DateTime.UtcNow,
            LastModified = DateTime.UtcNow
        };

        _projects.Add(copy);
        await PersistProjectsAsync();
        OnProjectsChanged?.Invoke();
        return copy;
    }

    public async Task AddHistoryEntryAsync(string projectId, PushSendHistoryEntry entry)
    {
        await LoadProjectsAsync();
        var project = _projects!.FirstOrDefault(p => p.Id == projectId);
        if (project == null) return;

        var history = new List<PushSendHistoryEntry> { entry };
        history.AddRange(project.History);
        if (history.Count > MaxHistoryEntries)
            history = history.Take(MaxHistoryEntries).ToList();

        var index = _projects.FindIndex(p => p.Id == projectId);
        _projects[index] = project with { History = history, LastModified = DateTime.UtcNow };
        await PersistProjectsAsync();
        OnProjectsChanged?.Invoke();
    }

    public async Task ClearHistoryAsync(string projectId)
    {
        await LoadProjectsAsync();
        var project = _projects!.FirstOrDefault(p => p.Id == projectId);
        if (project == null) return;

        var index = _projects.FindIndex(p => p.Id == projectId);
        _projects[index] = project with { History = new List<PushSendHistoryEntry>(), LastModified = DateTime.UtcNow };
        await PersistProjectsAsync();
        OnProjectsChanged?.Invoke();
    }

    public async Task<PushProject?> MigrateFromLegacyAsync()
    {
        try
        {
            await LoadProjectsAsync();

            // Only migrate if no APNs projects exist yet
            if (_projects!.Any(p => p.Platform == PushProjectPlatform.Apns))
                return null;

            var settings = await _settingsService.GetSettingsAsync();
            var legacy = settings.PushTesting;

            // Check if legacy settings have non-default values
            var defaultSettings = new PushTestingSettings();
            if (legacy.AuthMode == defaultSettings.AuthMode &&
                legacy.SelectedIdentityId == defaultSettings.SelectedIdentityId &&
                legacy.P8FilePath == defaultSettings.P8FilePath &&
                legacy.TeamId == defaultSettings.TeamId &&
                legacy.BundleId == defaultSettings.BundleId &&
                legacy.DeviceToken == defaultSettings.DeviceToken &&
                legacy.JsonPayload == defaultSettings.JsonPayload)
            {
                return null;
            }

            var project = new PushProject
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Migrated APNs Config",
                Platform = PushProjectPlatform.Apns,
                ApnsConfig = new ApnsPushProjectConfig
                {
                    AuthMode = legacy.AuthMode,
                    SelectedIdentityId = legacy.SelectedIdentityId,
                    P8FilePath = legacy.P8FilePath,
                    P8KeyId = legacy.P8KeyId,
                    TeamId = legacy.TeamId,
                    PushType = legacy.PushType,
                    Priority = legacy.Priority,
                    CollapseId = legacy.CollapseId,
                    NotificationId = legacy.NotificationId,
                    ExpirationSeconds = legacy.ExpirationSeconds,
                    BundleId = legacy.BundleId,
                    DeviceToken = legacy.DeviceToken,
                    JsonPayload = legacy.JsonPayload,
                    UseSandbox = legacy.UseSandbox
                },
                CreatedAt = DateTime.UtcNow,
                LastModified = DateTime.UtcNow
            };

            _projects.Add(project);
            await PersistProjectsAsync();

            // Clear legacy settings
            await _settingsService.UpdateSettingsAsync(s => s with { PushTesting = new PushTestingSettings() });

            _logger.LogInformation("Migrated legacy APNs push settings to push project");
            OnProjectsChanged?.Invoke();
            return project;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to migrate legacy push settings: {ex.Message}", ex);
            return null;
        }
    }

    private async Task LoadProjectsAsync()
    {
        if (_projects != null) return;

        try
        {
            if (await _fileSystem.FileExistsAsync(_storagePath))
            {
                var json = await _fileSystem.ReadFileAsync(_storagePath);
                if (!string.IsNullOrEmpty(json))
                {
                    _projects = JsonSerializer.Deserialize<List<PushProject>>(json, JsonOptions) ?? new();
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to load push projects: {ex.Message}", ex);
        }

        _projects = new List<PushProject>();
    }

    private async Task PersistProjectsAsync()
    {
        try
        {
            var dir = Path.GetDirectoryName(_storagePath)!;
            if (!await _fileSystem.DirectoryExistsAsync(dir))
                await _fileSystem.CreateDirectoryAsync(dir);

            var json = JsonSerializer.Serialize(_projects, JsonOptions);
            await _fileSystem.WriteFileAsync(_storagePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to persist push projects: {ex.Message}", ex);
        }
    }
}
