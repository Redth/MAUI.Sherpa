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
    private readonly ILocalVaultStore? _vaultStore;
    private readonly ILocalVaultAccessService? _localVaultAccess;
    private readonly ILocalVaultIntroductionService? _localVaultIntroduction;
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
    public const string DefaultLocalProviderId = "local";
    private const string ProviderMetadataVaultKey = "providers";
    private const string ProviderSettingsVaultPath = "/providers";
    private const string ProviderSettingsKeyPrefix = "settings-";

    public CloudSecretsService(
        ISecureStorageService secureStorage,
        IFileSystemService fileSystem,
        ILoggingService logger,
        ICloudSecretsProviderFactory providerFactory,
        ILocalVaultStore? vaultStore = null,
        ILocalVaultAccessService? localVaultAccess = null,
        ILocalVaultIntroductionService? localVaultIntroduction = null)
    {
        _secureStorage = secureStorage;
        _fileSystem = fileSystem;
        _logger = logger;
        _providerFactory = providerFactory;
        _vaultStore = vaultStore;
        _localVaultAccess = localVaultAccess;
        _localVaultIntroduction = localVaultIntroduction;
        _settingsPath = Path.Combine(
            AppDataPath.GetAppDataDirectory(),
            "cloud-secrets-providers.json");
    }

    public CloudSecretsProviderConfig? ActiveProvider { get; private set; }

    public event Action? OnActiveProviderChanged;

    #region Provider Management

    public async Task<IReadOnlyList<CloudSecretsProviderConfig>> GetProvidersAsync()
    {
        await LoadMetadataAsync();

        var metadata = _providerMetadata;
        if (_providerMetadata.All(p => p.Id != DefaultLocalProviderId))
        {
            if (CanUseLocalVault())
            {
                await EnsureDefaultLocalProviderMetadataAsync();
                metadata = _providerMetadata;
            }
            else
            {
                var withDefaultLocal = new List<CloudSecretsProviderMetadata>
                {
                    CreateDefaultLocalProviderMetadata()
                };
                withDefaultLocal.AddRange(_providerMetadata);
                metadata = withDefaultLocal;
            }
        }
        else if (CanUseLocalVault())
        {
            await EnsureDefaultLocalProviderMetadataAsync();
            metadata = _providerMetadata;
        }

        var result = new List<CloudSecretsProviderConfig>();

        foreach (var meta in metadata)
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
            await TrySetSecureStorageAsync(SecureKeyPrefix + provider.Id, secretJson);
        }
        else
        {
            await TryRemoveSecureStorageAsync(SecureKeyPrefix + provider.Id);
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
        var nonSecretSettings = provider.Settings
            .Where(kvp => !secretKeys.Contains(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        await PersistNonSecretSettingsAsync(provider.Id, nonSecretSettings);
        
        _logger.LogInformation($"Saved cloud secrets provider: {provider.Name}");
        
        // Update active provider if it's the one we just saved
        if (_activeProviderId == provider.Id)
        {
            ActiveProvider = provider;
            _activeProviderInstance = null; // Force re-creation
            OnActiveProviderChanged?.Invoke();
        }
    }

    public async Task EnableDefaultLocalProviderAsync(bool setActiveProvider = true)
    {
        await EnsureDefaultLocalProviderAsync();

        if (setActiveProvider)
            await SetActiveProviderAsync(DefaultLocalProviderId);
    }

    public async Task DeleteProviderAsync(string providerId)
    {
        if (providerId == DefaultLocalProviderId)
        {
            if (CanUseLocalVault())
                await EnsureDefaultLocalProviderAsync();
            _logger.LogInformation("The default Local secrets provider cannot be deleted.");
            return;
        }

        await LoadMetadataAsync();

        // Remove from secure storage
        await TryRemoveSecureStorageAsync(SecureKeyPrefix + providerId);
        
        // Remove non-secret settings file
        var nonSecretPath = GetNonSecretSettingsPath(providerId);
        if (await _fileSystem.FileExistsAsync(nonSecretPath))
            await _fileSystem.DeleteFileAsync(nonSecretPath);
        if (CanUseLocalVault())
            await _vaultStore.RemoveAsync(LocalVaultScopes.CloudProvider, ProviderSettingsVaultPath, GetProviderSettingsVaultKey(providerId));

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
            await TryRemoveSecureStorageAsync(ActiveProviderKey);
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
            await TrySetSecureStorageAsync(ActiveProviderKey, providerId);
        }
        
        OnActiveProviderChanged?.Invoke();
        _logger.LogInformation($"Active cloud secrets provider: {ActiveProvider?.Name ?? "None"}");
    }

    /// <summary>
    /// Initializes the service by loading the active provider
    /// </summary>
    public async Task InitializeAsync()
    {
        _activeProviderId = await TryGetSecureStorageAsync(ActiveProviderKey);
        if (!string.IsNullOrEmpty(_activeProviderId))
        {
            var providers = await GetProvidersAsync();
            ActiveProvider = providers.FirstOrDefault(p => p.Id == _activeProviderId);
            if (ActiveProvider == null)
            {
                // Provider was deleted, clear the active provider
                _activeProviderId = null;
                await TryRemoveSecureStorageAsync(ActiveProviderKey);
            }
        }

        if (ActiveProvider is null)
        {
            var providers = await GetProvidersAsync();
            var defaultProviderId = ChooseDefaultProviderId(providers);
            if (defaultProviderId is not null)
                await SetActiveProviderAsync(defaultProviderId);
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

    public async Task<Dictionary<string, string>?> GetSecretMetadataAsync(string key, CancellationToken cancellationToken = default)
    {
        var provider = await GetActiveProviderInstanceAsync();
        if (provider == null)
        {
            _logger.LogWarning("No active cloud secrets provider configured");
            return null;
        }

        return await provider.GetSecretMetadataAsync(key, cancellationToken);
    }

    public async Task<bool> SetSecretMetadataAsync(string key, Dictionary<string, string> metadata, CancellationToken cancellationToken = default)
    {
        var provider = await GetActiveProviderInstanceAsync();
        if (provider == null)
        {
            _logger.LogWarning("No active cloud secrets provider configured");
            return false;
        }

        return await provider.SetSecretMetadataAsync(key, metadata, cancellationToken);
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
            AppDataPath.GetAppDataDirectory(),
            $"cloud-secrets-{providerId}.json");

    private async Task<CloudSecretsProviderConfig?> LoadProviderConfigAsync(CloudSecretsProviderMetadata metadata)
    {
        try
        {
            var settings = new Dictionary<string, string>();
            
            var nonSecretSettings = await LoadNonSecretSettingsAsync(metadata.Id);
            foreach (var kvp in nonSecretSettings)
                settings[kvp.Key] = kvp.Value;
            
            // Load secret settings from secure storage
            var secretJson = await TryGetSecureStorageAsync(SecureKeyPrefix + metadata.Id);
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
        if (CanUseLocalVault())
        {
            try
            {
                var item = await _vaultStore.GetAsync(LocalVaultScopes.CloudProvider, "/", ProviderMetadataVaultKey);
                if (item is not null)
                {
                    var json = System.Text.Encoding.UTF8.GetString(item.Value);
                    _providerMetadata = JsonSerializer.Deserialize<List<CloudSecretsProviderMetadata>>(json) ?? new();
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to read cloud secrets metadata from local vault: {ex.Message}");
            }
        }

        try
        {
            if (await _fileSystem.FileExistsAsync(_settingsPath))
            {
                var json = await _fileSystem.ReadFileAsync(_settingsPath);
                if (!string.IsNullOrEmpty(json))
                {
                    _providerMetadata = JsonSerializer.Deserialize<List<CloudSecretsProviderMetadata>>(json) ?? new();
                    if (CanUseLocalVault() && await TryPersistMetadataToVaultAsync(json))
                        await _fileSystem.DeleteFileAsync(_settingsPath);
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

    private async Task EnsureDefaultLocalProviderAsync()
    {
        await LoadMetadataAsync();
        await EnsureDefaultLocalProviderMetadataAsync();
    }

    private async Task EnsureDefaultLocalProviderMetadataAsync()
    {
        if (_providerMetadata.Any(p => p.Id == DefaultLocalProviderId))
            return;

        _providerMetadata.Insert(0, CreateDefaultLocalProviderMetadata());
        await PersistMetadataAsync();
    }

    private static CloudSecretsProviderMetadata CreateDefaultLocalProviderMetadata() =>
        new(
            DefaultLocalProviderId,
            "Local",
            CloudSecretsProviderType.Local,
            new List<string>());

    private async Task PersistMetadataAsync()
    {
        var json = JsonSerializer.Serialize(_providerMetadata, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        if (CanUseLocalVault() && await TryPersistMetadataToVaultAsync(json))
        {
            if (await _fileSystem.FileExistsAsync(_settingsPath))
                await _fileSystem.DeleteFileAsync(_settingsPath);
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(directory))
                await _fileSystem.CreateDirectoryAsync(directory);

            await _fileSystem.WriteFileAsync(_settingsPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to persist cloud secrets metadata: {ex.Message}", ex);
        }
    }

    private async Task<bool> TryPersistMetadataToVaultAsync(string json)
    {
        if (_vaultStore is null)
            return false;

        try
        {
            await _vaultStore.PutAsync(
                LocalVaultScopes.CloudProvider,
                "/",
                ProviderMetadataVaultKey,
                System.Text.Encoding.UTF8.GetBytes(json),
                LocalVaultContentTypes.Json);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to persist cloud secrets metadata in local vault: {ex.Message}");
            return false;
        }
    }

    private async Task<Dictionary<string, string>> LoadNonSecretSettingsAsync(string providerId)
    {
        if (CanUseLocalVault())
        {
            try
            {
                var item = await _vaultStore.GetAsync(
                    LocalVaultScopes.CloudProvider,
                    ProviderSettingsVaultPath,
                    GetProviderSettingsVaultKey(providerId));

                if (item is not null)
                {
                    var json = System.Text.Encoding.UTF8.GetString(item.Value);
                    return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to read provider settings from local vault for {providerId}: {ex.Message}");
            }
        }

        var nonSecretPath = GetNonSecretSettingsPath(providerId);
        if (!await _fileSystem.FileExistsAsync(nonSecretPath))
            return new();

        var legacyJson = await _fileSystem.ReadFileAsync(nonSecretPath);
        if (string.IsNullOrEmpty(legacyJson))
            return new();

        var legacySettings = JsonSerializer.Deserialize<Dictionary<string, string>>(legacyJson) ?? new();
        if (CanUseLocalVault())
        {
            var legacySettingsJson = JsonSerializer.Serialize(legacySettings);
            if (await TryPersistNonSecretSettingsToVaultAsync(providerId, legacySettingsJson))
                await _fileSystem.DeleteFileAsync(nonSecretPath);
        }

        return legacySettings;
    }

    private async Task PersistNonSecretSettingsAsync(string providerId, Dictionary<string, string> settings)
    {
        var json = JsonSerializer.Serialize(settings);

        if (CanUseLocalVault() && await TryPersistNonSecretSettingsToVaultAsync(providerId, json))
        {
            var legacyPath = GetNonSecretSettingsPath(providerId);
            if (await _fileSystem.FileExistsAsync(legacyPath))
                await _fileSystem.DeleteFileAsync(legacyPath);
            return;
        }

        await _fileSystem.WriteFileAsync(GetNonSecretSettingsPath(providerId), json);
    }

    private static string GetProviderSettingsVaultKey(string providerId) => ProviderSettingsKeyPrefix + providerId;

    private async Task<bool> TryPersistNonSecretSettingsToVaultAsync(string providerId, string json)
    {
        if (_vaultStore is null)
            return false;

        try
        {
            await _vaultStore.PutAsync(
                LocalVaultScopes.CloudProvider,
                ProviderSettingsVaultPath,
                GetProviderSettingsVaultKey(providerId),
                System.Text.Encoding.UTF8.GetBytes(json),
                LocalVaultContentTypes.Json,
                new Dictionary<string, string>
                {
                    ["ProviderId"] = providerId
                });
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to persist provider settings in local vault for {providerId}: {ex.Message}");
            return false;
        }
    }

    private string? ChooseDefaultProviderId(IReadOnlyList<CloudSecretsProviderConfig> providers)
    {
        var localUnavailable = _localVaultAccess?.GetState().RequiresUserAction == true;
        if (localUnavailable)
        {
            var nonLocal = providers.FirstOrDefault(p => p.ProviderType != CloudSecretsProviderType.Local);
            if (nonLocal is not null)
                return nonLocal.Id;
        }

        if (ShouldPreferDefaultLocalProvider())
        {
            return providers.FirstOrDefault(p => p.Id == DefaultLocalProviderId)?.Id
                ?? providers.FirstOrDefault()?.Id;
        }

        return providers.FirstOrDefault(p => p.ProviderType != CloudSecretsProviderType.Local)?.Id
            ?? providers.FirstOrDefault(p => p.Id == DefaultLocalProviderId)?.Id
            ?? providers.FirstOrDefault()?.Id;
    }

    private bool ShouldPreferDefaultLocalProvider() =>
        _localVaultIntroduction?.GetState().HasDeclined != true;

    private bool IsLocalVaultStorageEnabled() =>
        _localVaultIntroduction?.GetState().IsLocalVaultEnabled != false;

    private bool CanUseLocalVault() =>
        _vaultStore is not null && IsLocalVaultStorageEnabled();

    private async Task<string?> TryGetSecureStorageAsync(string key)
    {
        try
        {
            return await _secureStorage.GetAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to read secure storage key {key}: {ex.Message}");
            return null;
        }
    }

    private async Task TrySetSecureStorageAsync(string key, string value)
    {
        try
        {
            await _secureStorage.SetAsync(key, value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to persist secure storage key {key}: {ex.Message}");
        }
    }

    private async Task TryRemoveSecureStorageAsync(string key)
    {
        try
        {
            await _secureStorage.RemoveAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to remove secure storage key {key}: {ex.Message}");
        }
    }

    #endregion
}
