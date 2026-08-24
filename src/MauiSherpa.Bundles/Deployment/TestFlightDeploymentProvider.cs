namespace MauiSherpa.Bundles;

public sealed class TestFlightDeploymentProvider(IBundleProcessRunner processRunner) : IBundleDeploymentProvider
{
    public BundleDeploymentProvider Provider => BundleDeploymentProvider.TestFlight;

    public IReadOnlyList<string> Validate(BundleDeploymentContext context)
    {
        var errors = new List<string>();
        var variables = BundleDeploymentCommandSupport.MergeVariables(context);
        var apiKey = BundleDeploymentCommandSupport.GetScalar(context.Target, variables, "apiKey");
        var apiIssuer = BundleDeploymentCommandSupport.GetScalar(context.Target, variables, "apiIssuer");
        var apiKeyPath = BundleDeploymentCommandSupport.GetScalar(context.Target, variables, "AppleApiKeyPath");

        BundleDeploymentCommandSupport.ValidatePlatform(errors, Provider, context.Platform, BundlePlatform.Ios, BundlePlatform.MacOS);
        BundleDeploymentCommandSupport.ValidateArtifact(
            errors,
            Provider,
            context.Artifact,
            context.Platform == BundlePlatform.MacOS ? "pkg" : "ipa");
        BundleDeploymentCommandSupport.RequireValue(errors, apiKey, "apiKey");
        BundleDeploymentCommandSupport.RequireValue(errors, apiIssuer, "apiIssuer");
        BundleDeploymentCommandSupport.ValidatePath(errors, apiKeyPath, "AppleApiKeyPath", !context.DryRun, "p8");

        return errors;
    }

    public async Task<BundleDeploymentResult> DeployAsync(
        BundleDeploymentContext context,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var errors = Validate(context);
        if (errors.Count > 0)
        {
            return new BundleDeploymentResult
            {
                Provider = Provider,
                Succeeded = false,
                Message = string.Join(Environment.NewLine, errors)
            };
        }

        var variables = BundleDeploymentCommandSupport.MergeVariables(context);
        var apiKey = BundleDeploymentCommandSupport.GetScalar(context.Target, variables, "apiKey")!;
        var apiIssuer = BundleDeploymentCommandSupport.GetScalar(context.Target, variables, "apiIssuer")!;
        var apiKeyPath = BundleDeploymentCommandSupport.GetScalar(context.Target, variables, "AppleApiKeyPath")!;
        if (context.DryRun)
        {
            return await BundleDeploymentCommandSupport.RunProcessAsync(
                Provider,
                processRunner,
                context,
                "xcrun",
                [
                    "altool",
                    "--upload-app",
                    "-f", context.Artifact.Path,
                    "-t", context.Platform == BundlePlatform.MacOS ? "osx" : "ios",
                    "--apiKey", apiKey,
                    "--apiIssuer", apiIssuer
                ],
                null,
                progress,
                cancellationToken).ConfigureAwait(false);
        }

        var workingDirectory = Path.GetFullPath(context.WorkingDirectory);
        var materializedKeyDirectory = Path.Combine(
            workingDirectory,
            ".bundle-deployment",
            "apple-api-keys",
            Guid.NewGuid().ToString("N"));
        var materializedKeyPath = Path.Combine(materializedKeyDirectory, $"AuthKey_{apiKey}.p8");

        try
        {
            Directory.CreateDirectory(materializedKeyDirectory);
            RestrictDirectory(materializedKeyDirectory);
            File.Copy(apiKeyPath, materializedKeyPath, overwrite: true);
            RestrictFile(materializedKeyPath);

            return await BundleDeploymentCommandSupport.RunProcessAsync(
                Provider,
                processRunner,
                context,
                "xcrun",
                [
                    "altool",
                    "--upload-app",
                    "-f", context.Artifact.Path,
                    "-t", context.Platform == BundlePlatform.MacOS ? "osx" : "ios",
                    "--apiKey", apiKey,
                    "--apiIssuer", apiIssuer
                ],
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["API_PRIVATE_KEYS_DIR"] = materializedKeyDirectory
                },
                progress,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (Directory.Exists(materializedKeyDirectory))
                Directory.Delete(materializedKeyDirectory, recursive: true);
            DeleteEmptyParents(materializedKeyDirectory, workingDirectory);
        }
    }

    private static void RestrictDirectory(string directory)
    {
        if (OperatingSystem.IsWindows())
            return;

        File.SetUnixFileMode(
            directory,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static void RestrictFile(string path)
    {
        if (OperatingSystem.IsWindows())
            return;

        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static void DeleteEmptyParents(string path, string stopAt)
    {
        var current = Directory.GetParent(path);
        while (current is not null &&
               current.Exists &&
               !string.Equals(current.FullName, stopAt, StringComparison.Ordinal))
        {
            if (current.EnumerateFileSystemInfos().Any())
                break;

            current.Delete();
            current = current.Parent;
        }
    }
}
