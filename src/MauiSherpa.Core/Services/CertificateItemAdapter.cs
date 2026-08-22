using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public sealed class CertificateItemAdapter : ISecretItemAdapter
{
    private const string KeyPrefix = "CERT_";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly ISecretsProviderRegistry _providerRegistry;
    private readonly ILocalCertificateService _localCertificateService;

    public CertificateItemAdapter(
        ISecretsProviderRegistry providerRegistry,
        ILocalCertificateService localCertificateService)
    {
        _providerRegistry = providerRegistry;
        _localCertificateService = localCertificateService;
    }

    public SecretItemKind Kind => SecretItemKind.Certificate;
    public bool IsProviderOwned => false;

    public async Task<IReadOnlyList<SecretItemRef>> ListLocalItemsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var identities = await _localCertificateService.GetSigningIdentitiesAsync();
        return identities
            .Where(x => x.IsValid && !string.IsNullOrWhiteSpace(x.SerialNumber))
            .Select(x => (
                Identity: x,
                Id: SecretItemAdapterHelper.SanitizeSerialNumber(x.SerialNumber!)))
            .Where(x => x.Id.Length > 0)
            .GroupBy(x => x.Id, StringComparer.Ordinal)
            .Select(x => new SecretItemRef(Kind, x.Key, x.First().Identity.CommonName))
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
            if (!SecretItemAdapterHelper.TryExtractAffixedId(key, KeyPrefix, "_P12", out var serial))
                continue;

            var itemId = SecretItemAdapterHelper.SanitizeSerialNumber(serial);
            if (itemId.Length > 0)
                items[itemId] = new SecretItemRef(Kind, itemId, serial);
        }

        foreach (var key in keys)
        {
            if (!SecretItemAdapterHelper.TryExtractAffixedId(key, KeyPrefix, "_META", out _))
                continue;

            var metadata = DeserializeMetadata(await provider.GetSecretAsync(key, cancellationToken));
            if (metadata is null)
                continue;

            var itemId = SecretItemAdapterHelper.SanitizeSerialNumber(metadata.SerialNumber);
            if (itemId.Length > 0)
                items[itemId] = new SecretItemRef(Kind, itemId, metadata.CommonName);
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
            if (!SecretItemAdapterHelper.TryExtractAffixedId(
                    key,
                    KeyPrefix,
                    "_P12",
                    out var serial))
            {
                continue;
            }

            var itemId = SecretItemAdapterHelper.SanitizeSerialNumber(serial);
            if (itemId.Length > 0)
                itemIds.Add(itemId);
        }

        return itemIds;
    }

    public async Task<SecretItemPayload?> ReadLocalAsync(
        SecretItemRef item,
        CancellationToken cancellationToken = default)
    {
        if (item.Kind != Kind)
            return null;

        var identities = await _localCertificateService.GetSigningIdentitiesAsync();
        var identity = identities.FirstOrDefault(x =>
            x.IsValid &&
            SecretItemAdapterHelper.SanitizeSerialNumber(x.SerialNumber ?? string.Empty) == item.Id);
        if (identity is null || string.IsNullOrWhiteSpace(identity.SerialNumber))
            return null;

        cancellationToken.ThrowIfCancellationRequested();
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
        var p12 = await _localCertificateService.ExportP12Async(identity.Identity, password);
        cancellationToken.ThrowIfCancellationRequested();

        var metadata = new CertificateSecretMetadata(
            string.Empty,
            identity.SerialNumber,
            identity.CommonName,
            GetCertificateType(identity.CommonName),
            identity.ExpirationDate ?? DateTime.MinValue,
            Environment.MachineName,
            DateTime.UnixEpoch);
        var artifacts = new[]
        {
            new SecretArtifact(GetKey(item.Id, "P12"), p12),
            new SecretArtifact(GetKey(item.Id, "PWD"), Encoding.UTF8.GetBytes(password)),
            new SecretArtifact(
                GetKey(item.Id, "META"),
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(metadata)))
        };

        return SecretItemAdapterHelper.CreatePayload(
            new SecretItemRef(Kind, item.Id, identity.CommonName),
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

        var p12Artifact = await SecretItemAdapterHelper.ReadArtifactAsync(
            provider,
            GetKey(item.Id, "P12"),
            cancellationToken);
        if (p12Artifact is null)
            return null;

        var artifacts = new List<SecretArtifact> { p12Artifact };
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
            metadata = new CertificateSecretMetadata(
                string.Empty,
                item.Id,
                item.DisplayName,
                string.Empty,
                DateTime.MinValue,
                string.Empty,
                DateTime.UnixEpoch);
            metadataArtifact = new SecretArtifact(
                metadataKey,
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(metadata)));
        }
        artifacts.Add(metadataArtifact);

        var itemId = SecretItemAdapterHelper.SanitizeSerialNumber(
            metadata?.SerialNumber ?? item.Id);
        return SecretItemAdapterHelper.CreatePayload(
            new SecretItemRef(
                Kind,
                itemId,
                string.IsNullOrWhiteSpace(metadata?.CommonName)
                    ? item.DisplayName
                    : metadata.CommonName),
            SecretItemAdapterHelper.GetUtcRevision(metadata?.CreatedAt ?? default),
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
                    GetKey(item.Id, "P12"),
                    GetKey(item.Id, "PWD"),
                    GetKey(item.Id, "META")
                },
                cancellationToken);
    }

    private static string GetKey(string serialNumber, string suffix) =>
        $"{KeyPrefix}{SecretItemAdapterHelper.SanitizeSerialNumber(serialNumber)}_{suffix}";

    private static CertificateSecretMetadata? DeserializeMetadata(byte[]? bytes)
    {
        if (bytes is null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<CertificateSecretMetadata>(bytes, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string GetCertificateType(string commonName)
    {
        var separator = commonName.IndexOf(':');
        return separator > 0 ? commonName[..separator].Trim() : string.Empty;
    }
}
