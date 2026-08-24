namespace MauiSherpa.Bundles;

public sealed class GooglePlayDeploymentProvider(IBundleProcessRunner processRunner) : IBundleDeploymentProvider
{
    public BundleDeploymentProvider Provider => BundleDeploymentProvider.GooglePlay;

    public IReadOnlyList<string> Validate(BundleDeploymentContext context)
    {
        var errors = new List<string>();
        var variables = BundleDeploymentCommandSupport.MergeVariables(context);

        BundleDeploymentCommandSupport.ValidatePlatform(errors, Provider, context.Platform, BundlePlatform.Android);
        BundleDeploymentCommandSupport.ValidateArtifact(errors, Provider, context.Artifact, "aab", "apk");
        BundleDeploymentCommandSupport.RequireValue(
            errors,
            BundleDeploymentCommandSupport.GetScalar(context.Target, variables, "packageName"),
            "packageName");
        BundleDeploymentCommandSupport.RequireValue(
            errors,
            BundleDeploymentCommandSupport.GetScalar(context.Target, variables, "track"),
            "track");
        BundleDeploymentCommandSupport.ValidatePath(
            errors,
            BundleDeploymentCommandSupport.GetScalar(context.Target, variables, "serviceAccountJsonPath"),
            "serviceAccountJsonPath",
            !context.DryRun,
            "json");

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
        var executable = BundleDeploymentCommandSupport.GetScalar(context.Target, variables, "executable") ?? "fastlane";
        var packageName = BundleDeploymentCommandSupport.GetScalar(context.Target, variables, "packageName")!;
        var track = BundleDeploymentCommandSupport.GetScalar(context.Target, variables, "track")!;
        var serviceAccountJsonPath = BundleDeploymentCommandSupport.GetScalar(context.Target, variables, "serviceAccountJsonPath")!;
        var artifactFlag = string.Equals(Path.GetExtension(context.Artifact.Path), ".apk", StringComparison.OrdinalIgnoreCase)
            ? "--apk"
            : "--aab";

        return await BundleDeploymentCommandSupport.RunProcessAsync(
            Provider,
            processRunner,
            context,
            executable,
            [
                "supply",
                artifactFlag, context.Artifact.Path,
                "--package_name", packageName,
                "--track", track,
                "--json_key", serviceAccountJsonPath
            ],
            null,
            progress,
            cancellationToken).ConfigureAwait(false);
    }
}
