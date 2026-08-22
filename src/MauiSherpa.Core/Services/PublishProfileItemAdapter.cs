using System.Text.Json;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public sealed class PublishProfileItemAdapter : ISecretItemAdapter
{
    internal const string KeyPrefix = "sherpa-publish-profiles/";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ISecretsProviderRegistry _providerRegistry;

    public PublishProfileItemAdapter(ISecretsProviderRegistry providerRegistry)
    {
        _providerRegistry = providerRegistry;
    }

    public SecretItemKind Kind => SecretItemKind.PublishProfile;
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
        var keys = await provider.ListSecretsAsync(KeyPrefix, cancellationToken);
        foreach (var key in keys)
        {
            if (!SecretItemAdapterHelper.TryGetRelativeKey(key, KeyPrefix, out var fallbackId))
                continue;

            var profile = DeserializeProfile(await provider.GetSecretAsync(key, cancellationToken));
            var profileId = string.IsNullOrWhiteSpace(profile?.Id) ? fallbackId : profile.Id;
            var displayName = string.IsNullOrWhiteSpace(profile?.Name) ? profileId : profile.Name;
            items[profileId] = new SecretItemRef(Kind, profileId, displayName);
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
            if (SecretItemAdapterHelper.TryGetRelativeKey(key, KeyPrefix, out var itemId))
                itemIds.Add(itemId);
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

        var key = KeyPrefix + item.Id;
        var artifact = await SecretItemAdapterHelper.ReadArtifactAsync(
            provider,
            key,
            cancellationToken);
        if (artifact is null)
            return null;

        var profile = DeserializeProfile(artifact.Value);
        var profileId = string.IsNullOrWhiteSpace(profile?.Id) ? item.Id : profile.Id;
        var displayName = string.IsNullOrWhiteSpace(profile?.Name)
            ? item.DisplayName
            : profile.Name;
        return SecretItemAdapterHelper.CreatePayload(
            new SecretItemRef(Kind, profileId, displayName),
            SecretItemAdapterHelper.GetUtcRevision(profile?.UpdatedAt ?? default),
            new[] { artifact });
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
                new[] { KeyPrefix + item.Id },
                cancellationToken);
    }

    private static PublishProfile? DeserializeProfile(byte[]? bytes)
    {
        if (bytes is null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<PublishProfile>(bytes, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
