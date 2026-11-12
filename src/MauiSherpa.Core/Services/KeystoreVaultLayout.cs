using System.Text.Json;

namespace MauiSherpa.Core.Services;

/// <summary>
/// How an Android keystore is laid out in a secrets provider. The Local Vault copy is the
/// keystore itself; copies in other providers use the same keys so they can be synced.
/// </summary>
internal static class KeystoreVaultLayout
{
    public const string KeyPrefix = "KEYSTORE_";
    public const string FileSuffix = "JKS";
    public const string PasswordSuffix = "PWD";
    public const string KeyPasswordSuffix = "KEYPWD";
    public const string MetadataSuffix = "META";

    public static readonly string[] Suffixes = { FileSuffix, PasswordSuffix, KeyPasswordSuffix, MetadataSuffix };

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    public static string GetKey(string alias, string suffix) => $"{KeyPrefix}{alias}_{suffix}";

    /// <summary>
    /// The ID of a keystore whose metadata predates IDs (for example one uploaded by an
    /// older version). Derived from the alias so it stays the same on every read.
    /// </summary>
    public static string GetFallbackId(string alias) => $"vault-{alias}";

    public static byte[] SerializeMetadata(KeystoreVaultMetadata metadata) =>
        JsonSerializer.SerializeToUtf8Bytes(metadata);

    public static KeystoreVaultMetadata? DeserializeMetadata(byte[]? bytes)
    {
        if (bytes is null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<KeystoreVaultMetadata>(bytes, ReadOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// The <c>_META</c> artifact. <see cref="Id"/> and <see cref="FileName"/> were added when the
/// Local Vault became the keystore's home, so older copies may not have them.
/// </summary>
internal sealed record KeystoreVaultMetadata(
    string Alias,
    string KeystoreType,
    DateTime CreatedDate,
    string? Notes,
    DateTime UploadedAt,
    string? Id = null,
    string? FileName = null);
