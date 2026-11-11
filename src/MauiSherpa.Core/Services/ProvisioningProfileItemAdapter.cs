using System.Text;
using System.Text.Json;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public sealed class ProvisioningProfileItemAdapter : ISecretItemAdapter
{
    internal const string KeyPrefix = "sherpa-provisioning-profiles/";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ISecretsProviderRegistry _providerRegistry;
    private readonly IAppleConnectService _appleConnectService;
    private readonly IAppleIdentityStateService _identityState;

    public ProvisioningProfileItemAdapter(
        ISecretsProviderRegistry providerRegistry,
        IAppleConnectService appleConnectService,
        IAppleIdentityStateService identityState)
    {
        _providerRegistry = providerRegistry;
        _appleConnectService = appleConnectService;
        _identityState = identityState;
    }

    public SecretItemKind Kind => SecretItemKind.ProvisioningProfile;
    public bool IsProviderOwned => false;

    public async Task<IReadOnlyList<SecretItemRef>> ListLocalItemsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var identityId = _identityState.SelectedIdentity?.Id;
        if (string.IsNullOrWhiteSpace(identityId))
            throw new InvalidOperationException("Select an Apple identity to list provisioning profiles.");

        var profiles = await _appleConnectService.GetProfilesForIdentityAsync(identityId);
        return profiles
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .GroupBy(x => x.Id, StringComparer.Ordinal)
            .Select(x => new SecretItemRef(Kind, x.Key, x.First().Name)
            {
                AppleIdentityId = identityId
            })
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

        var items = new Dictionary<string, SecretItemRef>(StringComparer.Ordinal);
        var keys = await provider.ListSecretsAsync(KeyPrefix, cancellationToken);
        foreach (var key in keys)
        {
            if (TryExtractProfileId(key, "data", out var profileId))
                items[profileId] = new SecretItemRef(Kind, profileId, profileId);
        }

        foreach (var key in keys)
        {
            if (!TryExtractProfileId(key, "meta", out var fallbackId))
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
            if (TryExtractProfileId(key, "data", out var profileId))
                itemIds.Add(profileId);
        }

        return itemIds;
    }

    public async Task<SecretItemPayload?> ReadLocalAsync(
        SecretItemRef item,
        CancellationToken cancellationToken = default)
    {
        if (item.Kind != Kind)
            return null;

        if (string.IsNullOrWhiteSpace(item.AppleIdentityId))
        {
            throw new InvalidOperationException(
                "This profile backup has no Apple identity. Select the correct Apple identity and start the backup again.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var profiles = await _appleConnectService.GetProfilesForIdentityAsync(item.AppleIdentityId);
        var profile = profiles.FirstOrDefault(x =>
            string.Equals(x.Id, item.Id, StringComparison.Ordinal));
        if (profile is null)
            return null;

        cancellationToken.ThrowIfCancellationRequested();
        var data = await _appleConnectService.DownloadProfileForIdentityAsync(profile.Id, item.AppleIdentityId);
        cancellationToken.ThrowIfCancellationRequested();
        if (data.Length == 0)
        {
            throw new InvalidOperationException(
                $"Apple returned no data for '{profile.Name}'. Refresh the profiles list and try the backup again.");
        }

        var artifacts = new[]
        {
            new SecretArtifact(GetDataKey(profile.Id), data),
            new SecretArtifact(
                GetMetadataKey(profile.Id),
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(profile, JsonOptions)))
        };

        return SecretItemAdapterHelper.CreatePayload(
            item with { Id = profile.Id, DisplayName = profile.Name },
            0,
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

        var dataArtifact = await SecretItemAdapterHelper.ReadArtifactAsync(
            provider,
            GetDataKey(item.Id),
            cancellationToken);
        if (dataArtifact is null)
            return null;

        var metadataKey = GetMetadataKey(item.Id);
        var metadataArtifact = await SecretItemAdapterHelper.ReadArtifactAsync(
            provider,
            metadataKey,
            cancellationToken);
        var profile = DeserializeProfile(metadataArtifact?.Value);
        if (metadataArtifact is null)
        {
            profile = new AppleProfile(
                item.Id,
                item.DisplayName,
                string.Empty,
                string.Empty,
                string.Empty,
                DateTime.MinValue,
                null,
                string.Empty);
            metadataArtifact = new SecretArtifact(
                metadataKey,
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(profile, JsonOptions)));
        }

        var profileId = string.IsNullOrWhiteSpace(profile?.Id) ? item.Id : profile.Id;
        var displayName = string.IsNullOrWhiteSpace(profile?.Name)
            ? item.DisplayName
            : profile.Name;
        return SecretItemAdapterHelper.CreatePayload(
            new SecretItemRef(Kind, profileId, displayName),
            0,
            new[] { dataArtifact, metadataArtifact });
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
                new[] { GetDataKey(item.Id), GetMetadataKey(item.Id) },
                cancellationToken);
    }

    private static string GetDataKey(string profileId) =>
        $"{KeyPrefix}{profileId}/data";

    private static string GetMetadataKey(string profileId) =>
        $"{KeyPrefix}{profileId}/meta";

    private static bool TryExtractProfileId(string key, string artifactName, out string profileId)
    {
        profileId = string.Empty;
        if (!SecretItemAdapterHelper.TryGetRelativeKey(key, KeyPrefix, out var relativeKey))
            return false;

        var suffix = "/" + artifactName;
        return SecretItemAdapterHelper.TryExtractAffixedId(
            relativeKey,
            string.Empty,
            suffix,
            out profileId);
    }

    private static AppleProfile? DeserializeProfile(byte[]? bytes)
    {
        if (bytes is null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<AppleProfile>(bytes, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
