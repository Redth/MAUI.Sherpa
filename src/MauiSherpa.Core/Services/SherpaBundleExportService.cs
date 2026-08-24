using MauiSherpa.Bundles;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public sealed class SherpaBundleExportService(IPublishProfileService publishProfileService)
    : ISherpaBundleExportService
{
    public async Task<byte[]> ExportAsync(
        SherpaBundleDefinition definition,
        string password,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrEmpty(password);

        var profile = await publishProfileService.GetProfileAsync(definition.PublishProfileId)
            ?? throw new InvalidOperationException(
                $"Publish profile '{definition.PublishProfileId}' was not found.");
        progress?.Report("Resolving publish profile secrets...");
        var resolvedSecrets = await publishProfileService.ResolveSecretsAsync(
            profile,
            progress,
            cancellationToken);

        var variables = new Dictionary<string, string>(
            definition.Template.Variables,
            StringComparer.OrdinalIgnoreCase);
        var secretVariables = new HashSet<string>(
            definition.Template.SecretVariables,
            StringComparer.OrdinalIgnoreCase);
        var assets = new Dictionary<string, BundleEmbeddedAsset>(
            definition.Template.Assets,
            StringComparer.OrdinalIgnoreCase);

        foreach (var (name, value) in resolvedSecrets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            secretVariables.Add(name);
            if (TryCreateAsset(name, value, out var asset))
            {
                assets[name] = asset;
                variables.Remove(name);
            }
            else
            {
                variables[name] = value;
            }
        }

        var template = definition.Template with
        {
            Name = definition.Name,
            Description = definition.Description,
            Variables = variables,
            SecretVariables = secretVariables,
            Assets = assets,
            Environments = AddAssetsToPlatforms(definition.Template.Environments, assets)
        };
        BundleValidator.ValidateAndThrow(template);
        progress?.Report("Sealing Expedition Pack...");
        return SherpaBundleFile.Encrypt(template, password);
    }

    private static bool TryCreateAsset(
        string name,
        string value,
        out BundleEmbeddedAsset asset)
    {
        var kind = GetAssetKind(name, value);
        if (kind is null)
        {
            asset = null!;
            return false;
        }

        byte[] content;
        if (kind is BundleAssetKind.AppleApiKey or
            BundleAssetKind.GoogleServiceAccount)
        {
            content = System.Text.Encoding.UTF8.GetBytes(value);
        }
        else
        {
            try
            {
                content = Convert.FromBase64String(value);
            }
            catch (FormatException)
            {
                asset = null!;
                return false;
            }
        }

        asset = new BundleEmbeddedAsset
        {
            Kind = kind.Value,
            FileName = GetFileName(name, kind.Value),
            ContentBase64 = Convert.ToBase64String(content),
            PasswordVariable = GetPasswordVariable(name, kind.Value),
            OutputVariable = name
        };
        return true;
    }

    private static BundleAssetKind? GetAssetKind(string name, string value)
    {
        if (name.EndsWith("_CERTIFICATE_P12", StringComparison.OrdinalIgnoreCase))
            return BundleAssetKind.AppleCertificate;
        if (name.Contains("_PROFILE", StringComparison.OrdinalIgnoreCase))
            return BundleAssetKind.AppleProvisioningProfile;
        if (name.EndsWith("_KEYSTORE", StringComparison.OrdinalIgnoreCase))
            return BundleAssetKind.AndroidKeystore;
        if (name.EndsWith("_P8_KEY", StringComparison.OrdinalIgnoreCase))
            return BundleAssetKind.AppleApiKey;
        if (name.EndsWith("_SERVICE_ACCOUNT_JSON", StringComparison.OrdinalIgnoreCase))
            return BundleAssetKind.GoogleServiceAccount;
        if (value.Contains("BEGIN PRIVATE KEY", StringComparison.Ordinal))
            return BundleAssetKind.AppleApiKey;
        if (value.Contains("\"type\"", StringComparison.OrdinalIgnoreCase) &&
            value.Contains("\"service_account\"", StringComparison.OrdinalIgnoreCase))
            return BundleAssetKind.GoogleServiceAccount;
        return null;
    }

    private static string GetFileName(string name, BundleAssetKind kind) => kind switch
    {
        BundleAssetKind.AppleCertificate => $"{name}.p12",
        BundleAssetKind.AppleProvisioningProfile => $"{name}.mobileprovision",
        BundleAssetKind.AndroidKeystore => $"{name}.keystore",
        BundleAssetKind.AppleApiKey => $"{name}.p8",
        BundleAssetKind.GoogleServiceAccount => $"{name}.json",
        BundleAssetKind.WindowsCertificate => $"{name}.pfx",
        _ => $"{name}.bin"
    };

    private static string? GetPasswordVariable(string name, BundleAssetKind kind) => kind switch
    {
        BundleAssetKind.AppleCertificate when name.EndsWith("_P12", StringComparison.OrdinalIgnoreCase) =>
            name[..^"_P12".Length] + "_PASSWORD",
        BundleAssetKind.AndroidKeystore => name + "_PASSWORD",
        BundleAssetKind.WindowsCertificate => name + "_PASSWORD",
        _ => null
    };

    private static Dictionary<string, SherpaBundleEnvironment> AddAssetsToPlatforms(
        IReadOnlyDictionary<string, SherpaBundleEnvironment> environments,
        IReadOnlyDictionary<string, BundleEmbeddedAsset> assets)
    {
        return environments.ToDictionary(
            pair => pair.Key,
            pair => pair.Value with
            {
                Platforms = pair.Value.Platforms.ToDictionary(
                    platformPair => platformPair.Key,
                    platformPair => platformPair.Value with
                    {
                        Install = platformPair.Value.Install with
                        {
                            AssetIds = platformPair.Value.Install.AssetIds
                                .Concat(assets
                                    .Where(asset => AppliesTo(asset.Value.Kind, platformPair.Key))
                                    .Select(asset => asset.Key))
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToList()
                        }
                    })
            },
            StringComparer.OrdinalIgnoreCase);
    }

    private static bool AppliesTo(BundleAssetKind kind, BundlePlatform platform) => kind switch
    {
        BundleAssetKind.AndroidKeystore or
        BundleAssetKind.GoogleServiceAccount => platform == BundlePlatform.Android,
        BundleAssetKind.AppleCertificate or
        BundleAssetKind.AppleProvisioningProfile or
        BundleAssetKind.AppleApiKey =>
            platform is BundlePlatform.Ios or BundlePlatform.MacOS or BundlePlatform.MacCatalyst,
        BundleAssetKind.WindowsCertificate => platform == BundlePlatform.Windows,
        BundleAssetKind.Generic => true,
        _ => false
    };
}
