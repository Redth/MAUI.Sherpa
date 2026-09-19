using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using MauiSherpa.Core.Interfaces;
using Microsoft.Data.Sqlite;
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
            throw;
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
            throw;
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
            throw;
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
            throw;
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
                !File.Exists(_legacyDatabasePath))
            {
                _legacyMigrationChecked = true;
                return;
            }

            var migrationMarker = await GetMigrationMarkerAsync(cancellationToken);
            if (migrationMarker is not null)
            {
                await DeleteMigratedDatabaseIfUnchangedAsync(migrationMarker, cancellationToken);
                _legacyMigrationChecked = true;
                return;
            }

            var key = await _keyStore.GetOrCreateKeyAsync(cancellationToken);
            var legacyProvider = new SqlCipherDatabaseProvider(_legacyDatabasePath, key);
            IReadOnlyList<LocalSecretDocument> legacyDocuments;
            try
            {
                using var legacyStore = new DocumentStore(new DocumentStoreOptions
                {
                    DatabaseProvider = legacyProvider
                });
                legacyDocuments = await legacyStore.Query<LocalSecretDocument>().ToList();
            }
            finally
            {
                // Disposing the store returns its connection to the pool. Release this database's
                // native handle as well so cleanup can remove it on Windows.
                using var connection = (SqliteConnection)legacyProvider.CreateConnection();
                SqliteConnection.ClearPool(connection);
            }

            foreach (var document in legacyDocuments)
            {
                if (string.IsNullOrWhiteSpace(document.Key))
                    throw new InvalidOperationException("A legacy local secret has no key; the source database has been retained.");

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

                var migrated = await _vaultStore.GetAsync(
                    LocalVaultScopes.LocalProviderSecret, path.FolderPath, path.Key, cancellationToken);
                if (migrated is null ||
                    !migrated.Value.SequenceEqual(document.Value) ||
                    migrated.ContentType != LocalVaultContentTypes.Binary ||
                    migrated.Metadata.Count != metadata.Count ||
                    metadata.Any(x => !migrated.Metadata.TryGetValue(x.Key, out var value) || value != x.Value))
                {
                    throw new InvalidOperationException($"Failed to verify migrated local secret '{document.Key}'.");
                }
            }

            var sourceHashes = await GetSourceHashesAsync(_legacyDatabasePath, cancellationToken);
            await MarkMigrationStepCompleteAsync(legacyDocuments.Count, sourceHashes, cancellationToken);
            DeleteLegacyDatabaseFiles(_legacyDatabasePath);
            _logger.LogInformation($"Migrated {legacyDocuments.Count} local secrets into the shared local vault.");
            _legacyMigrationChecked = true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Local secrets legacy database migration failed: {ex.Message}", ex);
            throw;
        }
        finally
        {
            _migrationLock.Release();
        }
    }

    private Task<LocalVaultItem?> GetMigrationMarkerAsync(CancellationToken cancellationToken)
    {
        return _vaultStore.GetAsync(
            LocalVaultScopes.Migration,
            "/",
            LegacyMigrationStepId,
            cancellationToken);
    }

    private async Task MarkMigrationStepCompleteAsync(
        int migratedCount,
        Dictionary<string, string> sourceHashes,
        CancellationToken cancellationToken)
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
            sourceHashes,
            cancellationToken: cancellationToken);
    }

    private async Task DeleteMigratedDatabaseIfUnchangedAsync(
        LocalVaultItem migrationMarker,
        CancellationToken cancellationToken)
    {
        if (!migrationMarker.Metadata.TryGetValue("SourceDatabaseSha256", out var migratedDatabaseSha256))
            return;

        var currentDatabaseSha256 = await GetFileSha256Async(_legacyDatabasePath, cancellationToken);
        var walPath = _legacyDatabasePath + "-wal";
        var walMatches = !File.Exists(walPath) ||
            migrationMarker.Metadata.TryGetValue("SourceWalSha256", out var migratedWalSha256) &&
            string.Equals(
                await GetFileSha256Async(walPath, cancellationToken),
                migratedWalSha256,
                StringComparison.Ordinal);
        if (!string.Equals(currentDatabaseSha256, migratedDatabaseSha256, StringComparison.Ordinal) ||
            !walMatches)
        {
            _logger.LogWarning("A different legacy local secrets database exists after migration; it has been retained.");
            return;
        }

        DeleteLegacyDatabaseFiles(_legacyDatabasePath);
        _logger.LogInformation("Completed deferred cleanup of the migrated local secrets database.");
    }

    private static async Task<Dictionary<string, string>> GetSourceHashesAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        var hashes = new Dictionary<string, string>
        {
            ["SourceDatabaseSha256"] = await GetFileSha256Async(databasePath, cancellationToken)
        };
        var walPath = databasePath + "-wal";
        if (File.Exists(walPath))
            hashes["SourceWalSha256"] = await GetFileSha256Async(walPath, cancellationToken);
        return hashes;
    }

    private static async Task<string> GetFileSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static string GetFlatKey(LocalVaultItem item)
    {
        return item.Metadata.TryGetValue(OriginalFlatKeyMetadataName, out var originalKey)
            ? originalKey
            : new SecretPath(item.Path, item.Key).ToFlatKey();
    }

    private static void DeleteLegacyDatabaseFiles(string legacyDatabasePath)
    {
        // Remove sidecars before the main database so any failure leaves the source discoverable
        // and eligible for a fingerprint-verified cleanup retry.
        foreach (var path in new[]
        {
            legacyDatabasePath + "-wal",
            legacyDatabasePath + "-shm",
            legacyDatabasePath
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
