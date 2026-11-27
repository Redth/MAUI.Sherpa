using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public static class PushProjectCredentials
{
    const string VaultScope = "push-project-credentials";

    public static void Validate(PushProjectPlatform platform, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException(platform == PushProjectPlatform.Apns
                ? "Enter a .p8 private key."
                : "Enter Google service-account JSON.");

        if (platform == PushProjectPlatform.Apns)
        {
            try
            {
                using var key = ECDsa.Create();
                if (!content.Contains("-----BEGIN PRIVATE KEY-----", StringComparison.Ordinal))
                    throw new ArgumentException("Use a PKCS#8 private key.");
                key.ImportFromPem(content);
                var parameters = key.ExportParameters(true);
                if (parameters.Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value)
                    throw new ArgumentException("APNs requires a P-256 private key.");
            }
            catch (Exception ex) when (ex is CryptographicException or ArgumentException)
            {
                throw new ArgumentException("Use a valid P-256 .p8 private key, including its BEGIN/END lines.");
            }
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || GetString(root, "type") != "service_account"
                || string.IsNullOrWhiteSpace(GetString(root, "project_id"))
                || string.IsNullOrWhiteSpace(GetString(root, "client_email"))
                || string.IsNullOrWhiteSpace(GetString(root, "private_key")))
                throw new ArgumentException("Use service-account JSON containing project_id, client_email, and private_key.");
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            try
            {
                _ = ServiceAccountCredential.FromServiceAccountData(stream);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or CryptographicException or FormatException)
            {
                throw new ArgumentException("The service-account JSON contains an invalid private key.");
            }
        }
        catch (JsonException)
        {
            throw new ArgumentException("Enter valid Google service-account JSON.");
        }
    }

    public static async Task<string> StorePastedAsync(ILocalVaultStore vault, PushProjectPlatform platform, string content)
    {
        Validate(platform, content);
        // Copies share credential references. Replacements get a new key to avoid changing another project.
        var key = Guid.NewGuid().ToString("N");
        await vault.PutAsync(VaultScope, "/", key, Encoding.UTF8.GetBytes(content.Trim()),
            platform == PushProjectPlatform.Apns ? LocalVaultContentTypes.Text : LocalVaultContentTypes.Json);
        return key;
    }

    public static async Task<string> ReadPastedAsync(ILocalVaultStore vault, string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("This project has no pasted credentials. Edit the project to add them.");
        var item = await vault.GetAsync(VaultScope, "/", key);
        if (item == null || item.Value.Length == 0)
            throw new InvalidOperationException("The project's credentials are missing from Local Vault. Edit the project to replace them.");
        return Encoding.UTF8.GetString(item.Value);
    }

    public static async Task<string> ReadSecretAsync(IManagedSecretsService secrets, string key)
    {
        var bytes = await secrets.GetValueAsync(key);
        if (bytes is not { Length: > 0 })
            throw new InvalidOperationException($"Could not read \"{key}\". Unlock its provider or choose another secret.");
        return Encoding.UTF8.GetString(bytes).Trim();
    }

    public static async Task<(string Key, string KeyId, string TeamId)> ReadApnsAsync(
        ApnsPushProjectConfig config, IAppleIdentityService identities, IManagedSecretsService secrets,
        ILocalVaultStore vault, IFileSystemService files)
    {
        string content;
        var keyId = config.P8KeyId ?? "";
        switch (config.AuthMode ?? "identity")
        {
            case "identity":
                var identity = await identities.GetIdentityAsync(config.SelectedIdentityId ?? "")
                    ?? throw new InvalidOperationException("The selected Apple identity is unavailable.");
                content = identity.P8KeyContent
                    ?? throw new InvalidOperationException("The selected Apple identity has no .p8 key.");
                keyId = identity.KeyId;
                break;
            case "p8file":
                content = await files.ReadFileAsync(config.P8FilePath ?? "")
                    ?? throw new InvalidOperationException("The .p8 key file is missing or unreadable.");
                break;
            case "paste":
                content = await ReadPastedAsync(vault, config.P8VaultKey);
                break;
            case "secret":
                content = await ReadSecretAsync(secrets, config.P8SecretKey ?? "");
                break;
            default:
                throw new InvalidOperationException("Choose a supported APNs credential source.");
        }
        if (config.AuthMode != "identity" && !string.IsNullOrWhiteSpace(config.KeyIdSecretKey))
            keyId = await ReadSecretAsync(secrets, config.KeyIdSecretKey);
        var teamId = !string.IsNullOrWhiteSpace(config.TeamIdSecretKey)
            ? await ReadSecretAsync(secrets, config.TeamIdSecretKey)
            : config.TeamId ?? "";
        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(teamId))
            throw new InvalidOperationException("Configure both Key ID and Team ID in the project.");
        Validate(PushProjectPlatform.Apns, content);
        return (content, keyId.Trim(), teamId.Trim());
    }

    public static async Task<string> ReadFcmAsync(
        FcmPushProjectConfig config, IGoogleIdentityService identities, IManagedSecretsService secrets,
        ILocalVaultStore vault, IFileSystemService files)
    {
        var content = (config.AuthMode ?? "identity") switch
        {
            "identity" => (await identities.GetIdentityAsync(config.SelectedGoogleIdentityId ?? ""))?.ServiceAccountJson
                ?? throw new InvalidOperationException("The selected Google service account is unavailable."),
            "file" => await files.ReadFileAsync(config.ServiceAccountFilePath ?? "")
                ?? throw new InvalidOperationException("The service-account JSON file is missing or unreadable."),
            "paste" => await ReadPastedAsync(vault, config.ServiceAccountVaultKey),
            "secret" => await ReadSecretAsync(secrets, config.ServiceAccountSecretKey ?? ""),
            _ => throw new InvalidOperationException("Choose a supported Google credential source.")
        };
        if ((config.AuthMode ?? "identity") != "identity")
            Validate(PushProjectPlatform.Fcm, content);
        return content;
    }

    static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
