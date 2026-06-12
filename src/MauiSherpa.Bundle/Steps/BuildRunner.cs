using MauiSherpa.Bundle.Loading;
using MauiSherpa.Bundle.Models;
using MauiSherpa.Bundle.Pipeline;

namespace MauiSherpa.Bundle.Steps;

/// <summary>
/// The <c>build</c> step: applies replace tokens, then invokes
/// <c>dotnet publish</c> for the platform's TFM with the merged MSBuild
/// properties, and records artifacts + version (spec §5.2, §7).
/// </summary>
public sealed class BuildRunner
{
    public async Task RunAsync(PlatformContext ctx)
    {
        ctx.Log.Step($"[{ctx.Platform.ToDisplayName()}] build");

        // 1. Replace tokens across the project tree.
        TokenReplacer.Apply(ctx.Run.Project.Directory, ctx.Config.ReplaceTokens, ctx.Log);

        // 2. Resolve the platform TFM.
        var tfm = ctx.Run.Project.GetTargetFramework(ctx.Platform);
        if (tfm is null)
            throw new SherpaBundleException(
                $"Project '{Path.GetFileName(ctx.Run.Project.CsprojPath)}' has no target framework for {ctx.Platform.ToDisplayName()}. " +
                $"Found: {string.Join(", ", ctx.Run.Project.TargetFrameworks)}.");

        // 3. Compose the publish command.
        var props = ctx.AllMSBuildProperties();
        var args = new List<string>
        {
            "publish", ctx.Run.Project.CsprojPath,
            "-f", tfm,
            "-c", GetConfiguration(props),
        };

        var rid = ResolveRuntimeIdentifier(ctx.Platform, props);
        if (rid is not null)
        {
            args.Add("-p:RuntimeIdentifier=" + rid);
        }

        foreach (var (key, value) in props)
        {
            if (string.Equals(key, "Configuration", StringComparison.OrdinalIgnoreCase))
                continue; // already passed via -c
            args.Add($"-p:{key}={value}");
        }

        // 4. Build.
        var result = await ctx.Process.RunAsync(
            "dotnet", args,
            workingDirectory: ctx.Run.Project.Directory,
            log: ctx.Log,
            ct: ctx.CancellationToken);

        if (!result.Success)
        {
            ctx.Log.Error(result.StdErr.Length > 0 ? result.StdErr : result.StdOut);
            throw new SherpaBundleException($"{ctx.Platform.ToDisplayName()} build failed (exit {result.ExitCode}).");
        }

        // 5. Capture version + artifacts.
        ctx.Result.Version = ResolveVersion(props);
        CollectArtifacts(ctx, tfm, rid);

        ctx.Log.Success($"{ctx.Platform.ToDisplayName()} build complete ({ctx.Result.Artifacts.Count} artifact(s)).");
    }

    private static string GetConfiguration(IReadOnlyDictionary<string, string> props)
        => props.TryGetValue("Configuration", out var c) && !string.IsNullOrWhiteSpace(c) ? c : "Release";

    private static string? ResolveRuntimeIdentifier(SherpaPlatform platform, IReadOnlyDictionary<string, string> props)
    {
        if (props.TryGetValue("RuntimeIdentifier", out var explicitRid) && !string.IsNullOrWhiteSpace(explicitRid))
            return explicitRid;

        return platform switch
        {
            // Android RID is selected by the Android SDK targets; leave unset.
            SherpaPlatform.Android => null,
            SherpaPlatform.IOS => "ios-arm64",
            SherpaPlatform.MacCatalyst => OperatingSystem.IsMacOS() && System.Runtime.InteropServices.RuntimeInformation
                .OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "maccatalyst-arm64" : "maccatalyst-x64",
            SherpaPlatform.MacOS => OperatingSystem.IsMacOS() && System.Runtime.InteropServices.RuntimeInformation
                .OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "osx-arm64" : "osx-x64",
            SherpaPlatform.Windows => "win-x64",
            _ => null,
        };
    }

    private static string? ResolveVersion(IReadOnlyDictionary<string, string> props)
    {
        if (props.TryGetValue("ApplicationVersion", out var v) && !string.IsNullOrWhiteSpace(v))
            return v;
        if (props.TryGetValue("ApplicationDisplayVersion", out var dv) && !string.IsNullOrWhiteSpace(dv))
            return dv;
        return null;
    }

    private static void CollectArtifacts(PlatformContext ctx, string tfm, string? rid)
    {
        var binDir = Path.Combine(ctx.Run.Project.Directory, "bin");
        if (!Directory.Exists(binDir))
            return;

        // Patterns per platform → artifact kind.
        var patterns = ctx.Platform switch
        {
            SherpaPlatform.Android => new[] { ("*-Signed.aab", "Aab"), ("*.aab", "Aab"), ("*-Signed.apk", "Apk"), ("*.apk", "Apk") },
            SherpaPlatform.IOS => new[] { ("*.ipa", "Ipa") },
            SherpaPlatform.MacCatalyst => new[] { ("*.pkg", "Pkg"), ("*.app", "App") },
            SherpaPlatform.MacOS => new[] { ("*.pkg", "Pkg"), ("*.app", "App") },
            SherpaPlatform.Windows => new[] { ("*.msix", "Msix"), ("*.msixbundle", "MsixBundle"), ("*.appx", "Appx") },
            _ => Array.Empty<(string, string)>(),
        };

        foreach (var (glob, kind) in patterns)
        {
            if (ctx.Result.Artifacts.ContainsKey(kind))
                continue;

            // Prefer the artifact under the current TFM's output folder.
            var match = Directory
                .EnumerateFiles(binDir, glob, SearchOption.AllDirectories)
                .Where(p => p.Contains(tfm, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault()
                ?? Directory
                    .EnumerateFiles(binDir, glob, SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();

            if (match is not null)
            {
                ctx.Result.Artifacts[kind] = match;
                ctx.Log.Info($"  artifact[{kind}] = {Path.GetRelativePath(ctx.Run.Project.Directory, match)}");
            }
        }
    }
}
