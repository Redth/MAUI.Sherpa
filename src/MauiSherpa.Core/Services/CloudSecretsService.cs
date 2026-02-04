using System.Text.Json;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Service for managing cloud secrets storage providers and operations.
/// Stores provider configurations in secure storage, with metadata in JSON.
/// </summary>
public class CloudSecretsService : ICloudSecretsService
{
    private readonly ISecureStorageService _secureStorage;
    private readonly IFileSystemService _fileSystem;
    private readonly ILoggingService _logger;
    private readonly ICloudSecretsProviderFactory _providerFactory;
    private readonly string _settingsPath;
    
    private List<CloudSecretsProviderMetadata> _providerMetadata = new();
    private string? _activeProviderId;
    private ICloudSecretsProvider? _activeProviderInstance;
    
    // Internal record for storing non-sensitive data in JSON
    private record CloudSecretsProviderMetadata(
        string Id,
        string Name,
        CloudSecretsProviderType ProviderType,
        // Only store non-secret setting keys here; secrets go in secure storage
        List<string> NonSecretSettingKeys
    );

    private const string SecureKeyPrefix = "cloud_secrets_provider_";
    private const string ActiveProviderKey = "cloud_secrets_active_provider";

    public CloudSecretsService(
        ISecureStorageService secureStorage,
        IFileSystemService fileSystem,
        ILoggingService logger,
        ICloudSecretsProviderFactory providerFactory)
    {
        _secureStorage = secureStorage;
        _fileSystem = fileSystem;
        _logger = logger;
        _providerFactory = providerFactory;
        _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MauiSherpa",
            "cloud-secrets-providers.json");
    }

    public CloudSecretsProviderConfig? ActiveProvider { get; private set; }

    public event Action? OnActiveProviderChanged;

    #region Provider Management

    public async Task<IReadOnlyList<CloudSecretsProviderConfig>> GetProvidersAsync()
    {
        await LoadMetadataAsync();
        var result = new List<CloudSecretsProviderConfig>();

        foreach (var meta in _providerMetadata)
        {
            var config = await LoadProviderConfigAsync(meta);
            if (config != null)
                result.Add(config);
        }

        return result.AsReadOnly();
    }

    public async Task SaveProviderAsync(CloudSecretsProviderConfig provider)
    {
        await LoadMetadataAsync();
        
        var providerSettings = _providerFactory.GetProviderSettings(provider.ProviderType);
        var secretKeys = providerSettings.Where(s => s.IsSecret).Select(s => s.Key).ToHashSet();
        var nonSecretKeys = provider.Settings.Keys.Where(k => !secretKeys.Contains(k)).ToList();
        
        // Store secret settings in secure storage
        var secretSettings = provider.Settings.Where(kvp => secretKeys.Contains(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        if (secretSettings.Count > 0)
        {
            var secretJson = JsonSerializer.Serialize(secretSettings);
            await _secureStorage.SetAsync(SecureKeyPrefix + provider.Id, secretJson);
        }
        
        // Store non-secret settings in metadata
        var metadata = new CloudSecretsProviderMetadata(
            provider.Id,
            provider.Name,
            provider.ProviderType,
            nonSecretKeys
        );

        var existing = _providerMetadata.FindIndex(m => m.Id == provider.Id);
        if (existing >= 0)
            _providerMetadata[existing] = metadata;
        else
            _providerMetadata.Add(metadata);

        await PersistMetadataAsync();
        
        // Store non-secret settings separately (since they're not in the metadata)
        var nonSecretSettingsJson = JsonSerializer.Serialize(
            provider.Settings.Where(kvp => !secretKeys.Contains(kvp.Key))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
        await _fileSystem.WriteFileAsync(GetNonSecretSettingsPath(provider.Id), nonSecretSettingsJson);
        
        _logger.LogInformation($"Saved cloud secrets provider: {provider.Name}");
        
        // Update active provider if it's the one we just saved
        if (_activeProviderId == provider.Id)
        {
            ActiveProvider = provider;
            _activeProviderInstance = null; // Force re-creation
            OnActiveProviderChanged?.Invoke();
        }
    }

    public async Task DeleteProviderAsync(string providerId)
    {
        await LoadMetadataAsync();

        // Remove from secure storage
        await _secureStorage.RemoveAsync(SecureKeyPrefix + providerId);
        
        // Remove non-secret settings file
        var nonSecretPath = GetNonSecretSettingsPath(providerId);
        if (await _fileSystem.FileExistsAsync(nonSecretPath))
            await _fileSystem.DeleteFileAsync(nonSecretPath);

        var removed = _providerMetadata.RemoveAll(m => m.Id == providerId);
        if (removed > 0)
        {
            await PersistMetadataAsync();
            _logger.LogInformation($"Deleted cloud secrets provider: {providerId}");
            
            // Clear active provider if deleted
            if (_activeProviderId == providerId)
            {
                await SetActiveProviderAsync(null);
            }
        }
    }

    public async Task<bool> TestProviderConnectionAsync(string providerId)
    {
        try
        {
            var providers = await GetProvidersAsync();
            var config = providers.FirstOrDefault(p => p.Id == providerId);
            if (config == null)
            {
                _logger.LogError($"Provider not found: {providerId}");
                return false;
            }

            var provider = _providerFactory.CreateProvider(config);
            var result = await provider.TestConnectionAsync();
            
            _logger.LogInformation($"Connection test for {config.Name}: {(result ? "SUCCESS" : "FAILED")}");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Connection test failed: {ex.Message}", ex);
            return false;
        }
    }

    #endregion

    #region Active Provider

    public async Task SetActiveProviderAsync(string? providerId)
    {
        if (providerId == null)
        {
            _activeProviderId = null;
            ActiveProvider = null;
            _activeProviderInstance = null;
            await _secureStorage.RemoveAsync(ActiveProviderKey);
        }
        else
        {
            var providers = await GetProvidersAsync();
            var config = providers.FirstOrDefault(p => p.Id == providerId);
            if (config == null)
            {
                _logger.LogError($"Cannot set active provider: {providerId} not found");
                return;
            }

            _activeProviderId = providerId;
            ActiveProvider = config;
            _activeProviderInstance = null; // Force re-creation on next use
            await _secureStorage.SetAsync(ActiveProviderKey, providerId);
        }
        
        OnActiveProviderChanged?.Invoke();
        _logger.LogInformation($"Active cloud secrets provider: {ActiveProvider?.Name ?? "None"}");
    }

    /// <summary>
    /// Initializes the service by loading the active provider
    /// </summary>
    public async Task InitializeAsync()
    {
        _activeProviderId = await _secureStorage.GetAsync(ActiveProviderKey);
        if (!string.IsNullOrEmpty(_activeProviderId))
        {
            var providers = await GetProvidersAsync();
            ActiveProvider = providers.FirstOrDefault(p => p.Id == _activeProviderId);
            if (ActiveProvider == null)
            {
                // Provider was deleted, clear the active provider
                _activeProviderId = null;
                await _secureStorage.RemoveAsync(ActiveProviderKey);
            }
        }
    }

    #endregion

    #region Secret Operations

    public async Task<bool> StoreSecretAsync(string key, byte[] value, Dictionary<string, string>? metadata = null, CancellationToken cancellationToken = default)
    {
        var provider = await GetActiveProviderInstanceAsync();
        if (provider == null)
        {
            _logger.LogWarning("No active cloud secrets provider configured");
            return false;
        }
        
        return await provider.StoreSecretAsync(key, value, metadata, cancellationToken);
    }

    public async Task<byte[]?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        var provider = await GetActiveProviderInstanceAsync();
        if (provider == null)
        {
            _logger.LogWarning("No active cloud secrets provider configured");
            return null;
        }
        
        return await provider.GetSecretAsync(key, cancellationToken);
    }

    public async Task<bool> DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        var provider = await GetActiveProviderInstanceAsync();
        if (provider == null)
        {
            _logger.LogWarning("No active cloud secrets provider configured");
            return false;
        }
        
        return await provider.DeleteSecretAsync(key, cancellationToken);
    }

    public async Task<bool> SecretExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        var provider = await GetActiveProviderInstanceAsync();
        if (provider == null)
        {
            _logger.LogWarning("No active cloud secrets provider configured");
            return false;
        }
        
        return await provider.SecretExistsAsync(key, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListSecretsAsync(string? prefix = null, CancellationToken cancellationToken = default)
    {
        var provider = await GetActiveProviderInstanceAsync();
        if (provider == null)
        {
            _logger.LogWarning("No active cloud secrets provider configured");
            return Array.Empty<string>();
        }
        
        return await provider.ListSecretsAsync(prefix, cancellationToken);
    }

    #endregion

    #region Private Helpers

    private async Task<ICloudSecretsProvider?> GetActiveProviderInstanceAsync()
    {
        if (_activeProviderInstance != null)
            return _activeProviderInstance;
        
        if (ActiveProvider == null)
            return null;
        
        _activeProviderInstance = _providerFactory.CreateProvider(ActiveProvider);
        return _activeProviderInstance;
    }

    private string GetNonSecretSettingsPath(string providerId) =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MauiSherpa",
            $"cloud-secrets-{providerId}.json");

    private async Task<CloudSecretsProviderConfig?> LoadProviderConfigAsync(CloudSecretsProviderMetadata metadata)
    {
        try
        {
            var settings = new Dictionary<string, string>();
            
            // Load non-secret settings
            var nonSecretPath = GetNonSecretSettingsPath(metadata.Id);
            if (await _fileSystem.FileExistsAsync(nonSecretPath))
            {
                var json = await _fileSystem.ReadFileAsync(nonSecretPath);
                if (!string.IsNullOrEmpty(json))
                {
                    var nonSecretSettings = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (nonSecretSettings != null)
                    {
                        foreach (var kvp in nonSecretSettings)
                            settings[kvp.Key] = kvp.Value;
                    }
                }
            }
            
            // Load secret settings from secure storage
            var secretJson = await _secureStorage.GetAsync(SecureKeyPrefix + metadata.Id);
            if (!string.IsNullOrEmpty(secretJson))
            {
                var secretSettings = JsonSerializer.Deserialize<Dictionary<string, string>>(secretJson);
                if (secretSettings != null)
                {
                    foreach (var kvp in secretSettings)
                        settings[kvp.Key] = kvp.Value;
                }
            }

            return new CloudSecretsProviderConfig(
                metadata.Id,
                metadata.Name,
                metadata.ProviderType,
                settings
            );
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to load provider config {metadata.Id}: {ex.Message}", ex);
            return null;
        }
    }

    private async Task LoadMetadataAsync()
    {
        try
        {
            if (await _fileSystem.FileExistsAsync(_settingsPath))
            {
                var json = await _fileSystem.ReadFileAsync(_settingsPath);
                if (!string.IsNullOrEmpty(json))
                {
                    _providerMetadata = JsonSerializer.Deserialize<List<CloudSecretsProviderMetadata>>(json) ?? new();
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to load cloud secrets metadata: {ex.Message}");
        }
        _providerMetadata = new();
    }

    private async Task PersistMetadataAsync()
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(directory))
                await _fileSystem.CreateDirectoryAsync(directory);

            var json = JsonSerializer.Serialize(_providerMetadata, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            await _fileSystem.WriteFileAsync(_settingsPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to persist cloud secrets metadata: {ex.Message}", ex);
        }
    }

    #endregion
}
