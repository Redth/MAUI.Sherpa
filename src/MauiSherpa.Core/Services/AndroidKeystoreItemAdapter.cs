using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Syncs Android keystores between providers. Keystores live in the Local Vault provider
/// (see <see cref="KeystoreService"/>), so they are provider-owned and the Local Vault copy is
/// the source. File-attached keystores (stored while the Local Vault is off) are the one local
/// source: they are read from their file and saved passwords.
/// </summary>
public sealed class AndroidKeystoreItemAdapter : ISecretItemAdapter
{
    private const string KeyPrefix = KeystoreVaultLayout.KeyPrefix;
    private readonly ISecretsProviderRegistry _providerRegistry;
    private readonly IKeystoreService _keystoreService;

    public AndroidKeystoreItemAdapter(
        ISecretsProviderRegistry providerRegistry,
        IKeystoreService keystoreService)
    {
        _providerRegistry = providerRegistry;
        _keystoreService = keystoreService;
    }

    public SecretItemKind Kind => SecretItemKind.AndroidKeystore;
    public bool IsProviderOwned => true;

    public async Task<IReadOnlyList<SecretItemRef>> ListLocalItemsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return (await GetFileAttachedKeystoresAsync())
            .Select(k => new SecretItemRef(Kind, k.Alias, k.Alias))
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<IReadOnlyList<AndroidKeystore>> GetFileAttachedKeystoresAsync() =>
        (await _keystoreService.ListKeystoresAsync())
            .Where(k => !k.StoredInVault && File.Exists(k.FilePath))
            .ToList();

    public async Task<IReadOnlyList<SecretItemRef>> ListProviderItemsAsync(
        string providerId,
        CancellationToken cancellationToken = default)
    {
        var provider = await SecretItemAdapterHelper.GetProviderAsync(_providerRegistry, providerId);
        if (provider is null)
            return Array.Empty<SecretItemRef>();

        var items = new Dictionary<string, SecretItemRef>(StringComparer.OrdinalIgnoreCase);
        var keys = await provider.ListSecretsAsync(KeyPrefix, cancellationToken);

        foreach (var key in keys)
        {
            if (SecretItemAdapterHelper.TryExtractAffixedId(key, KeyPrefix, "_JKS", out var alias))
                items[alias] = new SecretItemRef(Kind, alias, alias);
        }

        foreach (var key in keys)
        {
            if (!SecretItemAdapterHelper.TryExtractAffixedId(key, KeyPrefix, "_META", out var fallbackAlias))
                continue;

            var metadata = DeserializeMetadata(await provider.GetSecretAsync(key, cancellationToken));
            var alias = string.IsNullOrWhiteSpace(metadata?.Alias) ? fallbackAlias : metadata.Alias;
            items[alias] = new SecretItemRef(Kind, alias, alias);
        }

        return items.Values
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlySet<string>> ListProviderItemIdsAsync(
        string providerId,
        CancellationToken cancellationToken = default)
    {
        var itemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var provider = await SecretItemAdapterHelper.GetProviderAsync(_providerRegistry, providerId);
        if (provider is null)
            return itemIds;

        var keys = await provider.ListSecretsAsync(
            prefix: null,
            cancellationToken: cancellationToken);
        return ExtractProviderItemIds(keys);
    }

    public IReadOnlySet<string> ExtractProviderItemIds(IReadOnlyList<string> providerKeys)
    {
        var itemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in providerKeys)
        {
            if (SecretItemAdapterHelper.TryExtractAffixedId(
                    key,
                    KeyPrefix,
                    "_JKS",
                    out var alias))
            {
                itemIds.Add(alias);
            }
        }

        return itemIds;
    }

    public async Task<SecretItemPayload?> ReadLocalAsync(
        SecretItemRef item,
        CancellationToken cancellationToken = default)
    {
        if (item.Kind != Kind)
            return null;

        var keystore = (await GetFileAttachedKeystoresAsync())
            .FirstOrDefault(k => string.Equals(k.Alias, item.Id, StringComparison.Ordinal));
        if (keystore is null)
            return null;

        var content = await _keystoreService.GetKeystoreContentAsync(keystore.Id);
        if (content is null)
            return null;

        var lastWriteTime = File.GetLastWriteTimeUtc(keystore.FilePath);
        var artifacts = new List<SecretArtifact>
        {
            new(GetKey(keystore.Alias, KeystoreVaultLayout.FileSuffix), content),
            new(GetKey(keystore.Alias, KeystoreVaultLayout.MetadataSuffix), KeystoreVaultLayout.SerializeMetadata(
                new KeystoreVaultMetadata(
                    keystore.Alias,
                    keystore.KeystoreType,
                    keystore.CreatedDate,
                    keystore.Notes,
                    lastWriteTime,
                    keystore.Id,
                    Path.GetFileName(keystore.FilePath))))
        };
        if (await _keystoreService.GetKeystorePasswordAsync(keystore.Id) is { } password)
            artifacts.Add(new(GetKey(keystore.Alias, KeystoreVaultLayout.PasswordSuffix), System.Text.Encoding.UTF8.GetBytes(password)));
        if (await _keystoreService.GetKeyPasswordAsync(keystore.Id) is { } keyPassword)
            artifacts.Add(new(GetKey(keystore.Alias, KeystoreVaultLayout.KeyPasswordSuffix), System.Text.Encoding.UTF8.GetBytes(keyPassword)));

        return SecretItemAdapterHelper.CreatePayload(
            new SecretItemRef(Kind, keystore.Alias, keystore.Alias),
            SecretItemAdapterHelper.GetUtcRevision(lastWriteTime),
            artifacts);
    }

    public async Task<SecretItemPayload?> ReadProviderAsync(
        SecretItemRef item,
        string providerId,
        CancellationToken cancellationToken = default)
    {
        if (item.Kind != Kind)
            return null;

        var provider = await SecretItemAdapterHelper.GetProviderAsync(_providerRegistry, providerId);
        if (provider is null)
            return null;

        var jksArtifact = await SecretItemAdapterHelper.ReadArtifactAsync(
            provider,
            GetKey(item.Id, "JKS"),
            cancellationToken);
        if (jksArtifact is null)
            return null;

        var artifacts = new List<SecretArtifact> { jksArtifact };
        foreach (var suffix in new[] { KeystoreVaultLayout.PasswordSuffix, KeystoreVaultLayout.KeyPasswordSuffix })
        {
            var passwordArtifact = await SecretItemAdapterHelper.ReadArtifactAsync(
                provider,
                GetKey(item.Id, suffix),
                cancellationToken);
            if (passwordArtifact is not null)
                artifacts.Add(passwordArtifact);
        }

        var metadataKey = GetKey(item.Id, "META");
        var metadataArtifact = await SecretItemAdapterHelper.ReadArtifactAsync(
            provider,
            metadataKey,
            cancellationToken);
        var metadata = DeserializeMetadata(metadataArtifact?.Value);
        if (metadataArtifact is null)
        {
            metadata = new KeystoreVaultMetadata(
                item.Id,
                "PKCS12",
                DateTime.UnixEpoch,
                null,
                DateTime.UnixEpoch);
            metadataArtifact = new SecretArtifact(
                metadataKey,
                KeystoreVaultLayout.SerializeMetadata(metadata));
        }
        artifacts.Add(metadataArtifact);

        var alias = string.IsNullOrWhiteSpace(metadata?.Alias) ? item.Id : metadata.Alias;
        return SecretItemAdapterHelper.CreatePayload(
            new SecretItemRef(Kind, alias, alias),
            SecretItemAdapterHelper.GetUtcRevision(metadata?.UploadedAt ?? default),
            artifacts);
    }

    public async Task<bool> WriteProviderAsync(
        SecretItemPayload payload,
        string providerId,
        CancellationToken cancellationToken = default)
    {
        if (payload.Item.Kind != Kind)
            return false;

        var provider = await SecretItemAdapterHelper.GetProviderAsync(_providerRegistry, providerId);
        return provider is not null &&
            await SecretItemAdapterHelper.WriteArtifactsAsync(
                provider,
                payload.Artifacts,
                cancellationToken);
    }

    public async Task<bool> DeleteProviderAsync(
        SecretItemRef item,
        string providerId,
        CancellationToken cancellationToken = default)
    {
        if (item.Kind != Kind)
            return false;

        var provider = await SecretItemAdapterHelper.GetProviderAsync(_providerRegistry, providerId);
        return provider is not null &&
            await SecretItemAdapterHelper.DeleteArtifactsAsync(
                provider,
                KeystoreVaultLayout.Suffixes.Select(suffix => GetKey(item.Id, suffix)).ToArray(),
                cancellationToken);
    }

    private static string GetKey(string alias, string suffix) =>
        KeystoreVaultLayout.GetKey(alias, suffix);

    private static KeystoreVaultMetadata? DeserializeMetadata(byte[]? bytes) =>
        KeystoreVaultLayout.DeserializeMetadata(bytes);
}
