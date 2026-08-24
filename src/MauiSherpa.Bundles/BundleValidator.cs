namespace MauiSherpa.Bundles;

public sealed class BundleValidationException(IReadOnlyList<string> errors)
    : Exception($"Bundle validation failed:{Environment.NewLine}- {string.Join($"{Environment.NewLine}- ", errors)}")
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

public static class BundleValidator
{
    public static IReadOnlyList<string> Validate(SherpaBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        var errors = new List<string>();

        if (bundle.Version != SherpaBundle.CurrentVersion)
            errors.Add($"Unsupported bundle version '{bundle.Version}'.");
        if (string.IsNullOrWhiteSpace(bundle.Name))
            errors.Add("Bundle name is required.");
        if (bundle.Environments.Count == 0)
            errors.Add("At least one environment is required.");

        foreach (var (name, environment) in bundle.Environments)
        {
            if (string.IsNullOrWhiteSpace(name))
                errors.Add("Environment names cannot be empty.");
            if (environment.Platforms.Count == 0)
                errors.Add($"Environment '{name}' must contain at least one platform.");

            foreach (var (platform, configuration) in environment.Platforms)
            {
                foreach (var assetId in configuration.Install.AssetIds)
                {
                    if (!bundle.Assets.ContainsKey(assetId))
                        errors.Add($"Environment '{name}' platform '{platform}' references missing asset '{assetId}'.");
                }

                foreach (var replacement in configuration.Build.Replacements)
                {
                    if (!IsSafeRelativePath(replacement.Path))
                        errors.Add($"Replacement path '{replacement.Path}' must be a safe relative path.");
                }

                foreach (var deployment in configuration.Deploy)
                {
                    if (!Supports(deployment.Provider, platform))
                        errors.Add($"Provider '{deployment.Provider}' does not support platform '{platform}'.");
                }
            }
        }

        foreach (var (assetId, asset) in bundle.Assets)
        {
            if (string.IsNullOrWhiteSpace(assetId))
                errors.Add("Asset IDs cannot be empty.");
            if (!IsSafeFileName(asset.FileName))
                errors.Add($"Asset '{assetId}' has an unsafe file name.");
            try
            {
                _ = Convert.FromBase64String(asset.ContentBase64);
            }
            catch (FormatException)
            {
                errors.Add($"Asset '{assetId}' content is not valid base64.");
            }
        }

        return errors;
    }

    public static void ValidateAndThrow(SherpaBundle bundle)
    {
        var errors = Validate(bundle);
        if (errors.Count > 0)
            throw new BundleValidationException(errors);
    }

    private static bool Supports(BundleDeploymentProvider provider, BundlePlatform platform) =>
        provider switch
        {
            BundleDeploymentProvider.TestFlight => platform is BundlePlatform.Ios or BundlePlatform.MacOS,
            BundleDeploymentProvider.GooglePlay or
            BundleDeploymentProvider.FirebaseAppDistribution or
            BundleDeploymentProvider.AmazonAppstore => platform == BundlePlatform.Android,
            _ => false
        };

    internal static bool IsSafeRelativePath(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        !Path.IsPathRooted(path) &&
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .All(segment => segment is not "" and not "." and not "..");

    private static bool IsSafeFileName(string fileName) =>
        !string.IsNullOrWhiteSpace(fileName) &&
        string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal) &&
        fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
}
