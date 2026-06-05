using System.Security.Cryptography;
using System.Text;

namespace MauiSherpa.Core.Services;

public sealed class LocalVaultItem
{
    public string Id { get; set; } = "";
    public string Scope { get; set; } = "";
    public string Path { get; set; } = "/";
    public string Key { get; set; } = "";
    public string ContentType { get; set; } = "";
    public byte[] Value { get; set; } = [];
    public Dictionary<string, string> Metadata { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public static string CreateId(string scope, string path, string key)
    {
        var normalizedScope = LocalVaultNames.NormalizeScope(scope);
        var normalizedPath = SecretPath.NormalizeFolderPath(path);
        var normalizedKey = SecretPath.NormalizeKey(key);
        var identity = $"v1\n{normalizedScope}\n{normalizedPath}\n{normalizedKey}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public static class LocalVaultContentTypes
{
    public const string Json = "application/json";
    public const string Text = "text/plain";
    public const string Binary = "application/octet-stream";
}

public static class LocalVaultScopes
{
    public const string LocalProviderSecret = "local-provider-secret";
    public const string LocalProviderMetadata = "local-provider-metadata";
    public const string Settings = "settings";
    public const string SecureStorage = "secure";
    public const string CloudProvider = "cloud-provider";
    public const string Migration = "migration";
}

public static class LocalVaultNames
{
    public static string NormalizeScope(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
            throw new ArgumentException("Vault scope cannot be empty.", nameof(scope));

        return scope.Trim().ToLowerInvariant();
    }
}

public sealed record LocalVaultOptions(string DatabasePath)
{
    public const string DefaultDatabaseFileName = "local-vault.db";

    public static LocalVaultOptions Default =>
        new(Path.Combine(AppDataPath.GetAppDataDirectory(), DefaultDatabaseFileName));
}
