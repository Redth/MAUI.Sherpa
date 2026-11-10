using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public sealed class CertificateItemAdapter : ISecretItemAdapter
{
    private const string KeyPrefix = CertificateSecretKeys.Prefix;
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
        cancellationToken.ThrowIfCancellationRequested();
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
        var provider = await SecretItemAdapterHelper.GetProviderAsync(_providerRegistry, providerId)
            ?? throw new InvalidOperationException($"Secrets provider '{providerId}' is unavailable.");

        var items = new Dictionary<string, SecretItemRef>(StringComparer.Ordinal);
        var keys = await provider.ListSecretsStrictAsync(KeyPrefix, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var key in keys)
        {
            if (!CertificateSecretKeys.TryExtractSerial(key, CertificateSecretKeys.P12Suffix, out var serial))
                continue;

            var itemId = SecretItemAdapterHelper.SanitizeSerialNumber(serial);
            if (itemId.Length > 0)
                items[itemId] = new SecretItemRef(Kind, itemId, serial);
        }

        foreach (var key in keys)
        {
            if (!CertificateSecretKeys.TryExtractSerial(key, CertificateSecretKeys.MetadataSuffix, out var serial))
                continue;

            var itemId = SecretItemAdapterHelper.SanitizeSerialNumber(serial);
            if (!items.TryGetValue(itemId, out var item))
                continue;

            var metadata = DeserializeMetadata(await provider.GetSecretAsync(key, cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            if (metadata is not null &&
                SecretItemAdapterHelper.SanitizeSerialNumber(metadata.SerialNumber ?? string.Empty) == itemId &&
                !string.IsNullOrWhiteSpace(metadata.CommonName))
            {
                items[itemId] = item with { DisplayName = metadata.CommonName };
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

        var keys = await provider.ListSecretsStrictAsync(
            prefix: null,
            cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return ExtractProviderItemIds(keys);
    }

    public IReadOnlySet<string> ExtractProviderItemIds(IReadOnlyList<string> providerKeys)
    {
        var itemIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in providerKeys)
        {
            if (!CertificateSecretKeys.TryExtractSerial(
                    key,
                    CertificateSecretKeys.P12Suffix,
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
        cancellationToken.ThrowIfCancellationRequested();
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

        var p12Key = GetKey(item.Id, "P12");
        var p12Artifact = await SecretItemAdapterHelper.ReadArtifactAsync(provider, p12Key, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (p12Artifact is null)
        {
            var keys = await provider.ListSecretsStrictAsync(KeyPrefix, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var key in keys)
            {
                if (!CertificateSecretKeys.TryExtractSerial(key, CertificateSecretKeys.P12Suffix, out var serial) ||
                    SecretItemAdapterHelper.SanitizeSerialNumber(serial) != item.Id)
                {
                    continue;
                }

                p12Artifact = await SecretItemAdapterHelper.ReadArtifactAsync(provider, key, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (p12Artifact is not null)
                {
                    p12Key = key;
                    break;
                }
            }
        }

        if (p12Artifact is null)
            return null;

        var artifacts = new List<SecretArtifact> { p12Artifact with { Key = GetKey(item.Id, "P12") } };
        var passwordArtifact = await SecretItemAdapterHelper.ReadArtifactAsync(
            provider,
            p12Key[..^3] + "PWD",
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (passwordArtifact is not null)
            artifacts.Add(passwordArtifact with { Key = GetKey(item.Id, "PWD") });

        var metadataKey = GetKey(item.Id, "META");
        var metadataArtifact = await SecretItemAdapterHelper.ReadArtifactAsync(
            provider,
            p12Key[..^3] + "META",
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var metadata = DeserializeMetadata(metadataArtifact?.Value);
        if (metadata is null ||
            SecretItemAdapterHelper.SanitizeSerialNumber(metadata.SerialNumber ?? string.Empty) != item.Id)
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
        else
        {
            metadataArtifact = metadataArtifact! with { Key = metadataKey };
        }
        artifacts.Add(metadataArtifact);

        return SecretItemAdapterHelper.CreatePayload(
            new SecretItemRef(
                Kind,
                item.Id,
                string.IsNullOrWhiteSpace(metadata.CommonName)
                    ? item.DisplayName
                    : metadata.CommonName),
            SecretItemAdapterHelper.GetUtcRevision(metadata.CreatedAt),
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
        var itemId = SecretItemAdapterHelper.SanitizeSerialNumber(item.Id);
        if (item.Kind != Kind || itemId.Length == 0)
            return false;

        var provider = await SecretItemAdapterHelper.GetProviderAsync(_providerRegistry, providerId);
        if (provider is null)
            return false;

        var keys = await provider.ListSecretsStrictAsync(KeyPrefix, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var matchingKeys = keys.Where(key =>
            (CertificateSecretKeys.TryExtractSerial(key, CertificateSecretKeys.P12Suffix, out var serial) ||
             CertificateSecretKeys.TryExtractSerial(key, CertificateSecretKeys.PasswordSuffix, out serial) ||
             CertificateSecretKeys.TryExtractSerial(key, CertificateSecretKeys.MetadataSuffix, out serial)) &&
            SecretItemAdapterHelper.SanitizeSerialNumber(serial) == itemId);

        var succeeded = true;
        var errors = new List<Exception>();
        foreach (var key in matchingKeys.Distinct(StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal))
        {
            try
            {
                var deleted = await provider.DeleteSecretAsync(key, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                succeeded &= deleted;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                errors.Add(ex);
            }
        }

        if (errors.Count > 0)
            throw new AggregateException("One or more certificate artifacts could not be deleted.", errors);

        return succeeded;
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
