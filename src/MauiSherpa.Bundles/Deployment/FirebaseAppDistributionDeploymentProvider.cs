namespace MauiSherpa.Bundles;

public sealed class FirebaseAppDistributionDeploymentProvider(IBundleProcessRunner processRunner) : IBundleDeploymentProvider
{
    public BundleDeploymentProvider Provider => BundleDeploymentProvider.FirebaseAppDistribution;

    public IReadOnlyList<string> Validate(BundleDeploymentContext context)
    {
        var errors = new List<string>();
        var variables = BundleDeploymentCommandSupport.MergeVariables(context);

        BundleDeploymentCommandSupport.ValidatePlatform(errors, Provider, context.Platform, BundlePlatform.Android);
        BundleDeploymentCommandSupport.ValidateArtifact(errors, Provider, context.Artifact, "aab", "apk");
        BundleDeploymentCommandSupport.RequireValue(
            errors,
            BundleDeploymentCommandSupport.GetScalar(context.Target, variables, "appId"),
            "appId");
        BundleDeploymentCommandSupport.RequireAny(
            errors,
            BundleDeploymentCommandSupport.GetList(context.Target, variables, "groups")
                .Concat(BundleDeploymentCommandSupport.GetList(context.Target, variables, "testers"))
                .ToArray(),
            "groups",
            "testers");

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
        var executable = BundleDeploymentCommandSupport.GetScalar(context.Target, variables, "executable") ?? "firebase";
        var appId = BundleDeploymentCommandSupport.GetScalar(context.Target, variables, "appId")!;
        var groups = BundleDeploymentCommandSupport.GetList(context.Target, variables, "groups");
        var testers = BundleDeploymentCommandSupport.GetList(context.Target, variables, "testers");
        var releaseNotes = BundleDeploymentCommandSupport.GetScalar(context.Target, variables, "releaseNotes");
        var arguments = new List<string>
        {
            "appdistribution:distribute",
            context.Artifact.Path,
            "--app", appId
        };
        if (groups.Count > 0)
        {
            arguments.Add("--groups");
            arguments.Add(string.Join(",", groups));
        }
        if (testers.Count > 0)
        {
            arguments.Add("--testers");
            arguments.Add(string.Join(",", testers));
        }
        if (!string.IsNullOrWhiteSpace(releaseNotes))
        {
            arguments.Add("--release-notes");
            arguments.Add(releaseNotes);
        }

        return await BundleDeploymentCommandSupport.RunProcessAsync(
            Provider,
            processRunner,
            context,
            executable,
            arguments,
            null,
            progress,
            cancellationToken).ConfigureAwait(false);
    }
}
