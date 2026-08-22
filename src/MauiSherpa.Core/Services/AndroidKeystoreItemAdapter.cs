using System.Text;
using System.Text.Json;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public sealed class AndroidKeystoreItemAdapter : ISecretItemAdapter
{
    private const string KeyPrefix = "KEYSTORE_";
    private const string PasswordStoragePrefix = "android_keystore_pwd_";
    private readonly ISecretsProviderRegistry _providerRegistry;
    private readonly IKeystoreService _keystoreService;
    private readonly ISecureStorageService _secureStorage;

    public AndroidKeystoreItemAdapter(
        ISecretsProviderRegistry providerRegistry,
        IKeystoreService keystoreService,
        ISecureStorageService secureStorage)
    {
        _providerRegistry = providerRegistry;
        _keystoreService = keystoreService;
        _secureStorage = secureStorage;
    }

    public SecretItemKind Kind => SecretItemKind.AndroidKeystore;
    public bool IsProviderOwned => false;

    public async Task<IReadOnlyList<SecretItemRef>> ListLocalItemsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var keystores = await _keystoreService.ListKeystoresAsync();
        return keystores
            .Where(x => !string.IsNullOrWhiteSpace(x.Alias))
            .GroupBy(x => x.Alias, StringComparer.OrdinalIgnoreCase)
            .Select(x => new SecretItemRef(Kind, x.Key, x.First().Alias))
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

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

        var keystores = await _keystoreService.ListKeystoresAsync();
        var keystore = keystores.FirstOrDefault(x =>
            string.Equals(x.Alias, item.Id, StringComparison.OrdinalIgnoreCase));
        if (keystore is null || !File.Exists(keystore.FilePath))
            return null;

        var password = await _secureStorage.GetAsync(PasswordStoragePrefix + keystore.Id);
        if (password is null)
            return null;

        var fileBytes = await File.ReadAllBytesAsync(keystore.FilePath, cancellationToken);
        var lastWriteTime = File.GetLastWriteTimeUtc(keystore.FilePath);
        var metadata = new KeystoreArtifactMetadata(
            keystore.Alias,
            keystore.KeystoreType,
            keystore.CreatedDate,
            keystore.Notes,
            lastWriteTime);
        var artifacts = new[]
        {
            new SecretArtifact(GetKey(keystore.Alias, "JKS"), fileBytes),
            new SecretArtifact(
                GetKey(keystore.Alias, "PWD"),
                Encoding.UTF8.GetBytes(password)),
            new SecretArtifact(
                GetKey(keystore.Alias, "META"),
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(metadata)))
        };

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
        var passwordArtifact = await SecretItemAdapterHelper.ReadArtifactAsync(
            provider,
            GetKey(item.Id, "PWD"),
            cancellationToken);
        if (passwordArtifact is not null)
            artifacts.Add(passwordArtifact);

        var metadataKey = GetKey(item.Id, "META");
        var metadataArtifact = await SecretItemAdapterHelper.ReadArtifactAsync(
            provider,
            metadataKey,
            cancellationToken);
        var metadata = DeserializeMetadata(metadataArtifact?.Value);
        if (metadataArtifact is null)
        {
            metadata = new KeystoreArtifactMetadata(
                item.Id,
                "PKCS12",
                DateTime.UnixEpoch,
                null,
                DateTime.UnixEpoch);
            metadataArtifact = new SecretArtifact(
                metadataKey,
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(metadata)));
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
                new[]
                {
                    GetKey(item.Id, "JKS"),
                    GetKey(item.Id, "PWD"),
                    GetKey(item.Id, "META")
                },
                cancellationToken);
    }

    private static string GetKey(string alias, string suffix) =>
        $"{KeyPrefix}{alias}_{suffix}";

    private static KeystoreArtifactMetadata? DeserializeMetadata(byte[]? bytes)
    {
        if (bytes is null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<KeystoreArtifactMetadata>(
                bytes,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record KeystoreArtifactMetadata(
        string Alias,
        string KeystoreType,
        DateTime CreatedDate,
        string? Notes,
        DateTime UploadedAt);
}
