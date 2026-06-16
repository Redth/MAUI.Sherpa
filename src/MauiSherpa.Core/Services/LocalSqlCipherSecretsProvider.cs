using System.Text;
using System.Text.Json;
using MauiSherpa.Core.Interfaces;
using Shiny.DocumentDb;
using Shiny.DocumentDb.Sqlite.SqlCipher;

namespace MauiSherpa.Core.Services;

public class LocalSqlCipherSecretsProvider : ICloudSecretsProvider
{
    public const string DefaultDatabaseFileName = LocalVaultOptions.DefaultDatabaseFileName;
    public const string LegacyDatabaseFileName = "local-secrets.db";
    public const string DatabasePathSettingKey = "DatabasePath";

    private const string OriginalFlatKeyMetadataName = "OriginalFlatKey";
    private const string LegacyMigrationStepId = "local-provider-legacy-db";

    private readonly ILoggingService _logger;
    private readonly ILocalVaultStore _vaultStore;
    private readonly ILocalVaultKeyStore? _keyStore;
    private readonly string _legacyDatabasePath;
    private readonly SemaphoreSlim _migrationLock = new(1, 1);
    private bool _legacyMigrationChecked;

    public LocalSqlCipherSecretsProvider(
        CloudSecretsProviderConfig config,
        ILoggingService logger,
        ILocalVaultStore vaultStore,
        ILocalVaultKeyStore? keyStore = null)
    {
        _logger = logger;
        _vaultStore = vaultStore;
        _keyStore = keyStore;
        _legacyDatabasePath = GetLegacyDatabasePath(config);
    }

    public LocalSqlCipherSecretsProvider(
        CloudSecretsProviderConfig config,
        ILoggingService logger,
        ILocalSecretsKeyStore keyStore)
        : this(
            config,
            logger,
            new SqlCipherLocalVaultStore(keyStore, logger, new LocalVaultOptions(GetVaultDatabasePath(config))),
            keyStore)
    {
    }

    public CloudSecretsProviderType ProviderType => CloudSecretsProviderType.Local;

    public string DisplayName => "Local";

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureLegacyDatabaseMigratedAsync(cancellationToken);
            await _vaultStore.ListAsync(LocalVaultScopes.LocalProviderSecret, cancellationToken: cancellationToken);
            _logger.LogInformation($"Local secrets provider is available in vault {_vaultStore.DatabasePath}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Local secrets provider connection test failed: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<bool> StoreSecretAsync(
        string key,
        byte[] value,
        Dictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureLegacyDatabaseMigratedAsync(cancellationToken);
            var path = SecretPath.FromFlatKey(key);
            var existing = await FindItemByFlatKeyAsync(key, cancellationToken);
            var itemMetadata = metadata is null && existing is not null
                ? new Dictionary<string, string>(existing.Metadata, StringComparer.Ordinal)
                : metadata is null
                    ? new Dictionary<string, string>(StringComparer.Ordinal)
                    : new Dictionary<string, string>(metadata, StringComparer.Ordinal);
            itemMetadata[OriginalFlatKeyMetadataName] = key;

            await _vaultStore.PutAsync(
                LocalVaultScopes.LocalProviderSecret,
                path.FolderPath,
                path.Key,
                value,
                LocalVaultContentTypes.Binary,
                itemMetadata,
                cancellationToken);

            _logger.LogInformation($"Stored local secret: {key}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Local secrets provider store error: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<byte[]?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureLegacyDatabaseMigratedAsync(cancellationToken);
            var item = await FindItemByFlatKeyAsync(key, cancellationToken);
            return item?.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Local secrets provider get error: {ex.Message}", ex);
            return null;
        }
    }

    public async Task<Dictionary<string, string>?> GetSecretMetadataAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureLegacyDatabaseMigratedAsync(cancellationToken);
            var item = await FindItemByFlatKeyAsync(key, cancellationToken);
            if (item is null)
                return null;

            var metadata = new Dictionary<string, string>(item.Metadata, StringComparer.Ordinal);
            metadata.Remove(OriginalFlatKeyMetadataName);
            return metadata;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Local secrets provider metadata get error: {ex.Message}", ex);
            return null;
        }
    }

    public async Task<bool> SetSecretMetadataAsync(
        string key,
        Dictionary<string, string> metadata,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureLegacyDatabaseMigratedAsync(cancellationToken);
            var item = await FindItemByFlatKeyAsync(key, cancellationToken);
            if (item is null)
                return false;

            var itemMetadata = new Dictionary<string, string>(metadata, StringComparer.Ordinal)
            {
                [OriginalFlatKeyMetadataName] = GetFlatKey(item)
            };

            await _vaultStore.PutAsync(
                LocalVaultScopes.LocalProviderSecret,
                item.Path,
                item.Key,
                item.Value,
                item.ContentType,
                itemMetadata,
                cancellationToken);

            _logger.LogInformation($"Updated local secret metadata: {key}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Local secrets provider metadata set error: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<bool> DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureLegacyDatabaseMigratedAsync(cancellationToken);
            var item = await FindItemByFlatKeyAsync(key, cancellationToken);
            if (item is null)
                return true;

            await _vaultStore.RemoveAsync(
                LocalVaultScopes.LocalProviderSecret,
                item.Path,
                item.Key,
                cancellationToken);

            _logger.LogInformation($"Deleted local secret: {key}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Local secrets provider delete error: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<bool> SecretExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureLegacyDatabaseMigratedAsync(cancellationToken);
            return await FindItemByFlatKeyAsync(key, cancellationToken) is not null;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Local secrets provider exists check error: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<IReadOnlyList<string>> ListSecretsAsync(string? prefix = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureLegacyDatabaseMigratedAsync(cancellationToken);
            var documents = await _vaultStore.ListAsync(
                LocalVaultScopes.LocalProviderSecret,
                cancellationToken: cancellationToken);

            var keys = documents
                .Select(GetFlatKey)
                .Where(x => string.IsNullOrEmpty(prefix) || x.StartsWith(prefix, StringComparison.Ordinal))
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

            return keys.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Local secrets provider list error: {ex.Message}", ex);
            return Array.Empty<string>();
        }
    }

    private async Task<LocalVaultItem?> FindItemByFlatKeyAsync(string key, CancellationToken cancellationToken)
    {
        var path = SecretPath.FromFlatKey(key);
        var item = await _vaultStore.GetAsync(
            LocalVaultScopes.LocalProviderSecret,
            path.FolderPath,
            path.Key,
            cancellationToken);

        if (item is not null)
            return item;

        var allItems = await _vaultStore.ListAsync(
            LocalVaultScopes.LocalProviderSecret,
            cancellationToken: cancellationToken);

        return allItems.FirstOrDefault(x =>
            x.Metadata.TryGetValue(OriginalFlatKeyMetadataName, out var originalKey) &&
            string.Equals(originalKey, key, StringComparison.Ordinal));
    }

    private async Task EnsureLegacyDatabaseMigratedAsync(CancellationToken cancellationToken)
    {
        if (_legacyMigrationChecked)
            return;

        await _migrationLock.WaitAsync(cancellationToken);
        try
        {
            if (_legacyMigrationChecked)
                return;

            if (_keyStore is null ||
                string.Equals(_legacyDatabasePath, _vaultStore.DatabasePath, StringComparison.Ordinal) ||
                !File.Exists(_legacyDatabasePath) ||
                await IsMigrationStepCompleteAsync(cancellationToken))
            {
                _legacyMigrationChecked = true;
                return;
            }

            var key = await _keyStore.GetOrCreateKeyAsync(cancellationToken);
            var legacyStore = new DocumentStore(new DocumentStoreOptions
            {
                DatabaseProvider = new SqlCipherDatabaseProvider(_legacyDatabasePath, key)
            });

            var legacyDocuments = await legacyStore.Query<LocalSecretDocument>().ToList();
            foreach (var document in legacyDocuments.Where(x => !string.IsNullOrWhiteSpace(x.Key)))
            {
                var metadata = document.Metadata is null
                    ? new Dictionary<string, string>(StringComparer.Ordinal)
                    : new Dictionary<string, string>(document.Metadata, StringComparer.Ordinal);
                metadata[OriginalFlatKeyMetadataName] = document.Key;
                metadata["MigratedFrom"] = LegacyDatabaseFileName;

                var path = SecretPath.FromFlatKey(document.Key);
                await _vaultStore.PutAsync(
                    LocalVaultScopes.LocalProviderSecret,
                    path.FolderPath,
                    path.Key,
                    document.Value,
                    LocalVaultContentTypes.Binary,
                    metadata,
                    cancellationToken);
            }

            await MarkMigrationStepCompleteAsync(legacyDocuments.Count, cancellationToken);
            DeleteLegacyDatabaseFiles(_legacyDatabasePath);
            _logger.LogInformation($"Migrated {legacyDocuments.Count} local secrets into the shared local vault.");
            _legacyMigrationChecked = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Local secrets legacy database migration was skipped: {ex.Message}");
        }
        finally
        {
            _migrationLock.Release();
        }
    }

    private async Task<bool> IsMigrationStepCompleteAsync(CancellationToken cancellationToken)
    {
        return await _vaultStore.ExistsAsync(
            LocalVaultScopes.Migration,
            "/",
            LegacyMigrationStepId,
            cancellationToken);
    }

    private async Task MarkMigrationStepCompleteAsync(int migratedCount, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            Step = LegacyMigrationStepId,
            MigratedCount = migratedCount,
            CompletedAt = DateTime.UtcNow
        });

        await _vaultStore.PutAsync(
            LocalVaultScopes.Migration,
            "/",
            LegacyMigrationStepId,
            Encoding.UTF8.GetBytes(payload),
            LocalVaultContentTypes.Json,
            cancellationToken: cancellationToken);
    }

    private static string GetFlatKey(LocalVaultItem item)
    {
        return item.Metadata.TryGetValue(OriginalFlatKeyMetadataName, out var originalKey)
            ? originalKey
            : new SecretPath(item.Path, item.Key).ToFlatKey();
    }

    private static void DeleteLegacyDatabaseFiles(string legacyDatabasePath)
    {
        foreach (var path in new[]
        {
            legacyDatabasePath,
            legacyDatabasePath + "-wal",
            legacyDatabasePath + "-shm"
        })
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static string GetVaultDatabasePath(CloudSecretsProviderConfig config)
    {
        if (config.Settings.TryGetValue(DatabasePathSettingKey, out var configuredPath) &&
            !string.IsNullOrWhiteSpace(configuredPath))
        {
            return configuredPath;
        }

        return Path.Combine(AppDataPath.GetAppDataDirectory(), DefaultDatabaseFileName);
    }

    private static string GetLegacyDatabasePath(CloudSecretsProviderConfig config)
    {
        if (config.Settings.TryGetValue(DatabasePathSettingKey, out var configuredPath) &&
            !string.IsNullOrWhiteSpace(configuredPath))
        {
            return configuredPath;
        }

        return Path.Combine(AppDataPath.GetAppDataDirectory(), LegacyDatabaseFileName);
    }

    internal sealed class LocalSecretDocument
    {
        public string Id { get; set; } = "";
        public string Key { get; set; } = "";
        public byte[] Value { get; set; } = [];
        public Dictionary<string, string> Metadata { get; set; } = new();
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
