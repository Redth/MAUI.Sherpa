using MauiSherpa.Core.Interfaces;
using Shiny.DocumentDb;
using Shiny.DocumentDb.Sqlite.SqlCipher;

namespace MauiSherpa.Core.Services;

public sealed class SqlCipherLocalVaultStore : ILocalVaultStore
{
    private readonly ILocalVaultKeyStore _keyStore;
    private readonly ILoggingService _logger;
    private readonly SemaphoreSlim _openLock = new(1, 1);
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private IDocumentStore? _store;

    public SqlCipherLocalVaultStore(
        ILocalVaultKeyStore keyStore,
        ILoggingService logger)
        : this(keyStore, logger, LocalVaultOptions.Default)
    {
    }

    public SqlCipherLocalVaultStore(
        ILocalVaultKeyStore keyStore,
        ILoggingService logger,
        LocalVaultOptions options)
    {
        _keyStore = keyStore;
        _logger = logger;
        DatabasePath = options.DatabasePath;
    }

    public string DatabasePath { get; }

    public async Task<LocalVaultItem> PutAsync(
        string scope,
        string path,
        string key,
        byte[] value,
        string contentType,
        Dictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedScope = LocalVaultNames.NormalizeScope(scope);
        var secretPath = new SecretPath(path, key);
        var id = LocalVaultItem.CreateId(normalizedScope, secretPath.FolderPath, secretPath.Key);
        var now = DateTime.UtcNow;

        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            var store = await GetStoreAsync(cancellationToken);
            var existing = await store.Get<LocalVaultItem>(id);
            var item = new LocalVaultItem
            {
                Id = id,
                Scope = normalizedScope,
                Path = secretPath.FolderPath,
                Key = secretPath.Key,
                ContentType = string.IsNullOrWhiteSpace(contentType)
                    ? LocalVaultContentTypes.Binary
                    : contentType,
                Value = value,
                Metadata = metadata is null
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string>(metadata, StringComparer.Ordinal),
                CreatedAt = existing?.CreatedAt ?? now,
                UpdatedAt = now
            };

            if (existing is null)
                await store.Insert(item);
            else
                await store.Update(item);

            return item;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<LocalVaultItem?> GetAsync(
        string scope,
        string path,
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = LocalVaultItem.CreateId(scope, path, key);
        var store = await GetStoreAsync(cancellationToken);
        return await store.Get<LocalVaultItem>(id);
    }

    public async Task<bool> RemoveAsync(
        string scope,
        string path,
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = LocalVaultItem.CreateId(scope, path, key);

        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            var store = await GetStoreAsync(cancellationToken);
            var existing = await store.Get<LocalVaultItem>(id);
            if (existing is null)
                return false;

            await store.Remove<LocalVaultItem>(id);
            return true;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<bool> ExistsAsync(
        string scope,
        string path,
        string key,
        CancellationToken cancellationToken = default)
    {
        return await GetAsync(scope, path, key, cancellationToken) is not null;
    }

    public async Task<IReadOnlyList<LocalVaultItem>> ListAsync(
        string scope,
        string? path = null,
        string? keyPrefix = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedScope = LocalVaultNames.NormalizeScope(scope);
        var normalizedPath = path is null ? null : SecretPath.NormalizeFolderPath(path);
        var store = await GetStoreAsync(cancellationToken);
        var items = await store.Query<LocalVaultItem>().ToList();

        return items
            .Where(x => string.Equals(x.Scope, normalizedScope, StringComparison.Ordinal))
            .Where(x => normalizedPath is null || string.Equals(x.Path, normalizedPath, StringComparison.Ordinal))
            .Where(x => string.IsNullOrEmpty(keyPrefix) || x.Key.StartsWith(keyPrefix, StringComparison.Ordinal))
            .OrderBy(x => x.Path, StringComparer.Ordinal)
            .ThenBy(x => x.Key, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
    }

    private async Task<IDocumentStore> GetStoreAsync(CancellationToken cancellationToken)
    {
        if (_store is not null)
            return _store;

        await _openLock.WaitAsync(cancellationToken);
        try
        {
            if (_store is not null)
                return _store;

            var directory = Path.GetDirectoryName(DatabasePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var key = await _keyStore.GetOrCreateKeyAsync(cancellationToken);
            _store = new DocumentStore(new DocumentStoreOptions
            {
                DatabaseProvider = new SqlCipherDatabaseProvider(DatabasePath, key)
            });

            _logger.LogInformation($"Opened local vault database at {DatabasePath}");
            return _store;
        }
        finally
        {
            _openLock.Release();
        }
    }
}
