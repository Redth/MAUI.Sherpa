using System.Text;
using System.Text.Json;
using MauiSherpa.Bundle.Loading;
using MauiSherpa.Bundle.Models;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Services;

/// <summary>
/// Assembles a <c>.sherpabundle</c> document from a <see cref="PublishProfile"/>
/// (see sherpa-spec.md). Signing material is resolved from the profile's existing
/// Apple/Android configs and inlined as base64; deploy destinations and build
/// substitution maps come from <see cref="PublishProfile.BundleSettings"/>.
/// </summary>
public interface ISherpaBundleBuilderService
{
    /// <summary>Resolves all material and produces an in-memory bundle model.</summary>
    Task<SherpaBundle> BuildAsync(PublishProfile profile, IProgress<string>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Builds the bundle and writes it to <paramref name="filePath"/> as an
    /// encrypted SQLCipher database protected by <paramref name="password"/>.
    /// </summary>
    Task BuildAndSaveAsync(PublishProfile profile, string filePath, string password, IProgress<string>? progress = null, CancellationToken ct = default);
}

public sealed class SherpaBundleBuilderService : ISherpaBundleBuilderService
{
    readonly ICloudSecretsService _cloud;
    readonly ICertificateSyncService _certSync;
    readonly IKeystoreService _keystores;
    readonly ISecureStorageService _secureStorage;
    readonly IManagedSecretsService _managedSecrets;
    readonly IAppleConnectService _appleConnect;
    readonly IAppleIdentityService _appleIdentity;
    readonly IAppleIdentityStateService _identityState;
    readonly ILoggingService _logger;

    public SherpaBundleBuilderService(
        ICloudSecretsService cloud,
        ICertificateSyncService certSync,
        IKeystoreService keystores,
        ISecureStorageService secureStorage,
        IManagedSecretsService managedSecrets,
        IAppleConnectService appleConnect,
        IAppleIdentityService appleIdentity,
        IAppleIdentityStateService identityState,
        ILoggingService logger)
    {
        _cloud = cloud;
        _certSync = certSync;
        _keystores = keystores;
        _secureStorage = secureStorage;
        _managedSecrets = managedSecrets;
        _appleConnect = appleConnect;
        _appleIdentity = appleIdentity;
        _identityState = identityState;
        _logger = logger;
    }

    public async Task BuildAndSaveAsync(PublishProfile profile, string filePath, string password, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("A password is required to write an encrypted bundle.", nameof(password));

        var bundle = await BuildAsync(profile, progress, ct);
        progress?.Report("Encrypting bundle...");
        await SqlCipherBundleStore.SaveAsync(bundle, filePath, password, ct);
        progress?.Report($"Saved {filePath}");
    }

    public async Task<SherpaBundle> BuildAsync(PublishProfile profile, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var settings = profile.BundleSettings ?? new PublishProfileBundleSettings();
        var enabled = settings.Platforms.Count > 0 ? new HashSet<BundlePlatform>(settings.Platforms) : null;
        bool Wants(BundlePlatform p) => enabled is null || enabled.Contains(p);

        // Apple material grouped by platform.
        var iosCerts = new List<CertificateRef>();
        var iosProfiles = new List<ProfileRef>();
        MacPlatform? macOs = null;
        MacPlatform? macCatalyst = null;

        foreach (var apple in profile.AppleConfigs)
        {
            ct.ThrowIfCancellationRequested();

            // Match the API-call identity selection that the publish path uses.
            if (!string.IsNullOrEmpty(apple.IdentityId))
            {
                var identity = await _appleIdentity.GetIdentityAsync(apple.IdentityId);
                if (identity is not null)
                    _identityState.SetSelectedIdentity(identity);
            }

            var platform = apple.Platform ?? ApplePlatformType.iOS;
            if (!WantsApple(platform, Wants))
                continue;

            progress?.Report($"Resolving Apple material for {apple.Label}...");
            var (p12, p12Password) = await ResolveCertificateAsync(apple.CertificateSerialNumber, ct);
            var provisioning = await ResolveProfileAsync(apple.ProfileId, ct);

            switch (platform)
            {
                case ApplePlatformType.iOS:
                    if (p12 is not null)
                        iosCerts.Add(new CertificateRef { Content = p12, Password = p12Password });
                    if (provisioning is not null)
                        iosProfiles.Add(new ProfileRef { Content = provisioning });
                    break;

                case ApplePlatformType.macOS:
                    macOs = new MacPlatform
                    {
                        Certificate = p12,
                        CertificatePassword = p12Password,
                        ProvisioningProfile = provisioning,
                    };
                    break;

                case ApplePlatformType.MacCatalyst:
                    macCatalyst = new MacPlatform
                    {
                        Certificate = p12,
                        CertificatePassword = p12Password,
                        ProvisioningProfile = provisioning,
                    };
                    break;
            }
        }

        // Android keystores.
        var keystores = new List<Keystore>();
        if (Wants(BundlePlatform.Android))
        {
            foreach (var androidConfig in profile.AndroidConfigs)
            {
                ct.ThrowIfCancellationRequested();
                var ks = await ResolveKeystoreAsync(androidConfig, ct);
                if (ks is not null)
                    keystores.Add(ks);
            }
        }

        // Deploy targets grouped by platform.
        var deploy = await ResolveDeployTargetsAsync(settings.DeployTargets, ct);

        // Managed secret mappings → environment-level replace tokens (spec §5.2).
        var replaceTokens = new Dictionary<string, string>(settings.ReplaceTokens);
        foreach (var mapping in profile.SecretMappings)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var bytes = await _managedSecrets.GetValueAsync(mapping.SourceKey, ct);
                if (bytes is null) continue;
                var value = Encoding.UTF8.GetString(bytes);
                foreach (var dest in mapping.DestinationKeys)
                    replaceTokens[dest] = value;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to resolve secret '{mapping.SourceKey}' for bundle: {ex.Message}");
            }
        }

        // Assemble platform blocks.
        AndroidPlatform? android = null;
        if (Wants(BundlePlatform.Android) && (keystores.Count > 0 || deploy.TryGetValue(BundlePlatform.Android, out _)))
        {
            android = new AndroidPlatform
            {
                Setup = keystores.Count > 0 ? new AndroidSetup { Keystores = keystores } : null,
                Deploy = deploy.GetValueOrDefault(BundlePlatform.Android),
            };
        }

        ApplePlatform? ios = null;
        if (Wants(BundlePlatform.iOS) && (iosCerts.Count > 0 || iosProfiles.Count > 0 || deploy.TryGetValue(BundlePlatform.iOS, out _)))
        {
            ios = new ApplePlatform
            {
                Setup = (iosCerts.Count > 0 || iosProfiles.Count > 0)
                    ? new AppleSetup
                    {
                        Certificates = iosCerts.Count > 0 ? iosCerts : null,
                        Profiles = iosProfiles.Count > 0 ? iosProfiles : null,
                    }
                    : null,
                Deploy = deploy.GetValueOrDefault(BundlePlatform.iOS),
            };
        }

        WindowsPlatform? windows = null;
        if (Wants(BundlePlatform.Windows) && deploy.ContainsKey(BundlePlatform.Windows))
        {
            // Windows signing material is not yet captured on the profile — emit a
            // placeholder block carrying deploy/variables so it can be filled later.
            windows = new WindowsPlatform();
        }

        var env = new EnvironmentBlock
        {
            Variables = Nullify(settings.Variables),
            ReplaceTokens = Nullify(replaceTokens),
            MSBuildProperties = Nullify(settings.MSBuildProperties),
            Android = android,
            IOS = ios,
            MacOS = macOs,
            MacCatalyst = macCatalyst,
            Windows = windows,
        };

        var environmentName = string.IsNullOrWhiteSpace(settings.EnvironmentName) ? "Production" : settings.EnvironmentName;
        return new SherpaBundle
        {
            Environments = new Dictionary<string, EnvironmentBlock>(StringComparer.OrdinalIgnoreCase)
            {
                [environmentName] = env,
            },
        };
    }

    static bool WantsApple(ApplePlatformType platform, Func<BundlePlatform, bool> wants) => platform switch
    {
        ApplePlatformType.iOS => wants(BundlePlatform.iOS),
        ApplePlatformType.macOS => wants(BundlePlatform.MacOS),
        ApplePlatformType.MacCatalyst => wants(BundlePlatform.MacCatalyst),
        _ => false,
    };

    async Task<(string? Content, string? Password)> ResolveCertificateAsync(string? serial, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(serial))
            return (null, null);

        try
        {
            var p12Bytes = await _cloud.GetSecretAsync(_certSync.GetCertificateSecretKey(serial), ct);
            var pwdBytes = await _cloud.GetSecretAsync(_certSync.GetCertificatePasswordKey(serial), ct);
            return (
                p12Bytes is not null ? Convert.ToBase64String(p12Bytes) : null,
                pwdBytes is not null ? Encoding.UTF8.GetString(pwdBytes) : null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to resolve certificate {serial} for bundle: {ex.Message}");
            return (null, null);
        }
    }

    async Task<string?> ResolveProfileAsync(string? profileId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(profileId))
            return null;

        try
        {
            var bytes = await _appleConnect.DownloadProfileAsync(profileId);
            return Convert.ToBase64String(bytes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to download provisioning profile {profileId} for bundle: {ex.Message}");
            return null;
        }
    }

    async Task<Keystore?> ResolveKeystoreAsync(PublishProfileAndroidConfig android, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(android.KeystoreId))
            return null;

        try
        {
            var keystores = await _keystores.ListKeystoresAsync();
            var keystore = keystores.FirstOrDefault(k => k.Id == android.KeystoreId);
            if (keystore is null)
                return null;

            string? content = null;
            if (!string.IsNullOrEmpty(keystore.FilePath) && File.Exists(keystore.FilePath))
                content = Convert.ToBase64String(await File.ReadAllBytesAsync(keystore.FilePath, ct));

            var password = await _secureStorage.GetAsync($"android_keystore_pwd_{keystore.Id}");

            return new Keystore
            {
                Content = content,
                KeyAlias = keystore.Alias,
                StorePassword = password,
                KeyPassword = password, // PKCS12 keystores share the store password.
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to resolve keystore for {android.Label}: {ex.Message}");
            return null;
        }
    }

    async Task<Dictionary<BundlePlatform, List<DeployTarget>>> ResolveDeployTargetsAsync(
        List<PublishProfileDeployTarget> targets, CancellationToken ct)
    {
        var result = new Dictionary<BundlePlatform, List<DeployTarget>>();

        foreach (var target in targets)
        {
            ct.ThrowIfCancellationRequested();
            var fields = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

            // Plain fields entered directly.
            foreach (var (key, value) in target.Fields)
                fields[key] = JsonSerializer.SerializeToElement(value);

            // Apple App Store Connect key sourced from a saved identity.
            if (!string.IsNullOrEmpty(target.AppleIdentityId))
            {
                var identity = await _appleIdentity.GetIdentityAsync(target.AppleIdentityId);
                if (identity is not null)
                {
                    if (!string.IsNullOrEmpty(identity.P8KeyContent))
                        fields["ApiKey"] = JsonSerializer.SerializeToElement(
                            Convert.ToBase64String(Encoding.UTF8.GetBytes(identity.P8KeyContent)));
                    fields["KeyId"] = JsonSerializer.SerializeToElement(identity.KeyId);
                    fields["IssuerId"] = JsonSerializer.SerializeToElement(identity.IssuerId);
                }
            }

            // Fields sourced from the managed secrets store.
            foreach (var (fieldName, sourceKey) in target.SecretFields)
            {
                try
                {
                    var bytes = await _managedSecrets.GetValueAsync(sourceKey, ct);
                    if (bytes is not null)
                        fields[fieldName] = JsonSerializer.SerializeToElement(Encoding.UTF8.GetString(bytes));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to resolve deploy secret '{sourceKey}': {ex.Message}");
                }
            }

            var entry = new DeployTarget { Provider = target.Provider, Fields = fields };
            if (!result.TryGetValue(target.Platform, out var list))
                result[target.Platform] = list = new List<DeployTarget>();
            list.Add(entry);
        }

        return result;
    }

    static Dictionary<string, string>? Nullify(Dictionary<string, string> map)
        => map.Count > 0 ? map : null;
}
