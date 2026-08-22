using System.Security.Cryptography;
using System.Text;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

internal static class SecretItemAdapterHelper
{
    public static string ComputeContentHash(IEnumerable<SecretArtifact> artifacts)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (var artifact in artifacts.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(artifact.Key));
            hash.AppendData(artifact.Value);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public static SecretItemPayload CreatePayload(
        SecretItemRef item,
        long revision,
        IEnumerable<SecretArtifact> artifacts)
    {
        var artifactList = artifacts
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToList();
        return new SecretItemPayload(item, revision, ComputeContentHash(artifactList), artifactList);
    }

    public static async Task<ICloudSecretsProvider?> GetProviderAsync(
        ISecretsProviderRegistry registry,
        string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            return null;

        return await registry.GetProviderAsync(providerId);
    }

    public static async Task<SecretArtifact?> ReadArtifactAsync(
        ICloudSecretsProvider provider,
        string key,
        CancellationToken cancellationToken)
    {
        var value = await provider.GetSecretAsync(key, cancellationToken);
        if (value is null)
            return null;

        var metadata = await provider.GetSecretMetadataAsync(key, cancellationToken);
        return new SecretArtifact(key, value, metadata);
    }

    public static async Task<bool> WriteArtifactsAsync(
        ICloudSecretsProvider provider,
        IReadOnlyList<SecretArtifact> artifacts,
        CancellationToken cancellationToken)
    {
        var ordered = artifacts
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToList();
        var snapshots = new Dictionary<string, ArtifactSnapshot>(StringComparer.Ordinal);

        foreach (var artifact in ordered)
        {
            var exists = await provider.SecretExistsAsync(artifact.Key, cancellationToken);
            var value = exists
                ? await provider.GetSecretAsync(artifact.Key, cancellationToken)
                : null;
            var metadata = value is null
                ? null
                : await provider.GetSecretMetadataAsync(artifact.Key, cancellationToken);
            snapshots[artifact.Key] = new ArtifactSnapshot(value, metadata);
        }

        var attemptedKeys = new List<string>();
        foreach (var artifact in ordered)
        {
            attemptedKeys.Add(artifact.Key);
            bool stored;
            try
            {
                stored = await provider.StoreSecretAsync(
                    artifact.Key,
                    artifact.Value,
                    artifact.Metadata,
                    cancellationToken);
            }
            catch (Exception writeError)
            {
                var cleanupErrors = await RollbackAsync(
                    provider,
                    attemptedKeys,
                    snapshots,
                    CancellationToken.None);
                if (cleanupErrors.Count > 0)
                    throw new AggregateException(
                        "Artifact write failed and rollback was incomplete.",
                        new[] { writeError }.Concat(cleanupErrors));

                throw;
            }

            if (stored)
                continue;

            var errors = await RollbackAsync(
                provider,
                attemptedKeys,
                snapshots,
                CancellationToken.None);
            if (errors.Count > 0)
                throw new AggregateException("Artifact write failed and rollback was incomplete.", errors);

            return false;
        }

        return true;
    }

    public static async Task<bool> DeleteArtifactsAsync(
        ICloudSecretsProvider provider,
        IEnumerable<string> keys,
        CancellationToken cancellationToken)
    {
        var succeeded = true;
        var errors = new List<Exception>();

        foreach (var key in keys.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
        {
            try
            {
                if (!await provider.SecretExistsAsync(key, cancellationToken))
                    continue;

                if (!await provider.DeleteSecretAsync(key, cancellationToken))
                    succeeded = false;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        }

        if (errors.Count > 0)
            throw new AggregateException("One or more artifacts could not be deleted.", errors);

        return succeeded;
    }

    public static bool KeyMatchesPrefix(string key, string prefix) =>
        NormalizeStorageKey(key).StartsWith(NormalizeStorageKey(prefix), StringComparison.Ordinal);

    public static bool StorageKeysEqual(string left, string right) =>
        string.Equals(
            NormalizeStorageKey(left),
            NormalizeStorageKey(right),
            StringComparison.Ordinal);

    public static bool TryGetRelativeKey(string key, string prefix, out string relativeKey)
    {
        relativeKey = string.Empty;
        if (!KeyMatchesPrefix(key, prefix) || key.Length < prefix.Length)
            return false;

        relativeKey = key[prefix.Length..];
        return relativeKey.Length > 0;
    }

    public static bool TryExtractAffixedId(
        string key,
        string prefix,
        string suffix,
        out string id)
    {
        id = string.Empty;
        var normalizedKey = NormalizeStorageKey(key);
        var normalizedPrefix = NormalizeStorageKey(prefix);
        var normalizedSuffix = NormalizeStorageKey(suffix);

        if (!normalizedKey.StartsWith(normalizedPrefix, StringComparison.Ordinal) ||
            !normalizedKey.EndsWith(normalizedSuffix, StringComparison.Ordinal) ||
            key.Length <= prefix.Length + suffix.Length)
        {
            return false;
        }

        id = key.Substring(prefix.Length, key.Length - prefix.Length - suffix.Length);
        return id.Length > 0;
    }

    public static string SanitizeSerialNumber(string serialNumber)
    {
        var sanitized = new string(serialNumber
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
        return sanitized.TrimStart('0');
    }

    public static long GetUtcRevision(DateTime value)
    {
        if (value == default)
            return 0;

        return value.Kind == DateTimeKind.Utc
            ? value.Ticks
            : value.ToUniversalTime().Ticks;
    }

    private static string NormalizeStorageKey(string key)
    {
        var builder = new StringBuilder(key.Length);
        foreach (var character in key)
        {
            builder.Append(char.IsLetterOrDigit(character)
                ? char.ToUpperInvariant(character)
                : '_');
        }

        return builder.ToString();
    }

    private static async Task<List<Exception>> RollbackAsync(
        ICloudSecretsProvider provider,
        IEnumerable<string> attemptedKeys,
        IReadOnlyDictionary<string, ArtifactSnapshot> snapshots,
        CancellationToken cancellationToken)
    {
        var errors = new List<Exception>();
        foreach (var key in attemptedKeys.Reverse())
        {
            try
            {
                var snapshot = snapshots[key];
                if (snapshot.Value is null)
                {
                    await provider.DeleteSecretAsync(key, cancellationToken);
                }
                else
                {
                    await provider.StoreSecretAsync(
                        key,
                        snapshot.Value,
                        snapshot.Metadata,
                        cancellationToken);
                }
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        }

        return errors;
    }

    private sealed record ArtifactSnapshot(
        byte[]? Value,
        Dictionary<string, string>? Metadata);
}
