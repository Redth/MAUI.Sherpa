namespace MauiSherpa.Bundles;

public sealed class SherpaBundleRunner(
    BundleToolchainInstaller toolchainInstaller,
    BundleBuildService buildService,
    BundleDeploymentRegistry deploymentRegistry,
    BundleAssetMaterializer? assetMaterializer = null,
    IBundleProcessRunner? signingProcessRunner = null)
{
    private readonly BundleAssetMaterializer _assetMaterializer = assetMaterializer ?? new BundleAssetMaterializer();
    private readonly IBundleProcessRunner _signingProcessRunner = signingProcessRunner ?? new BundleProcessRunner();

    public async Task<BundleRunResult> RunAsync(
        SherpaBundle bundle,
        BundleRunRequest request,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        BundleValidator.ValidateAndThrow(bundle);
        if (!bundle.Environments.TryGetValue(request.Environment, out var environment))
            throw new BundleValidationException([$"Environment '{request.Environment}' was not found."]);

        var platforms = request.Platforms.Count > 0
            ? request.Platforms.Distinct().ToArray()
            : environment.Platforms.Keys.ToArray();
        if (platforms.Length == 0)
            throw new BundleValidationException([$"Environment '{request.Environment}' has no selected platforms."]);

        BundlePlatformResult[] results;
        if (request.Parallel)
        {
            results = await Task.WhenAll(platforms.Select(platform =>
                RunPlatformAsync(bundle, environment, platform, request, progress, cancellationToken)))
                .ConfigureAwait(false);
        }
        else
        {
            var sequentialResults = new List<BundlePlatformResult>();
            foreach (var platform in platforms)
            {
                sequentialResults.Add(await RunPlatformAsync(
                    bundle,
                    environment,
                    platform,
                    request,
                    progress,
                    cancellationToken).ConfigureAwait(false));
            }
            results = sequentialResults.ToArray();
        }

        return new BundleRunResult
        {
            BundleName = bundle.Name,
            Environment = request.Environment,
            Platforms = results.ToList()
        };
    }

    private async Task<BundlePlatformResult> RunPlatformAsync(
        SherpaBundle bundle,
        SherpaBundleEnvironment environment,
        BundlePlatform platform,
        BundleRunRequest request,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (!environment.Platforms.TryGetValue(platform, out var configuration))
            throw new BundleValidationException([$"Platform '{platform}' is not configured for '{request.Environment}'."]);

        var result = new BundlePlatformResult { Platform = platform };
        var buildVariables = BundleVariableResolver.Resolve(
            bundle, request.Environment, platform, BundlePhase.Build, request.VariableOverrides);
        var installVariables = BundleVariableResolver.Resolve(
            bundle, request.Environment, platform, BundlePhase.Install, request.VariableOverrides);
        var deployVariables = BundleVariableResolver.Resolve(
            bundle, request.Environment, platform, BundlePhase.Deploy, request.VariableOverrides);
        await using var workspace = await BundleStagingWorkspace.CreateAsync(
            request.SourceDirectory, [], buildVariables.Values, cancellationToken).ConfigureAwait(false);

        var assetVariables = await _assetMaterializer.MaterializeAsync(
            bundle, configuration, workspace.RootPath, cancellationToken).ConfigureAwait(false);
        var variables = new Dictionary<string, string>(buildVariables.Values, StringComparer.OrdinalIgnoreCase);
        var resolvedDeployVariables = new Dictionary<string, string>(
            deployVariables.Values,
            StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in assetVariables)
        {
            variables[name] = value;
            resolvedDeployVariables[name] = value;
        }
        var secretValues = buildVariables.SecretValues
            .Concat(installVariables.SecretValues)
            .Concat(deployVariables.SecretValues)
            .Concat(configuration.Install.AssetIds
                .Select(id => bundle.Assets[id].ContentBase64))
            .ToHashSet(StringComparer.Ordinal);

        // Certificate passwords are commonly scoped to the Install phase; fold install-phase
        // variables in (in addition to the materialized asset paths already merged above) so the
        // signing session can resolve them even when they are not also visible to Build/Deploy.
        var signingVariables = new Dictionary<string, string>(variables, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in installVariables.Values)
            signingVariables[name] = value;

        BundleSigningSession signingSession;
        try
        {
            signingSession = request.Phases.Contains(BundlePhase.Install) ||
                             request.Phases.Contains(BundlePhase.Build)
                ? await BundleSigningSession.PrepareAsync(
                    bundle,
                    configuration,
                    platform,
                    workspace.RootPath,
                    signingVariables,
                    secretValues,
                    request.DryRun,
                    _signingProcessRunner,
                    progress,
                    cancellationToken).ConfigureAwait(false)
                : BundleSigningSession.CreateEmpty(_signingProcessRunner);
        }
        catch (Exception ex)
        {
            result.Phases.Add(Failed(
                request.Phases.Contains(BundlePhase.Install) ? BundlePhase.Install : BundlePhase.Build,
                ex));
            return result;
        }
        await using var signingSessionScope = signingSession;
        foreach (var (name, value) in signingSession.Variables)
        {
            variables[name] = value;
            resolvedDeployVariables[name] = value;
        }

        await workspace.ApplyReplacementsAsync(
            configuration.Build.Replacements, variables, cancellationToken).ConfigureAwait(false);

        var preparation = new BundlePreparationResult(
            new Dictionary<string, string>(),
            []);
        if (request.Phases.Contains(BundlePhase.Install))
        {
            try
            {
                preparation = await toolchainInstaller.PrepareAsync(
                    bundle.Toolchain,
                    platform,
                    secretValues,
                    request.DryRun,
                    progress,
                    cancellationToken).ConfigureAwait(false);
                result.Phases.Add(new BundlePhaseResult
                {
                    Phase = BundlePhase.Install,
                    Succeeded = true,
                    Message = request.DryRun ? "Install plan validated." : "Dependencies prepared.",
                    Diagnostics = preparation.Diagnostics.Concat(signingSession.Diagnostics).ToList()
                });
            }
            catch (Exception ex)
            {
                result.Phases.Add(Failed(BundlePhase.Install, ex));
                return result;
            }
        }

        if (request.Phases.Contains(BundlePhase.Build))
        {
            try
            {
                var output = request.OutputDirectory
                    ?? Path.Combine(Path.GetFullPath(request.SourceDirectory), "artifacts", "expedition-packs");
                var platformOutput = Path.Combine(output, request.Environment, platform.ToString().ToLowerInvariant());
                var artifacts = await buildService.BuildAsync(
                    platform,
                    configuration.Build,
                    workspace.RootPath,
                    platformOutput,
                    request.Project,
                    variables,
                    preparation.Environment,
                    secretValues,
                    request.DryRun,
                    progress,
                    cancellationToken).ConfigureAwait(false);
                result.Artifacts.AddRange(artifacts);
                result.Phases.Add(new BundlePhaseResult
                {
                    Phase = BundlePhase.Build,
                    Succeeded = true,
                    Message = request.DryRun
                        ? "Build plan validated."
                        : $"Produced {artifacts.Count} artifact(s).",
                    Diagnostics = request.Phases.Contains(BundlePhase.Install)
                        ? []
                        : signingSession.Diagnostics.ToList()
                });
            }
            catch (Exception ex)
            {
                result.Phases.Add(Failed(BundlePhase.Build, ex));
                return result;
            }
        }

        if (request.Phases.Contains(BundlePhase.Deploy))
        {
            try
            {
                if (configuration.Deploy.Count == 0)
                    throw new BundleValidationException([$"No deployment targets are configured for {platform}."]);
                if (!request.DryRun && result.Artifacts.Count == 0 && string.IsNullOrWhiteSpace(request.ArtifactPath))
                    throw new InvalidOperationException(
                        "Deploy requires a build artifact from the current run or an explicit artifact path.");

                foreach (var target in configuration.Deploy)
                {
                    var artifact = request.DryRun
                        ? new BundleArtifact
                        {
                            Path = target.Artifact ?? "dry-run-artifact",
                            Platform = platform,
                            Kind = target.Artifact is null
                                ? DefaultArtifactKind(platform)
                                : Path.GetExtension(target.Artifact).TrimStart('.')
                        }
                        : result.Artifacts.Count > 0
                            ? SelectArtifact(result.Artifacts, target)
                            : CreateExternalArtifact(request.ArtifactPath!, platform);
                    var provider = deploymentRegistry.Get(target.Provider);
                    var context = new BundleDeploymentContext
                    {
                        Target = target,
                        Platform = platform,
                        Artifact = artifact,
                        WorkingDirectory = workspace.RootPath,
                        Variables = resolvedDeployVariables,
                        SecretValues = secretValues,
                        DryRun = request.DryRun
                    };
                    var errors = provider.Validate(context);
                    if (errors.Count > 0)
                        throw new BundleValidationException(errors);
                    var deployment = await provider.DeployAsync(
                        context, progress, cancellationToken).ConfigureAwait(false);
                    if (!deployment.Succeeded)
                        throw new InvalidOperationException(deployment.Message ?? $"{target.Provider} deployment failed.");
                }

                result.Phases.Add(new BundlePhaseResult
                {
                    Phase = BundlePhase.Deploy,
                    Succeeded = true,
                    Message = request.DryRun ? "Deployment plan validated." : "Deployment completed."
                });
            }
            catch (Exception ex)
            {
                result.Phases.Add(Failed(BundlePhase.Deploy, ex));
            }
        }

        return result;
    }

    private static BundleArtifact SelectArtifact(
        IReadOnlyList<BundleArtifact> artifacts,
        BundleDeploymentTarget target)
    {
        if (string.IsNullOrWhiteSpace(target.Artifact))
            return artifacts[0];

        return artifacts.FirstOrDefault(artifact =>
                   string.Equals(artifact.Kind, target.Artifact.TrimStart('.'), StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(Path.GetFileName(artifact.Path), target.Artifact, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException($"Artifact '{target.Artifact}' was not produced.");
    }

    private static BundleArtifact CreateExternalArtifact(string path, BundlePlatform platform)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Artifact '{fullPath}' was not found.", fullPath);
        return new BundleArtifact
        {
            Path = fullPath,
            Platform = platform,
            Kind = Path.GetExtension(fullPath).TrimStart('.').ToLowerInvariant()
        };
    }

    private static BundlePhaseResult Failed(BundlePhase phase, Exception exception) => new()
    {
        Phase = phase,
        Succeeded = false,
        Message = exception.Message
    };

    private static string DefaultArtifactKind(BundlePlatform platform) => platform switch
    {
        BundlePlatform.Android => "aab",
        BundlePlatform.Ios => "ipa",
        BundlePlatform.MacOS or BundlePlatform.MacCatalyst => "pkg",
        BundlePlatform.Windows => "msix",
        _ => "artifact"
    };

}
