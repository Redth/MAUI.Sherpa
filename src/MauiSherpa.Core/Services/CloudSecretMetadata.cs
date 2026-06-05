using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MauiSherpa.Core.Services;

internal static class CloudSecretMetadata
{
    public const string ReservedKeyPrefix = "maui-sherpa-metadata-";
    private const string ReservedKeyPrefixUnderscore = "maui_sherpa_metadata_";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public static string GetMetadataKey(string providerStorageKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(providerStorageKey));
        return ReservedKeyPrefix + Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool IsMetadataKey(string key)
    {
        return key.StartsWith(ReservedKeyPrefix, StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith(ReservedKeyPrefixUnderscore, StringComparison.OrdinalIgnoreCase);
    }

    public static byte[] Serialize(Dictionary<string, string> metadata)
    {
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(metadata, JsonOptions));
    }

    public static Dictionary<string, string> Deserialize(byte[] bytes)
    {
        var json = Encoding.UTF8.GetString(bytes);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions) ??
            new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
