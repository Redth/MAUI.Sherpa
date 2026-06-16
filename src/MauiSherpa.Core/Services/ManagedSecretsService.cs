using System.Text.Json;
using System.Text;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public class ManagedSecretsService : IManagedSecretsService
{
    public const string FolderPlaceholderKey = "sherpa-folder-marker";

    readonly ICloudSecretsService _cloudService;
    readonly ILoggingService _logger;
    static readonly byte[] FolderPlaceholderValue = Encoding.UTF8.GetBytes("""{"kind":"maui-sherpa-folder"}""");

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public ManagedSecretsService(ICloudSecretsService cloudService, ILoggingService logger)
    {
        _cloudService = cloudService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ManagedSecret>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (_cloudService.ActiveProvider is null)
            return Array.Empty<ManagedSecret>();

        // List metadata keys as the source of truth (value keys may be sanitized by provider)
        var metaKeys = await _cloudService.ListSecretsAsync(IManagedSecretsService.MetadataPrefix, cancellationToken);
        var secrets = new List<ManagedSecret>();

        foreach (var fullMetaKey in metaKeys)
        {
            try
            {
                var metaBytes = await _cloudService.GetSecretAsync(fullMetaKey, cancellationToken);
                if (metaBytes is null)
                    continue;

                var json = System.Text.Encoding.UTF8.GetString(metaBytes);
                var meta = JsonSerializer.Deserialize<ManagedSecret>(json, JsonOptions);
                if (meta is not null)
                    secrets.Add(meta);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to load metadata for '{fullMetaKey}': {ex.Message}");
            }
        }

        return secrets;
    }

    public async Task<IReadOnlyList<ManagedSecretFolder>> ListFoldersAsync(CancellationToken cancellationToken = default)
    {
        if (_cloudService.ActiveProvider is null)
            return Array.Empty<ManagedSecretFolder>();

        var folderKeys = await _cloudService.ListSecretsAsync(IManagedSecretsService.FolderPrefix, cancellationToken);
        var folders = new List<ManagedSecretFolder>();

        foreach (var fullFolderKey in folderKeys)
        {
            try
            {
                var folderBytes = await _cloudService.GetSecretAsync(fullFolderKey, cancellationToken);
                if (folderBytes is null)
                    continue;

                var json = System.Text.Encoding.UTF8.GetString(folderBytes);
                var folder = JsonSerializer.Deserialize<ManagedSecretFolder>(json, JsonOptions);
                if (folder is not null)
                    folders.Add(folder);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to load folder metadata for '{fullFolderKey}': {ex.Message}");
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

        foreach (var (_, moved, value) in secretMoves)
        {
            var stored = await _cloudService.StoreSecretAsync(
                IManagedSecretsService.SecretPrefix + moved.Key,
                value,
                cancellationToken: cancellationToken);
            if (!stored)
                return false;

            await SaveMetadataAsync(moved, cancellationToken);
        }

        foreach (var (_, moved) in folderMoves)
        {
            var stored = await StoreFolderAsync(moved, cancellationToken);
            if (!stored)
                return false;
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
        if (_cloudService.ActiveProvider is null)
            return null;

        return await LoadMetadataAsync(key, cancellationToken);
    }

    public async Task<byte[]?> GetValueAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_cloudService.ActiveProvider is null)
            return null;

        var fullKey = IManagedSecretsService.SecretPrefix + key;
        return await _cloudService.GetSecretAsync(fullKey, cancellationToken);
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
        try
        {
            var metaKey = IManagedSecretsService.MetadataPrefix + key;
            var metaBytes = await _cloudService.GetSecretAsync(metaKey, cancellationToken);
            if (metaBytes is null)
                return null;

            var json = System.Text.Encoding.UTF8.GetString(metaBytes);
            return JsonSerializer.Deserialize<ManagedSecret>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to load metadata for secret '{key}': {ex.Message}");
            return null;
        }
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
}
