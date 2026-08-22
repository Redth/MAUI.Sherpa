using System.Text;
using System.Text.Json;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public sealed class ManagedSecretItemAdapter : ISecretItemAdapter
{
    public const string FolderItemIdPrefix = "$folder$:";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ISecretsProviderRegistry _providerRegistry;

    public ManagedSecretItemAdapter(ISecretsProviderRegistry providerRegistry)
    {
        _providerRegistry = providerRegistry;
    }

    public SecretItemKind Kind => SecretItemKind.ManagedSecret;
    public bool IsProviderOwned => true;

    public Task<IReadOnlyList<SecretItemRef>> ListLocalItemsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SecretItemRef>>(Array.Empty<SecretItemRef>());

    public async Task<IReadOnlyList<SecretItemRef>> ListProviderItemsAsync(
        string providerId,
        CancellationToken cancellationToken = default)
    {
        var provider = await SecretItemAdapterHelper.GetProviderAsync(_providerRegistry, providerId);
        if (provider is null)
            return Array.Empty<SecretItemRef>();

        var items = new Dictionary<string, SecretItemRef>(StringComparer.Ordinal);
        var valueKeys = await provider.ListSecretsAsync(
            IManagedSecretsService.SecretPrefix,
            cancellationToken);
        foreach (var key in valueKeys)
        {
            if (SecretItemAdapterHelper.KeyMatchesPrefix(key, IManagedSecretsService.MetadataPrefix) ||
                !SecretItemAdapterHelper.TryGetRelativeKey(
                    key,
                    IManagedSecretsService.SecretPrefix,
                    out var itemId) ||
                itemId.EndsWith(ManagedSecretsService.FolderPlaceholderKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            items[itemId] = CreateItem(itemId);
        }

        var metadataKeys = await provider.ListSecretsAsync(
            IManagedSecretsService.MetadataPrefix,
            cancellationToken);
        foreach (var key in metadataKeys)
        {
            var metadataBytes = await provider.GetSecretAsync(key, cancellationToken);
            var metadata = DeserializeMetadata(metadataBytes);
            if (metadata is not null)
            {
                items[metadata.Key] = CreateItem(metadata.Key);
            }
            else if (SecretItemAdapterHelper.TryGetRelativeKey(
                key,
                IManagedSecretsService.MetadataPrefix,
                out var itemId))
            {
                items.TryAdd(itemId, CreateItem(itemId));
            }
        }

        return items.Values
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlySet<string>> ListProviderItemIdsAsync(
        string providerId,
        CancellationToken cancellationToken = default)
    {
        var itemIds = new HashSet<string>(StringComparer.Ordinal);
        var provider = await SecretItemAdapterHelper.GetProviderAsync(_providerRegistry, providerId);
        if (provider is null)
            return itemIds;

        // Providers currently enumerate their complete key collection before applying a prefix,
        // so take one snapshot and partition all managed-secret key families in memory.
        var keys = await provider.ListSecretsAsync(
            prefix: null,
            cancellationToken: cancellationToken);
        return ExtractProviderItemIds(keys);
    }

    public IReadOnlySet<string> ExtractProviderItemIds(IReadOnlyList<string> providerKeys)
    {
        var itemIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in providerKeys)
        {
            if (SecretItemAdapterHelper.TryGetRelativeKey(
                    key,
                    IManagedSecretsService.FolderPrefix,
                    out var folderPath))
            {
                itemIds.Add(CreateFolderItem(folderPath).Id);
            }
            else if (SecretItemAdapterHelper.TryGetRelativeKey(
                    key,
                    IManagedSecretsService.MetadataPrefix,
                    out var metadataItemId) &&
                !IsFolderPlaceholderItemId(metadataItemId))
            {
                itemIds.Add(metadataItemId);
            }
            else if (SecretItemAdapterHelper.TryGetRelativeKey(
                    key,
                    IManagedSecretsService.SecretPrefix,
                    out var itemId) &&
                !IsFolderPlaceholderItemId(itemId))
            {
                itemIds.Add(itemId);
            }
        }

        return itemIds;
    }

    public Task<SecretItemPayload?> ReadLocalAsync(
        SecretItemRef item,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<SecretItemPayload?>(null);

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

        if (TryGetFolderPath(item.Id, out var folderPath))
        {
            var folderMetadata = await SecretItemAdapterHelper.ReadArtifactAsync(
                provider,
                GetFolderMetadataKey(folderPath),
                cancellationToken);
            if (folderMetadata is null)
                return null;

            var artifacts = new List<SecretArtifact> { folderMetadata };
            var placeholder = await SecretItemAdapterHelper.ReadArtifactAsync(
                provider,
                GetFolderPlaceholderKey(folderPath),
                cancellationToken);
            if (placeholder is not null)
                artifacts.Add(placeholder);

            return SecretItemAdapterHelper.CreatePayload(item, 0, artifacts);
        }

        var valueKey = IManagedSecretsService.SecretPrefix + item.Id;
        var valueArtifact = await SecretItemAdapterHelper.ReadArtifactAsync(
            provider,
            valueKey,
            cancellationToken);
        if (valueArtifact is null)
            return null;

        var metadataKey = IManagedSecretsService.MetadataPrefix + item.Id;
        var metadataArtifact = await SecretItemAdapterHelper.ReadArtifactAsync(
            provider,
            metadataKey,
            cancellationToken);
        var metadata = DeserializeMetadata(metadataArtifact?.Value);
        metadata ??= new ManagedSecret(
            item.Id,
            ManagedSecretType.String,
            null,
            null,
            DateTime.UnixEpoch,
            DateTime.UnixEpoch);
        metadataArtifact ??= new SecretArtifact(
            metadataKey,
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(metadata, JsonOptions)));

        return SecretItemAdapterHelper.CreatePayload(
            new SecretItemRef(Kind, metadata.Key, GetDisplayName(metadata.Key)),
            SecretItemAdapterHelper.GetUtcRevision(metadata.UpdatedAt),
            new[] { valueArtifact, metadataArtifact });
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
        if (provider is null)
            return false;

        if (TryGetFolderPath(item.Id, out var folderPath))
        {
            return await SecretItemAdapterHelper.DeleteArtifactsAsync(
                provider,
                new[]
                {
                    GetFolderMetadataKey(folderPath),
                    GetFolderPlaceholderKey(folderPath)
                },
                cancellationToken);
        }

        return provider is not null &&
            await SecretItemAdapterHelper.DeleteArtifactsAsync(
                provider,
                new[]
                {
                    IManagedSecretsService.SecretPrefix + item.Id,
                    IManagedSecretsService.MetadataPrefix + item.Id
                },
                cancellationToken);
    }

    public static SecretItemRef CreateFolderItem(string folderPath)
    {
        var normalized = SecretPath.NormalizeFolderPath(folderPath);
        return new SecretItemRef(
            SecretItemKind.ManagedSecret,
            FolderItemIdPrefix + normalized,
            normalized);
    }

    private static bool TryGetFolderPath(string itemId, out string folderPath)
    {
        folderPath = "/";
        if (!itemId.StartsWith(FolderItemIdPrefix, StringComparison.Ordinal))
            return false;

        folderPath = SecretPath.NormalizeFolderPath(itemId[FolderItemIdPrefix.Length..]);
        return folderPath != "/";
    }

    private static string GetFolderMetadataKey(string folderPath) =>
        IManagedSecretsService.FolderPrefix +
        SecretPath.NormalizeFolderPath(folderPath).TrimStart('/');

    private static string GetFolderPlaceholderKey(string folderPath) =>
        IManagedSecretsService.SecretPrefix +
        new SecretPath(folderPath, ManagedSecretsService.FolderPlaceholderKey).ToFlatKey();

    private static bool IsFolderPlaceholderItemId(string itemId) =>
        (itemId.Length == ManagedSecretsService.FolderPlaceholderKey.Length &&
            SecretItemAdapterHelper.KeyMatchesPrefix(
                itemId,
                ManagedSecretsService.FolderPlaceholderKey)) ||
        SecretItemAdapterHelper.TryExtractAffixedId(
            itemId,
            string.Empty,
            "/" + ManagedSecretsService.FolderPlaceholderKey,
            out _);

    private static SecretItemRef CreateItem(string itemId) =>
        new(SecretItemKind.ManagedSecret, itemId, GetDisplayName(itemId));

    private static string GetDisplayName(string key)
    {
        var separator = key.LastIndexOf('/');
        return separator >= 0 ? key[(separator + 1)..] : key;
    }

    private static ManagedSecret? DeserializeMetadata(byte[]? bytes)
    {
        if (bytes is null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<ManagedSecret>(bytes, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
