using System.Text;
using System.Text.Json;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public class PublishProfileService : IPublishProfileService
{
    const string CloudKeyPrefix = "sherpa-publish-profiles/";

    readonly ICloudSecretsService _cloudService;
    readonly ICertificateSyncService _certSync;
    readonly IKeystoreService _keystoreService;
    readonly ISecureStorageService _secureStorage;
    readonly IManagedSecretsService _managedSecrets;
    readonly IAppleConnectService _appleConnect;
    readonly IAppleIdentityService _appleIdentity;
    readonly IAppleIdentityStateService _identityState;
    readonly IGoogleIdentityService _googleIdentity;
    readonly ILoggingService _logger;
    readonly ISecretsProviderRegistry? _providerRegistry;
    readonly ISecretSyncCoordinator? _syncCoordinator;

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    List<PublishProfile>? _cache;
    readonly object _cacheLock = new();
    readonly SemaphoreSlim _cacheLoadGate = new(1, 1);
    long _cacheGeneration;

    public event Action? OnProfilesChanged;

    public PublishProfileService(
        ICloudSecretsService cloudService,
        ICertificateSyncService certSync,
        IKeystoreService keystoreService,
        ISecureStorageService secureStorage,
        IManagedSecretsService managedSecrets,
        IAppleConnectService appleConnect,
        IAppleIdentityService appleIdentity,
        IAppleIdentityStateService identityState,
        IGoogleIdentityService googleIdentity,
        ILoggingService logger,
        ISecretsProviderRegistry? providerRegistry = null,
        ISecretSyncCoordinator? syncCoordinator = null)
    {
        _cloudService = cloudService;
        _certSync = certSync;
        _keystoreService = keystoreService;
        _secureStorage = secureStorage;
        _managedSecrets = managedSecrets;
        _appleConnect = appleConnect;
        _appleIdentity = appleIdentity;
        _identityState = identityState;
        _googleIdentity = googleIdentity;
        _logger = logger;
        _providerRegistry = providerRegistry;
        _syncCoordinator = syncCoordinator;
        if (_syncCoordinator is not null)
            _syncCoordinator.ItemStateChanged += OnSyncItemStateChanged;
    }

    public async Task<IReadOnlyList<PublishProfile>> GetProfilesAsync()
    {
        lock (_cacheLock)
        {
            if (_cache is not null)
                return _cache;
        }

        await _cacheLoadGate.WaitAsync();
        try
        {
            lock (_cacheLock)
            {
                if (_cache is not null)
                    return _cache;
            }

            var generation = Interlocked.Read(ref _cacheGeneration);
            var profiles = new Dictionary<string, PublishProfile>(StringComparer.Ordinal);
            if (_providerRegistry is not null)
            {
                var providerConfigs = await _providerRegistry.GetProvidersAsync();
                foreach (var config in providerConfigs
                    .OrderBy(provider => provider.ProviderType == CloudSecretsProviderType.Local ? 0 : 1)
                    .ThenBy(provider => provider.Name, StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        var provider = await _providerRegistry.GetProviderAsync(config.Id);
                        if (provider is null)
                            continue;

                        var keys = await provider.ListSecretsAsync(CloudKeyPrefix);
                        foreach (var key in keys)
                        {
                            var bytes = await provider.GetSecretAsync(key);
                            if (bytes is null)
                                continue;

                            var profile = JsonSerializer.Deserialize<PublishProfile>(
                                Encoding.UTF8.GetString(bytes),
                                JsonOptions);
                            if (profile is not null)
                                profiles.TryAdd(profile.Id, profile);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Failed to load publish profiles from '{config.Name}': {ex.Message}");
                    }
                }
            }
            else if (_cloudService.ActiveProvider is not null)
            {
                var keys = await _cloudService.ListSecretsAsync(CloudKeyPrefix);
                foreach (var key in keys)
                {
                    try
                    {
                        var bytes = await _cloudService.GetSecretAsync(key);
                        if (bytes is null)
                            continue;

                        var profile = JsonSerializer.Deserialize<PublishProfile>(
                            Encoding.UTF8.GetString(bytes),
                            JsonOptions);
                        if (profile is not null)
                            profiles.TryAdd(profile.Id, profile);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Failed to load publish profile '{key}': {ex.Message}");
                    }
                }
            }

            var loaded = profiles.Values.OrderBy(p => p.Name).ToList();
            lock (_cacheLock)
            {
                if (generation == Interlocked.Read(ref _cacheGeneration))
                    _cache = loaded;
                return _cache is not null
                    ? _cache
                    : Array.Empty<PublishProfile>();
            }
        }
        finally
        {
            _cacheLoadGate.Release();
        }
    }

    public async Task<PublishProfile?> GetProfileAsync(string id)
    {
        var profiles = await GetProfilesAsync();
        return profiles.FirstOrDefault(p => p.Id == id);
    }

    public async Task SaveProfileAsync(PublishProfile profile)
    {
        if (_cloudService.ActiveProvider is null)
            throw new InvalidOperationException("No cloud provider configured");

        var key = CloudKeyPrefix + profile.Id;
        var json = JsonSerializer.Serialize(profile, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);

        await _cloudService.StoreSecretAsync(key, bytes);

        // Update cache directly instead of invalidating — cloud list may lag
        Interlocked.Increment(ref _cacheGeneration);
        lock (_cacheLock)
        {
            _cache ??= [];
            var existing = _cache.FindIndex(p => p.Id == profile.Id);
            if (existing >= 0)
                _cache[existing] = profile;
            else
                _cache.Add(profile);
            _cache = _cache.OrderBy(p => p.Name).ToList();
        }

        OnProfilesChanged?.Invoke();
    }

    public async Task DeleteProfileAsync(string id)
    {
        if (_cloudService.ActiveProvider is null)
            throw new InvalidOperationException("No cloud provider configured");

        var key = CloudKeyPrefix + id;
        await _cloudService.DeleteSecretAsync(key);

        // Update cache directly instead of invalidating
        Interlocked.Increment(ref _cacheGeneration);
        lock (_cacheLock)
            _cache?.RemoveAll(p => p.Id == id);
        OnProfilesChanged?.Invoke();
    }

    void OnSyncItemStateChanged(SecretItemRef item)
    {
        if (item.Kind != SecretItemKind.PublishProfile)
            return;

        Interlocked.Increment(ref _cacheGeneration);
        lock (_cacheLock)
            _cache = null;
        OnProfilesChanged?.Invoke();
    }

    public async Task<Dictionary<string, string>> ResolveSecretsAsync(
        PublishProfile profile,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var secrets = new Dictionary<string, string>();

        // Resolve Apple configs
        foreach (var apple in profile.AppleConfigs)
        {
            ct.ThrowIfCancellationRequested();

            // Ensure the correct Apple identity is selected for API calls
            if (!string.IsNullOrEmpty(apple.IdentityId))
            {
                var identity = await _appleIdentity.GetIdentityAsync(apple.IdentityId);
                if (identity is not null)
                    _identityState.SetSelectedIdentity(identity);
            }

            // Build prefix using platform/distribution enums (must match AppleConfigVM.GetDefaultKeys)
            var prefix = GetAppleKeyPrefix(apple);

            // Certificate P12
            if (!string.IsNullOrEmpty(apple.CertificateSerialNumber))
            {
                progress?.Report($"Fetching certificate for {apple.Label}...");
                try
                {
                    var (p12Bytes, certPassword) = await _certSync.GetCertificateSecretsAsync(apple.CertificateSerialNumber, autoUploadFromKeychain: true, ct);
                    if (p12Bytes is not null)
                    {
                        var defaultKey = $"{prefix}_CERTIFICATE_P12";
                        AddMappedSecrets(secrets, apple.KeyMappings, defaultKey, Convert.ToBase64String(p12Bytes));
                    }

                    if (!string.IsNullOrEmpty(certPassword))
                    {
                        var defaultKey = $"{prefix}_CERTIFICATE_PASSWORD";
                        AddMappedSecrets(secrets, apple.KeyMappings, defaultKey, certPassword);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to resolve certificate for {apple.Label}: {ex.Message}");
                }
            }

            // Installer Certificate
            if (!string.IsNullOrEmpty(apple.InstallerCertSerialNumber))
            {
                progress?.Report($"Fetching installer certificate for {apple.Label}...");
                try
                {
                    var (p12Bytes, installerPassword) = await _certSync.GetCertificateSecretsAsync(apple.InstallerCertSerialNumber, autoUploadFromKeychain: true, ct);
                    if (p12Bytes is not null)
                    {
                        var defaultKey = $"{prefix}_INSTALLER_P12";
                        AddMappedSecrets(secrets, apple.KeyMappings, defaultKey, Convert.ToBase64String(p12Bytes));
                    }

                    if (!string.IsNullOrEmpty(installerPassword))
                    {
                        var defaultKey = $"{prefix}_INSTALLER_PASSWORD";
                        AddMappedSecrets(secrets, apple.KeyMappings, defaultKey, installerPassword);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to resolve installer cert for {apple.Label}: {ex.Message}");
                }
            }

            // Provisioning Profile
            if (!string.IsNullOrEmpty(apple.ProfileId))
            {
                progress?.Report($"Fetching provisioning profile for {apple.Label}...");
                try
                {
                    var profileBytes = await _appleConnect.DownloadProfileAsync(apple.ProfileId);
                    var defaultKey = $"{prefix}_PROFILE";
                    AddMappedSecrets(secrets, apple.KeyMappings, defaultKey, Convert.ToBase64String(profileBytes));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to resolve profile for {apple.Label}: {ex.Message}");
                }
            }

            // Notarization credentials — resolved from managed secret references
            if (apple.IncludeNotarization)
            {
                progress?.Report("Fetching notarization credentials...");
                try
                {
                    // Apple ID: manual value or managed secret
                    if (!string.IsNullOrEmpty(apple.NotarizationAppleIdManualValue))
                    {
                        AddMappedSecrets(secrets, apple.KeyMappings, "APPLE_NOTARIZATION_APPLE_ID", apple.NotarizationAppleIdManualValue);
                    }
                    else if (!string.IsNullOrEmpty(apple.NotarizationAppleIdSecretKey))
                    {
                        var val = await _managedSecrets.GetValueAsync(apple.NotarizationAppleIdSecretKey, ct);
                        if (val is not null)
                            AddMappedSecrets(secrets, apple.KeyMappings, "APPLE_NOTARIZATION_APPLE_ID", System.Text.Encoding.UTF8.GetString(val));
                    }
                    // Password: manual value or managed secret
                    if (!string.IsNullOrEmpty(apple.NotarizationPasswordManualValue))
                    {
                        AddMappedSecrets(secrets, apple.KeyMappings, "APPLE_NOTARIZATION_PASSWORD", apple.NotarizationPasswordManualValue);
                    }
                    else if (!string.IsNullOrEmpty(apple.NotarizationPasswordSecretKey))
                    {
                        var val = await _managedSecrets.GetValueAsync(apple.NotarizationPasswordSecretKey, ct);
                        if (val is not null)
                            AddMappedSecrets(secrets, apple.KeyMappings, "APPLE_NOTARIZATION_PASSWORD", System.Text.Encoding.UTF8.GetString(val));
                    }
                    // Team ID: manual value or managed secret
                    if (!string.IsNullOrEmpty(apple.NotarizationTeamIdManualValue))
                    {
                        AddMappedSecrets(secrets, apple.KeyMappings, "APPLE_NOTARIZATION_TEAM_ID", apple.NotarizationTeamIdManualValue);
                    }
                    else if (!string.IsNullOrEmpty(apple.NotarizationTeamIdSecretKey))
                    {
                        var val = await _managedSecrets.GetValueAsync(apple.NotarizationTeamIdSecretKey, ct);
                        if (val is not null)
                            AddMappedSecrets(secrets, apple.KeyMappings, "APPLE_NOTARIZATION_TEAM_ID", System.Text.Encoding.UTF8.GetString(val));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to resolve notarization creds: {ex.Message}");
                }
            }
        }

        // Resolve Apple developer identities
        foreach (var appleIdentity in profile.AppleIdentities ?? Enumerable.Empty<PublishProfileAppleIdentity>())
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(appleIdentity.IdentityId))
                continue;

            progress?.Report($"Fetching Apple identity {appleIdentity.Label}...");
            try
            {
                var identity = await _appleIdentity.GetIdentityAsync(appleIdentity.IdentityId);
                if (identity is null)
                {
                    _logger.LogWarning($"Apple identity '{appleIdentity.IdentityId}' was not found for publish profile item '{appleIdentity.Label}'");
                    continue;
                }

                var defaultKeys = GetAppleIdentityDefaultKeys(appleIdentity);
                if (!string.IsNullOrEmpty(identity.KeyId))
                    AddMappedSecrets(secrets, appleIdentity.KeyMappings, defaultKeys.KeyId, identity.KeyId);
                if (!string.IsNullOrEmpty(identity.IssuerId))
                    AddMappedSecrets(secrets, appleIdentity.KeyMappings, defaultKeys.IssuerId, identity.IssuerId);
                if (!string.IsNullOrEmpty(identity.P8KeyContent))
                    AddMappedSecrets(secrets, appleIdentity.KeyMappings, defaultKeys.P8Key, identity.P8KeyContent);
                else
                    _logger.LogWarning($"Apple identity '{identity.Name}' has no private key content to publish");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to resolve Apple identity for {appleIdentity.Label}: {ex.Message}");
            }
        }

        // Resolve Android configs
        foreach (var android in profile.AndroidConfigs)
        {
            ct.ThrowIfCancellationRequested();

            if (!string.IsNullOrEmpty(android.KeystoreId))
            {
                progress?.Report($"Fetching keystore for {android.Label}...");
                try
                {
                    var keystores = await _keystoreService.ListKeystoresAsync();
                    var keystore = keystores.FirstOrDefault(k => k.Id == android.KeystoreId);
                    if (keystore is not null)
                    {
                        // Read keystore file bytes
                        if (!string.IsNullOrEmpty(keystore.FilePath) && File.Exists(keystore.FilePath))
                        {
                            var keystoreBytes = await File.ReadAllBytesAsync(keystore.FilePath, ct);
                            var defaultKey = $"ANDROID_{SanitizeLabel(android.Label)}_KEYSTORE";
                            AddMappedSecrets(secrets, android.KeyMappings, defaultKey, Convert.ToBase64String(keystoreBytes));
                        }

                        // Key alias
                        var defaultAliasKey = $"ANDROID_{SanitizeLabel(android.Label)}_KEY_ALIAS";
                        AddMappedSecrets(secrets, android.KeyMappings, defaultAliasKey, keystore.Alias);

                        // Keystore password from secure storage
                        var pwd = await _secureStorage.GetAsync($"android_keystore_pwd_{keystore.Id}");
                        if (!string.IsNullOrEmpty(pwd))
                        {
                            var defaultPwdKey = $"ANDROID_{SanitizeLabel(android.Label)}_KEYSTORE_PASSWORD";
                            AddMappedSecrets(secrets, android.KeyMappings, defaultPwdKey, pwd);

                            // Key password (typically same as keystore password for PKCS12)
                            var defaultKeyPwdKey = $"ANDROID_{SanitizeLabel(android.Label)}_KEY_PASSWORD";
                            AddMappedSecrets(secrets, android.KeyMappings, defaultKeyPwdKey, pwd);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to resolve keystore for {android.Label}: {ex.Message}");
                }
            }
        }

        // Resolve Google developer identities
        foreach (var googleIdentity in profile.GoogleIdentities ?? Enumerable.Empty<PublishProfileGoogleIdentity>())
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(googleIdentity.IdentityId))
                continue;

            progress?.Report($"Fetching Google identity {googleIdentity.Label}...");
            try
            {
                var identity = await _googleIdentity.GetIdentityAsync(googleIdentity.IdentityId);
                if (identity is null)
                {
                    _logger.LogWarning($"Google identity '{googleIdentity.IdentityId}' was not found for publish profile item '{googleIdentity.Label}'");
                    continue;
                }

                var defaultKeys = GetGoogleIdentityDefaultKeys(googleIdentity);
                if (!string.IsNullOrEmpty(identity.ProjectId))
                    AddMappedSecrets(secrets, googleIdentity.KeyMappings, defaultKeys.ProjectId, identity.ProjectId);
                if (!string.IsNullOrEmpty(identity.ClientEmail))
                    AddMappedSecrets(secrets, googleIdentity.KeyMappings, defaultKeys.ClientEmail, identity.ClientEmail);
                if (!string.IsNullOrEmpty(identity.ServiceAccountJson))
                    AddMappedSecrets(secrets, googleIdentity.KeyMappings, defaultKeys.ServiceAccountJson, identity.ServiceAccountJson);
                else
                    _logger.LogWarning($"Google identity '{identity.Name}' has no service account JSON to publish");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to resolve Google identity for {googleIdentity.Label}: {ex.Message}");
            }
        }

        // Resolve managed secrets
        foreach (var mapping in profile.SecretMappings)
        {
            ct.ThrowIfCancellationRequested();

            progress?.Report($"Fetching secret {mapping.SourceKey}...");
            try
            {
                var valueBytes = await _managedSecrets.GetValueAsync(mapping.SourceKey, ct);
                if (valueBytes is not null)
                {
                    var value = Encoding.UTF8.GetString(valueBytes);
                    foreach (var destKey in mapping.DestinationKeys)
                    {
                        secrets[destKey] = value;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to resolve secret '{mapping.SourceKey}': {ex.Message}");
            }
        }

        progress?.Report($"Resolved {secrets.Count} secrets");
        return secrets;
    }

    static void AddMappedSecrets(
        Dictionary<string, string> secrets,
        Dictionary<string, List<string>> keyMappings,
        string defaultKey,
        string value)
    {
        if (keyMappings.TryGetValue(defaultKey, out var destinations) && destinations.Count > 0)
        {
            foreach (var dest in destinations)
                secrets[dest] = value;
        }
        else
        {
            secrets[defaultKey] = value;
        }
    }

    /// <summary>
    /// Build the Apple key prefix from platform/distribution enums,
    /// matching the logic in AppleConfigVM.GetDefaultKeys().
    /// Falls back to sanitized label if enums are null.
    /// </summary>
    static string GetAppleKeyPrefix(PublishProfileAppleConfig config)
    {
        var platLabel = config.Platform switch
        {
            ApplePlatformType.iOS => "IOS",
            ApplePlatformType.MacCatalyst => "MACCATALYST",
            ApplePlatformType.macOS => "MACOS",
            _ => SanitizeLabel(config.Label)
        };
        var distLabel = config.DistributionType switch
        {
            AppleDistributionType.Development => "DEV",
            AppleDistributionType.AdHoc => "ADHOC",
            AppleDistributionType.AppStore => "APPSTORE",
            AppleDistributionType.Direct => "DIRECT",
            _ => ""
        };
        return string.IsNullOrEmpty(distLabel) ? $"APPLE_{platLabel}" : $"APPLE_{platLabel}_{distLabel}";
    }

    static (string KeyId, string IssuerId, string P8Key) GetAppleIdentityDefaultKeys(PublishProfileAppleIdentity identity)
    {
        var label = SanitizeLabel(identity.Label);
        return (
            $"APPLE_{label}_KEY_ID",
            $"APPLE_{label}_ISSUER_ID",
            $"APPLE_{label}_P8_KEY");
    }

    static (string ProjectId, string ClientEmail, string ServiceAccountJson) GetGoogleIdentityDefaultKeys(PublishProfileGoogleIdentity identity)
    {
        var label = SanitizeLabel(identity.Label);
        return (
            $"GOOGLE_{label}_PROJECT_ID",
            $"GOOGLE_{label}_CLIENT_EMAIL",
            $"GOOGLE_{label}_SERVICE_ACCOUNT_JSON");
    }

    static string SanitizeLabel(string label)
        => label.ToUpperInvariant().Replace(' ', '_').Replace('-', '_');
}
