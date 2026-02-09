using System.Text;
using System.Text.Json;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public class KeystoreSyncService : IKeystoreSyncService
{
    private const string CloudKeyPrefix = "KEYSTORE_";

    private readonly IKeystoreService _keystoreService;
    private readonly ICloudSecretsService _cloudService;
    private readonly ISecureStorageService _secureStorage;
    private readonly ILoggingService _logger;

    public KeystoreSyncService(
        IKeystoreService keystoreService,
        ICloudSecretsService cloudService,
        ISecureStorageService secureStorage,
        ILoggingService logger)
    {
        _keystoreService = keystoreService;
        _cloudService = cloudService;
        _secureStorage = secureStorage;
        _logger = logger;
    }

    public async Task<IReadOnlyList<KeystoreSyncStatus>> GetKeystoreStatusesAsync(CancellationToken ct = default)
    {
        var results = new List<KeystoreSyncStatus>();
        var localKeystores = await _keystoreService.ListKeystoresAsync();

        // Get cloud secrets
        var cloudSecrets = new List<string>();
        try
        {
            var secrets = await _cloudService.ListSecretsAsync(CloudKeyPrefix, ct);
            cloudSecrets = secrets?.ToList() ?? new();
        }
        catch
        {
            // Cloud not configured or unavailable
        }

        // Match local keystores with cloud (normalize dashes/underscores and case — providers like Azure Key Vault
        // sanitize keys: underscores→hyphens on store, then hyphens→underscores+uppercase on list)
        var matchedCloudKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ks in localKeystores)
        {
            var cloudKey = GetCloudKey(ks.Alias, "JKS");
            var matchingKey = cloudSecrets.FirstOrDefault(k => NormalizeKey(k) == NormalizeKey(cloudKey));
            var hasCloud = matchingKey != null;
            if (hasCloud) matchedCloudKeys.Add(matchingKey!);

            results.Add(new KeystoreSyncStatus(
                LocalId: ks.Id,
                Alias: ks.Alias,
                LocalPath: ks.FilePath,
                CloudKey: hasCloud ? matchingKey : null,
                HasLocal: true,
                HasCloud: hasCloud));
        }

        // Cloud-only keystores
        foreach (var key in cloudSecrets.Where(k => k.EndsWith("_JKS", StringComparison.OrdinalIgnoreCase) && !matchedCloudKeys.Any(m => NormalizeKey(m) == NormalizeKey(k))))
        {
            var alias = ExtractAliasFromKey(key);
            results.Add(new KeystoreSyncStatus(
                LocalId: null,
                Alias: alias,
                LocalPath: null,
                CloudKey: key,
                HasLocal: false,
                HasCloud: true));
        }

        return results.AsReadOnly();
    }

    public async Task UploadKeystoreToCloudAsync(string keystoreId, string password, CancellationToken ct = default)
    {
        await UploadKeystoreFileAsync(keystoreId, ct);
        await UploadKeystorePasswordAsync(keystoreId, password, ct);
        await UploadKeystoreMetadataAsync(keystoreId, ct);
    }

    public async Task UploadKeystoreFileAsync(string keystoreId, CancellationToken ct = default)
    {
        var ks = await GetKeystoreByIdAsync(keystoreId);
        if (!File.Exists(ks.FilePath))
            throw new FileNotFoundException($"Keystore file not found: {ks.FilePath}");

        var fileBytes = await File.ReadAllBytesAsync(ks.FilePath, ct);
        await _cloudService.StoreSecretAsync(GetCloudKey(ks.Alias, "JKS"), fileBytes, null, ct);
    }

    public async Task UploadKeystorePasswordAsync(string keystoreId, string password, CancellationToken ct = default)
    {
        var ks = await GetKeystoreByIdAsync(keystoreId);
        await _cloudService.StoreSecretAsync(GetCloudKey(ks.Alias, "PWD"), Encoding.UTF8.GetBytes(password), null, ct);
    }

    public async Task UploadKeystoreMetadataAsync(string keystoreId, CancellationToken ct = default)
    {
        var ks = await GetKeystoreByIdAsync(keystoreId);
        var meta = JsonSerializer.Serialize(new
        {
            ks.Alias,
            ks.KeystoreType,
            ks.CreatedDate,
            ks.Notes,
            UploadedAt = DateTime.UtcNow
        });
        await _cloudService.StoreSecretAsync(GetCloudKey(ks.Alias, "META"), Encoding.UTF8.GetBytes(meta), null, ct);
        _logger.LogInformation($"Keystore uploaded to cloud: {ks.Alias}");
    }

    private async Task<AndroidKeystore> GetKeystoreByIdAsync(string keystoreId)
    {
        var keystores = await _keystoreService.ListKeystoresAsync();
        return keystores.FirstOrDefault(k => k.Id == keystoreId)
            ?? throw new InvalidOperationException($"Keystore not found: {keystoreId}");
    }

    public async Task DownloadKeystoreFromCloudAsync(string cloudKey, CancellationToken ct = default)
    {
        var alias = ExtractAliasFromKey(cloudKey);
        _logger.LogInformation($"Downloading keystore from cloud: {alias}");

        // Download keystore file
        var fileBytes = await _cloudService.GetSecretAsync(GetCloudKey(alias, "JKS"), ct)
            ?? throw new InvalidOperationException($"Keystore data not found in cloud for: {alias}");

        // Save to app data
        var keystoreDir = Path.Combine(AppDataPath.GetAppDataDirectory(), "keystores");
        Directory.CreateDirectory(keystoreDir);
        var filePath = Path.Combine(keystoreDir, $"{alias}.keystore");
        await File.WriteAllBytesAsync(filePath, fileBytes, ct);

        // Read metadata
        var metaBytes = await _cloudService.GetSecretAsync(GetCloudKey(alias, "META"), ct);
        string keystoreType = "PKCS12";
        DateTime createdDate = DateTime.UtcNow;
        if (metaBytes != null)
        {
            try
            {
                var metaJson = Encoding.UTF8.GetString(metaBytes);
                using var doc = JsonDocument.Parse(metaJson);
                keystoreType = doc.RootElement.TryGetProperty("KeystoreType", out var kt) ? kt.GetString() ?? "PKCS12" : "PKCS12";
                createdDate = doc.RootElement.TryGetProperty("CreatedDate", out var cd) ? cd.GetDateTime() : DateTime.UtcNow;
            }
            catch { }
        }

        // Download password and store locally
        var pwdBytes = await _cloudService.GetSecretAsync(GetCloudKey(alias, "PWD"), ct);
        var keystore = new AndroidKeystore(
            Id: Guid.NewGuid().ToString(),
            Alias: alias,
            FilePath: filePath,
            KeystoreType: keystoreType,
            CreatedDate: createdDate);

        await _keystoreService.AddKeystoreAsync(keystore);

        if (pwdBytes != null)
            await _secureStorage.SetAsync($"android_keystore_pwd_{keystore.Id}", Encoding.UTF8.GetString(pwdBytes));

        _logger.LogInformation($"Keystore downloaded from cloud: {alias} → {filePath}");
    }

    public async Task DeleteKeystoreFromCloudAsync(string cloudKey, CancellationToken ct = default)
    {
        var alias = ExtractAliasFromKey(cloudKey);
        _logger.LogInformation($"Deleting keystore from cloud: {alias}");

        await _cloudService.DeleteSecretAsync(GetCloudKey(alias, "JKS"), ct);
        await _cloudService.DeleteSecretAsync(GetCloudKey(alias, "PWD"), ct);
        await _cloudService.DeleteSecretAsync(GetCloudKey(alias, "META"), ct);
    }

    private static string GetCloudKey(string alias, string suffix) => $"{CloudKeyPrefix}{alias}_{suffix}";

    private static string NormalizeKey(string key) => key.Replace("-", "_").ToUpperInvariant();

    private static string ExtractAliasFromKey(string cloudKey)
    {
        var withoutPrefix = cloudKey.StartsWith(CloudKeyPrefix) ? cloudKey[CloudKeyPrefix.Length..] : cloudKey;
        var lastUnderscore = withoutPrefix.LastIndexOf('_');
        return lastUnderscore > 0 ? withoutPrefix[..lastUnderscore] : withoutPrefix;
    }
}
