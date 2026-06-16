using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public static class CloudSecretsProviderPathExtensions
{
    public static Task<bool> StoreSecretAsync(
        this ICloudSecretsProvider provider,
        SecretPath path,
        byte[] value,
        Dictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        return provider.StoreSecretAsync(path.ToFlatKey(), value, metadata, cancellationToken);
    }

    public static Task<byte[]?> GetSecretAsync(
        this ICloudSecretsProvider provider,
        SecretPath path,
        CancellationToken cancellationToken = default)
    {
        return provider.GetSecretAsync(path.ToFlatKey(), cancellationToken);
    }

    public static Task<Dictionary<string, string>?> GetSecretMetadataAsync(
        this ICloudSecretsProvider provider,
        SecretPath path,
        CancellationToken cancellationToken = default)
    {
        return provider.GetSecretMetadataAsync(path.ToFlatKey(), cancellationToken);
    }

    public static Task<bool> SetSecretMetadataAsync(
        this ICloudSecretsProvider provider,
        SecretPath path,
        Dictionary<string, string> metadata,
        CancellationToken cancellationToken = default)
    {
        return provider.SetSecretMetadataAsync(path.ToFlatKey(), metadata, cancellationToken);
    }

    public static Task<bool> DeleteSecretAsync(
        this ICloudSecretsProvider provider,
        SecretPath path,
        CancellationToken cancellationToken = default)
    {
        return provider.DeleteSecretAsync(path.ToFlatKey(), cancellationToken);
    }

    public static Task<bool> SecretExistsAsync(
        this ICloudSecretsProvider provider,
        SecretPath path,
        CancellationToken cancellationToken = default)
    {
        return provider.SecretExistsAsync(path.ToFlatKey(), cancellationToken);
    }

    public static Task<IReadOnlyList<string>> ListSecretsAsync(
        this ICloudSecretsProvider provider,
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        var normalized = SecretPath.NormalizeFolderPath(folderPath);
        var prefix = normalized == "/" ? null : normalized.TrimStart('/') + "/";
        return provider.ListSecretsAsync(prefix, cancellationToken);
    }
}
