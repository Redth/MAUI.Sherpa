using MauiSherpa.Bundle.Deploy;
using MauiSherpa.Bundle.Loading;
using MauiSherpa.Bundle.Models;
using MauiSherpa.Bundle.Steps;
using MauiSherpa.Bundle.Substitution;

namespace MauiSherpa.Bundle.Pipeline;

/// <summary>
/// Orchestrates a run: load bundle → select environment/platforms → run
/// setup → build → deploy in order for each platform (spec §1, §6, §7).
/// </summary>
public sealed class SherpaPipeline
{
    private readonly IBundleLoader _loader;
    private readonly IProcessRunner _process;
    private readonly DeployProviderRegistry _deployRegistry;

    public SherpaPipeline(
        IBundleLoader? loader = null,
        IProcessRunner? process = null,
        DeployProviderRegistry? deployRegistry = null)
    {
        _loader = loader ?? new JsonBundleLoader();
        _process = process ?? new ProcessRunner();
        _deployRegistry = deployRegistry ?? DeployProviderRegistry.CreateDefault();
    }

    public async Task<SherpaResult> RunAsync(SherpaRunOptions options, ISherpaLog log, CancellationToken ct = default)
    {
        // 1. Load + select environment.
        var bundle = _loader.Load(options.BundlePath);
        if (!bundle.TryGetEnvironment(options.Environment, out var envName, out var env))
            throw new SherpaBundleException(
                $"Environment '{options.Environment}' not found. Available: {string.Join(", ", bundle.Environments.Keys)}.");
        log.Info($"Environment: {envName}");

        // 2. Resolve project + variables.
        var project = ProjectInfo.Resolve(options.ProjectPath, Directory.GetCurrentDirectory());
        log.Info($"Project: {Path.GetFileName(project.CsprojPath)} [{string.Join(", ", project.TargetFrameworks)}]");
        var variables = ConfigResolver.BuildVariableResolver(bundle, env, options.Variables);

        // 3. Resolve platforms.
        var platforms = ResolvePlatforms(options, env, log);
        if (platforms.Count == 0)
            throw new SherpaBundleException("No platforms to process (none requested and none defined in the environment).");
        log.Info($"Platforms: {string.Join(", ", platforms.Select(p => p.ToDisplayName()))}");
        log.Info($"Steps: {string.Join(" → ", options.Steps.Select(s => s.ToString().ToLowerInvariant()))}");

        var scratch = Path.Combine(Path.GetTempPath(), "sherpacli", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);

        var ctx = new SherpaContext
        {
            Bundle = bundle,
            EnvironmentName = envName,
            Environment = env,
            Options = options,
            Project = project,
            Variables = variables,
            Log = log,
            Process = _process,
            ScratchDirectory = scratch,
            CancellationToken = ct,
        };
        ctx.Result.Environment = envName;

        var setup = new SetupRunner();
        var build = new BuildRunner();
        var deploy = new DeployRunner(_deployRegistry);

        try
        {
            // Workload/SDK inference (spec §6) runs once before building.
            if (options.Steps.Contains(SherpaStep.Build))
                await InferAndRestoreAsync(ctx, platforms);

            foreach (var platform in platforms)
            {
                var config = ConfigResolver.ResolveForPlatform(
                    bundle, env, platform, variables, options.ReplaceTokens, options.MSBuildProperties);

                var platformResult = new PlatformResult();
                ctx.Result.Platforms[platform.ToDisplayName()] = platformResult;

                var platformCtx = new PlatformContext
                {
                    Run = ctx,
                    Platform = platform,
                    Config = config,
                    Result = platformResult,
                };

                foreach (var step in options.Steps)
                {
                    ct.ThrowIfCancellationRequested();
                    switch (step)
                    {
                        case SherpaStep.Setup: await setup.RunAsync(platformCtx); break;
                        case SherpaStep.Build: await build.RunAsync(platformCtx); break;
                        case SherpaStep.Deploy: await deploy.RunAsync(platformCtx); break;
                    }
                }
            }
        }
        finally
        {
            // Scratch holds decoded keystores/certs — remove unless explicitly kept.
            if (Environment.GetEnvironmentVariable("SHERPA_KEEP_SCRATCH") is null)
                TryDelete(scratch);
        }

        return ctx.Result;
    }

    private static IReadOnlyList<SherpaPlatform> ResolvePlatforms(
        SherpaRunOptions options, EnvironmentBlock env, ISherpaLog log)
    {
        var defined = DefinedPlatforms(env);
        if (options.Platforms is null)
            return defined;

        // Explicit selection: honor it, but warn about platforms with no bundle block.
        foreach (var p in options.Platforms)
            if (!defined.Contains(p))
                log.Warn($"Platform {p.ToDisplayName()} was requested but has no block in '{env}'; setup/deploy will be skipped.");
        return options.Platforms;
    }

    private static List<SherpaPlatform> DefinedPlatforms(EnvironmentBlock env)
    {
        var list = new List<SherpaPlatform>();
        if (env.Android is not null) list.Add(SherpaPlatform.Android);
        if (env.IOS is not null) list.Add(SherpaPlatform.IOS);
        if (env.MacOS is not null) list.Add(SherpaPlatform.MacOS);
        if (env.MacCatalyst is not null) list.Add(SherpaPlatform.MacCatalyst);
        if (env.Windows is not null) list.Add(SherpaPlatform.Windows);
        return list;
    }

    /// <summary>
    /// Spec §6: honor global.json (the SDK does this automatically) and restore
    /// the matching workload set. Xcode presence is logged for Apple targets.
    /// </summary>
    private async Task InferAndRestoreAsync(SherpaContext ctx, IReadOnlyList<SherpaPlatform> platforms)
    {
        ctx.Log.Step("inference: restoring workloads");
        var restore = await _process.RunAsync(
            "dotnet",
            new[] { "workload", "restore", ctx.Project.CsprojPath },
            workingDirectory: ctx.Project.Directory,
            log: ctx.Log,
            ct: ctx.CancellationToken);
        if (restore.Success)
            ctx.Log.Success("Workloads restored.");
        else
            ctx.Log.Warn($"`dotnet workload restore` exited {restore.ExitCode}; continuing (workloads may already be present).");

        var needsApple = platforms.Any(p => p is SherpaPlatform.IOS or SherpaPlatform.MacOS or SherpaPlatform.MacCatalyst);
        if (needsApple && OperatingSystem.IsMacOS())
        {
            var xcode = await _process.RunAsync("xcodebuild", new[] { "-version" }, ct: ctx.CancellationToken);
            if (xcode.Success)
                ctx.Log.Info($"Active Xcode: {xcode.StdOut.Split('\n').FirstOrDefault()?.Trim()}");
            else
                ctx.Log.Warn("xcodebuild not found / not selected; Apple builds may fail. Run `xcode-select -s`.");
        }
    }

    private static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { /* best effort */ }
    }
}
