using System.Text.Json;
using System.Text;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public class ManagedSecretsService : IManagedSecretsService
{
    public const string FolderPlaceholderKey = "sherpa-folder-marker";

    readonly ICloudSecretsService _cloudService;
    readonly ILoggingService _logger;
    readonly ISecretsProviderRegistry? _providerRegistry;
    static readonly byte[] FolderPlaceholderValue = Encoding.UTF8.GetBytes("""{"kind":"maui-sherpa-folder"}""");

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public ManagedSecretsService(
        ICloudSecretsService cloudService,
        ILoggingService logger,
        ISecretsProviderRegistry? providerRegistry = null)
    {
        _cloudService = cloudService;
        _logger = logger;
        _providerRegistry = providerRegistry;
    }

    public async Task<IReadOnlyList<ManagedSecret>> ListAsync(CancellationToken cancellationToken = default)
    {
        var secrets = new Dictionary<string, ManagedSecret>(StringComparer.Ordinal);
        foreach (var provider in await GetReadProvidersAsync(cancellationToken))
        {
            try
            {
                // Metadata keys are the source of truth because providers may sanitize value keys.
                var metaKeys = await provider.ListSecretsAsync(
                    IManagedSecretsService.MetadataPrefix,
                    cancellationToken);
                foreach (var fullMetaKey in metaKeys)
                {
                    var metaBytes = await provider.GetSecretAsync(fullMetaKey, cancellationToken);
                    if (metaBytes is null)
                        continue;

                    var json = Encoding.UTF8.GetString(metaBytes);
                    var meta = JsonSerializer.Deserialize<ManagedSecret>(json, JsonOptions);
                    if (meta is not null)
                        secrets.TryAdd(meta.Key, meta);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to enumerate managed secrets from {provider.DisplayName}: {ex.Message}");
            }
        }

        return secrets.Values
            .OrderBy(secret => secret.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<ManagedSecretFolder>> ListFoldersAsync(CancellationToken cancellationToken = default)
    {
        var folders = new List<ManagedSecretFolder>();
        foreach (var provider in await GetReadProvidersAsync(cancellationToken))
        {
            try
            {
                var folderKeys = await provider.ListSecretsAsync(
                    IManagedSecretsService.FolderPrefix,
                    cancellationToken);
                foreach (var fullFolderKey in folderKeys)
                {
                    var folderBytes = await provider.GetSecretAsync(fullFolderKey, cancellationToken);
                    if (folderBytes is null)
                        continue;

                    var json = Encoding.UTF8.GetString(folderBytes);
                    var folder = JsonSerializer.Deserialize<ManagedSecretFolder>(json, JsonOptions);
                    if (folder is not null)
                        folders.Add(folder);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to enumerate managed secret folders from {provider.DisplayName}: {ex.Message}");
            }
        }

        return folders
            .GroupBy(f => f.Path, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<bool> CreateFolderAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        if (_cloudService.ActiveProvider is null)
            throw new InvalidOperationException("No active cloud secrets provider configured.");

        var normalizedPath = SecretPath.NormalizeFolderPath(folderPath);
        if (normalizedPath == "/")
            throw new ArgumentException("Root folder already exists.", nameof(folderPath));

        var folder = new ManagedSecretFolder(
            normalizedPath,
            GetFolderName(normalizedPath),
            DateTime.UtcNow);
        var stored = await StoreFolderAsync(folder, cancellationToken);

        if (stored)
            _logger.LogInformation($"Created managed secrets folder: {normalizedPath}");

        return stored;
    }

    public async Task<bool> RenameFolderAsync(string folderPath, string newFolderPath, CancellationToken cancellationToken = default)
    {
        if (_cloudService.ActiveProvider is null)
            throw new InvalidOperationException("No active cloud secrets provider configured.");

        var oldPath = SecretPath.NormalizeFolderPath(folderPath);
        var normalizedNewPath = SecretPath.NormalizeFolderPath(newFolderPath);
        if (oldPath == "/" || normalizedNewPath == "/")
            throw new ArgumentException("Root folder cannot be renamed.");
        if (oldPath == normalizedNewPath)
            return true;
        if (normalizedNewPath.StartsWith(oldPath + "/", StringComparison.Ordinal))
            throw new ArgumentException("A folder cannot be moved inside itself.", nameof(newFolderPath));

        var secrets = await ListAsync(cancellationToken);
        var affectedSecrets = secrets
            .Where(secret => IsInFolderTree(GetSecretFolder(secret.Key), oldPath))
            .ToList();
        var movedKeys = affectedSecrets
            .Select(secret => MoveKey(secret.Key, oldPath, normalizedNewPath))
            .ToHashSet(StringComparer.Ordinal);
        if (secrets.Any(secret => !affectedSecrets.Any(affected => affected.Key == secret.Key) && movedKeys.Contains(secret.Key)))
            return false;

        var secretMoves = new List<(ManagedSecret Existing, ManagedSecret Moved, byte[] Value)>();
        foreach (var secret in affectedSecrets)
        {
            var value = await GetValueAsync(secret.Key, cancellationToken);
            if (value is null)
                return false;

            var moved = secret with
            {
                Key = MoveKey(secret.Key, oldPath, normalizedNewPath),
                UpdatedAt = DateTime.UtcNow
            };
            secretMoves.Add((secret, moved, value));
        }

        var folders = await ListFoldersAsync(cancellationToken);
        var affectedFolders = folders
            .Where(folder => folder.Path == oldPath || folder.Path.StartsWith(oldPath + "/", StringComparison.Ordinal))
            .ToList();
        if (!affectedFolders.Any(folder => folder.Path == oldPath))
            affectedFolders.Insert(0, new ManagedSecretFolder(oldPath, GetFolderName(oldPath), DateTime.UtcNow));

        var folderMoves = affectedFolders
            .Select(folder =>
            {
                var movedPath = MoveFolderPath(folder.Path, oldPath, normalizedNewPath);
                return (Existing: folder, Moved: new ManagedSecretFolder(movedPath, GetFolderName(movedPath), DateTime.UtcNow));
            })
            .ToList();

        var stagedSecretKeys = new List<string>();
        var stagedFolderPaths = new List<string>();
        foreach (var (_, moved, value) in secretMoves)
        {
            var stored = await _cloudService.StoreSecretAsync(
                IManagedSecretsService.SecretPrefix + moved.Key,
                value,
                cancellationToken: cancellationToken);
            if (!stored)
            {
                await RollbackFolderRenameStagingAsync(
                    stagedSecretKeys,
                    stagedFolderPaths,
                    cancellationToken);
                return false;
            }

            stagedSecretKeys.Add(moved.Key);
            if (!await SaveMetadataAsync(moved, cancellationToken))
            {
                await RollbackFolderRenameStagingAsync(
                    stagedSecretKeys,
                    stagedFolderPaths,
                    cancellationToken);
                return false;
            }
        }

        foreach (var (_, moved) in folderMoves)
        {
            var stored = await StoreFolderAsync(moved, cancellationToken);
            if (!stored)
            {
                await RollbackFolderRenameStagingAsync(
                    stagedSecretKeys,
                    stagedFolderPaths,
                    cancellationToken);
                return false;
            }
            stagedFolderPaths.Add(moved.Path);
        }

        foreach (var (existing, _, _) in secretMoves)
            await DeleteAsync(existing.Key, cancellationToken);

        foreach (var (existing, _) in folderMoves)
        {
            await _cloudService.DeleteSecretAsync(GetFolderMetadataKey(existing.Path), cancellationToken);
            await _cloudService.DeleteSecretAsync(GetFolderPlaceholderKey(existing.Path), cancellationToken);
        }

        _logger.LogInformation($"Renamed managed secrets folder: {oldPath} -> {normalizedNewPath}");
        return true;
    }

    async Task RollbackFolderRenameStagingAsync(
        IEnumerable<string> secretKeys,
        IEnumerable<string> folderPaths,
        CancellationToken cancellationToken)
    {
        foreach (var key in secretKeys.Reverse())
        {
            await _cloudService.DeleteSecretAsync(
                IManagedSecretsService.SecretPrefix + key,
                cancellationToken);
            await _cloudService.DeleteSecretAsync(
                IManagedSecretsService.MetadataPrefix + key,
                cancellationToken);
        }

        foreach (var path in folderPaths.Reverse())
        {
            await _cloudService.DeleteSecretAsync(
                GetFolderMetadataKey(path),
                cancellationToken);
            await _cloudService.DeleteSecretAsync(
                GetFolderPlaceholderKey(path),
                cancellationToken);
        }
    }

    public async Task<bool> DeleteFolderAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        if (_cloudService.ActiveProvider is null)
            throw new InvalidOperationException("No active cloud secrets provider configured.");

        var normalizedPath = SecretPath.NormalizeFolderPath(folderPath);
        if (normalizedPath == "/")
            throw new ArgumentException("Root folder cannot be deleted.", nameof(folderPath));

        var secrets = await ListAsync(cancellationToken);
        if (secrets.Any(secret => IsInFolderTree(GetSecretFolder(secret.Key), normalizedPath)))
            return false;

        var folders = await ListFoldersAsync(cancellationToken);
        if (folders.Any(folder => folder.Path != normalizedPath && folder.Path.StartsWith(normalizedPath + "/", StringComparison.Ordinal)))
            return false;

        var metadataDeleted = await _cloudService.DeleteSecretAsync(GetFolderMetadataKey(normalizedPath), cancellationToken);
        var placeholderDeleted = await _cloudService.DeleteSecretAsync(GetFolderPlaceholderKey(normalizedPath), cancellationToken);
        var deleted = metadataDeleted && placeholderDeleted;
        if (deleted)
            _logger.LogInformation($"Deleted managed secrets folder: {normalizedPath}");

        return deleted;
    }

    public async Task<ManagedSecret?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        return await LoadMetadataAsync(key, cancellationToken);
    }

    public async Task<byte[]?> GetValueAsync(string key, CancellationToken cancellationToken = default)
    {
        var fullKey = IManagedSecretsService.SecretPrefix + key;
        foreach (var provider in await GetReadProvidersAsync(cancellationToken))
        {
            try
            {
                var value = await provider.GetSecretAsync(fullKey, cancellationToken);
                if (value is not null)
                    return value;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to read managed secret '{key}' from {provider.DisplayName}: {ex.Message}");
            }
        }

        return null;
    }

    public async Task<bool> CreateAsync(string key, byte[] value, ManagedSecretType type,
        string? description = null, string? originalFileName = null, Dictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        if (_cloudService.ActiveProvider is null)
            throw new InvalidOperationException("No active cloud secrets provider configured.");

        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Secret key cannot be empty.", nameof(key));

        if (SecretPath.FromFlatKey(key).Key.Equals(FolderPlaceholderKey, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Secret key is reserved for folder metadata.", nameof(key));

        var fullKey = IManagedSecretsService.SecretPrefix + key;
        var now = DateTime.UtcNow;

        var stored = await _cloudService.StoreSecretAsync(fullKey, value, cancellationToken: cancellationToken);
        if (!stored)
            return false;

        var meta = new ManagedSecret(key, type, description, originalFileName, now, now, metadata);
        if (!await SaveMetadataAsync(meta, cancellationToken))
        {
            await _cloudService.DeleteSecretAsync(fullKey, cancellationToken);
            return false;
        }

        _logger.LogInformation($"Created managed secret: {key} (type: {type})");
        return true;
    }

    public async Task<bool> UpdateAsync(string key, byte[]? value = null, string? description = null,
        Dictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        if (_cloudService.ActiveProvider is null)
            throw new InvalidOperationException("No active cloud secrets provider configured.");

        var existing = await LoadMetadataAsync(key, cancellationToken);
        if (existing is null)
            return false;

        if (value is not null)
        {
            var fullKey = IManagedSecretsService.SecretPrefix + key;
            var stored = await _cloudService.StoreSecretAsync(fullKey, value, cancellationToken: cancellationToken);
            if (!stored)
                return false;
        }

        var updated = existing with
        {
            Description = description ?? existing.Description,
            Metadata = metadata is null
                ? existing.Metadata
                : new Dictionary<string, string>(metadata, StringComparer.Ordinal),
            UpdatedAt = DateTime.UtcNow
        };
        if (!await SaveMetadataAsync(updated, cancellationToken))
            return false;

        _logger.LogInformation($"Updated managed secret: {key}");
        return true;
    }

    public async Task<bool> MoveAsync(string key, string newKey, byte[]? value = null, string? description = null,
        Dictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        if (_cloudService.ActiveProvider is null)
            throw new InvalidOperationException("No active cloud secrets provider configured.");

        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Secret key cannot be empty.", nameof(key));
        if (string.IsNullOrWhiteSpace(newKey))
            throw new ArgumentException("Secret key cannot be empty.", nameof(newKey));

        var oldPath = SecretPath.FromFlatKey(key);
        var newPath = SecretPath.FromFlatKey(newKey);
        if (newPath.Key.Equals(FolderPlaceholderKey, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Secret key is reserved for folder metadata.", nameof(newKey));

        if (oldPath.ToFlatKey() == newPath.ToFlatKey())
            return await UpdateAsync(key, value, description, metadata, cancellationToken);

        var existing = await LoadMetadataAsync(key, cancellationToken);
        if (existing is null)
            return false;

        if (await LoadMetadataAsync(newKey, cancellationToken) is not null ||
            await _cloudService.SecretExistsAsync(IManagedSecretsService.SecretPrefix + newKey, cancellationToken))
        {
            return false;
        }

        var valueToStore = value ?? await GetValueAsync(key, cancellationToken);
        if (valueToStore is null)
            return false;

        var stored = await _cloudService.StoreSecretAsync(
            IManagedSecretsService.SecretPrefix + newKey,
            valueToStore,
            cancellationToken: cancellationToken);
        if (!stored)
            return false;

        var moved = existing with
        {
            Key = newKey,
            Description = description ?? existing.Description,
            Metadata = metadata is null
                ? existing.Metadata
                : new Dictionary<string, string>(metadata, StringComparer.Ordinal),
            UpdatedAt = DateTime.UtcNow
        };
        var metadataStored = await SaveMetadataAsync(moved, cancellationToken);
        if (!metadataStored)
        {
            await _cloudService.DeleteSecretAsync(IManagedSecretsService.SecretPrefix + newKey, cancellationToken);
            return false;
        }

        var deleted = await DeleteAsync(key, cancellationToken);
        if (!deleted)
            return false;

        _logger.LogInformation($"Moved managed secret: {key} -> {newKey}");
        return true;
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_cloudService.ActiveProvider is null)
            throw new InvalidOperationException("No active cloud secrets provider configured.");

        var fullKey = IManagedSecretsService.SecretPrefix + key;
        var metaKey = IManagedSecretsService.MetadataPrefix + key;

        var deleted = await _cloudService.DeleteSecretAsync(fullKey, cancellationToken);

        // Always try to delete metadata even if value deletion failed
        try
        {
            await _cloudService.DeleteSecretAsync(metaKey, cancellationToken);
        }
        catch
        {
            // Metadata cleanup is best-effort
        }

        _logger.LogInformation($"Deleted managed secret: {key}");
        return deleted;
    }

    async Task<ManagedSecret?> LoadMetadataAsync(string key, CancellationToken cancellationToken)
    {
        foreach (var provider in await GetReadProvidersAsync(cancellationToken))
        {
            try
            {
                var metaKey = IManagedSecretsService.MetadataPrefix + key;
                var metaBytes = await provider.GetSecretAsync(metaKey, cancellationToken);
                if (metaBytes is null)
                    continue;

                var json = Encoding.UTF8.GetString(metaBytes);
                return JsonSerializer.Deserialize<ManagedSecret>(json, JsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to load metadata for secret '{key}' from {provider.DisplayName}: {ex.Message}");
            }
        }

        return null;
    }

    async Task<bool> SaveMetadataAsync(ManagedSecret meta, CancellationToken cancellationToken)
    {
        var metaKey = IManagedSecretsService.MetadataPrefix + meta.Key;
        var json = JsonSerializer.Serialize(meta, JsonOptions);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        return await _cloudService.StoreSecretAsync(metaKey, bytes, cancellationToken: cancellationToken);
    }

    async Task<bool> StoreFolderAsync(ManagedSecretFolder folder, CancellationToken cancellationToken)
    {
        var placeholderStored = await _cloudService.StoreSecretAsync(
            GetFolderPlaceholderKey(folder.Path),
            FolderPlaceholderValue,
            new Dictionary<string, string>
            {
                ["SherpaKind"] = "FolderPlaceholder",
                ["FolderPath"] = folder.Path
            },
            cancellationToken);
        if (!placeholderStored)
            return false;

        var json = JsonSerializer.Serialize(folder, JsonOptions);
        var metadataStored = await _cloudService.StoreSecretAsync(
            GetFolderMetadataKey(folder.Path),
            Encoding.UTF8.GetBytes(json),
            cancellationToken: cancellationToken);
        if (!metadataStored)
        {
            await _cloudService.DeleteSecretAsync(GetFolderPlaceholderKey(folder.Path), cancellationToken);
            return false;
        }

        return true;
    }

    async Task<IReadOnlyList<ICloudSecretsProvider>> GetReadProvidersAsync(
        CancellationToken cancellationToken)
    {
        if (_providerRegistry is null)
        {
            if (_cloudService.ActiveProvider is null)
                return Array.Empty<ICloudSecretsProvider>();

            return [new ActiveCloudProviderAdapter(_cloudService)];
        }

        var providers = new List<ICloudSecretsProvider>();
        var configs = await _providerRegistry.GetProvidersAsync();
        foreach (var config in configs
            .OrderBy(provider => provider.ProviderType == CloudSecretsProviderType.Local ? 0 : 1)
            .ThenBy(provider => provider.Name, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var provider = await _providerRegistry.GetProviderAsync(config.Id);
            if (provider is not null)
                providers.Add(provider);
        }

        return providers;
    }

    static string GetFolderMetadataKey(string folderPath) =>
        IManagedSecretsService.FolderPrefix + SecretPath.NormalizeFolderPath(folderPath).TrimStart('/');

    static string GetFolderPlaceholderKey(string folderPath) =>
        IManagedSecretsService.SecretPrefix + new SecretPath(folderPath, FolderPlaceholderKey).ToFlatKey();

    static string GetFolderName(string folderPath)
    {
        var normalized = SecretPath.NormalizeFolderPath(folderPath);
        var lastSeparator = normalized.LastIndexOf('/');
        return lastSeparator < 0 ? normalized : normalized[(lastSeparator + 1)..];
    }

    static string GetSecretFolder(string key)
    {
        var lastSeparator = key.LastIndexOf('/');
        return lastSeparator < 0 ? "/" : SecretPath.NormalizeFolderPath(key[..lastSeparator]);
    }

    static bool IsInFolderTree(string secretFolder, string folder)
    {
        return secretFolder == folder ||
            (folder != "/" && secretFolder.StartsWith(folder + "/", StringComparison.Ordinal));
    }

    static string MoveKey(string key, string oldFolderPath, string newFolderPath)
    {
        var relativeKey = key[(oldFolderPath.TrimStart('/').Length + 1)..];
        return newFolderPath.TrimStart('/') + "/" + relativeKey;
    }

    static string MoveFolderPath(string folderPath, string oldFolderPath, string newFolderPath)
    {
        var relativePath = folderPath == oldFolderPath
            ? ""
            : folderPath[(oldFolderPath.Length + 1)..];
        return SecretPath.NormalizeFolderPath(string.IsNullOrEmpty(relativePath)
            ? newFolderPath
            : newFolderPath + "/" + relativePath);
    }

    private sealed class ActiveCloudProviderAdapter : ICloudSecretsProvider
    {
        private readonly ICloudSecretsService _cloudService;

        public ActiveCloudProviderAdapter(ICloudSecretsService cloudService)
        {
            _cloudService = cloudService;
        }

        public CloudSecretsProviderType ProviderType =>
            _cloudService.ActiveProvider?.ProviderType ?? CloudSecretsProviderType.None;

        public string DisplayName => _cloudService.ActiveProvider?.Name ?? "Active provider";

        public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_cloudService.ActiveProvider is not null);

        public Task<bool> StoreSecretAsync(
            string key,
            byte[] value,
            Dictionary<string, string>? metadata = null,
            CancellationToken cancellationToken = default) =>
            _cloudService.StoreSecretAsync(key, value, metadata, cancellationToken);

        public Task<byte[]?> GetSecretAsync(string key, CancellationToken cancellationToken = default) =>
            _cloudService.GetSecretAsync(key, cancellationToken);

        public Task<Dictionary<string, string>?> GetSecretMetadataAsync(
            string key,
            CancellationToken cancellationToken = default) =>
            _cloudService.GetSecretMetadataAsync(key, cancellationToken);

        public Task<bool> SetSecretMetadataAsync(
            string key,
            Dictionary<string, string> metadata,
            CancellationToken cancellationToken = default) =>
            _cloudService.SetSecretMetadataAsync(key, metadata, cancellationToken);

        public Task<bool> DeleteSecretAsync(string key, CancellationToken cancellationToken = default) =>
            _cloudService.DeleteSecretAsync(key, cancellationToken);

        public Task<bool> SecretExistsAsync(string key, CancellationToken cancellationToken = default) =>
            _cloudService.SecretExistsAsync(key, cancellationToken);

        public Task<IReadOnlyList<string>> ListSecretsAsync(
            string? prefix = null,
            CancellationToken cancellationToken = default) =>
            _cloudService.ListSecretsAsync(prefix, cancellationToken);
    }
}
