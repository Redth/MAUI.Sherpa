using MauiSherpa.Bundle.Models;
using MauiSherpa.Bundle.Steps;

namespace MauiSherpa.Bundle.Deploy;

/// <summary>
/// Publishes an Android App Bundle to Google Play via <c>fastlane supply</c>
/// (spec §4: PlayStore → ServiceAccountKey/Track). fastlane wraps the Google
/// Play Developer API and is the most portable CLI path.
/// </summary>
public sealed class PlayStoreProvider : DeployProviderBase
{
    private static readonly HashSet<string> ValidTracks = new(StringComparer.OrdinalIgnoreCase)
    {
        "internal", "alpha", "beta", "production",
    };

    public override string Name => "PlayStore";
    public override bool Supports(SherpaPlatform platform) => platform == SherpaPlatform.Android;

    public override async Task<DeployOutcome> DeployAsync(DeployContext ctx)
    {
        var track = ctx.RequireField("Track");
        if (!ValidTracks.Contains(track))
            return Failed($"Invalid Track '{track}'. Valid: {string.Join(", ", ValidTracks)}.");

        var keyB64 = ctx.RequireField("ServiceAccountKey");
        var aab = ctx.PrimaryArtifact("Aab", "Apk");
        if (aab is null)
            return Failed("No .aab/.apk artifact was produced to publish.");

        var packageName = ctx.Field("PackageName")
            ?? (ctx.Platform.Config.MSBuildProperties.TryGetValue("ApplicationId", out var appId) ? appId : null);
        if (string.IsNullOrWhiteSpace(packageName))
            return Failed("Could not determine package name. Set Android ApplicationId or deploy field 'PackageName'.");

        var fastlane = await ctx.Process.WhichAsync("fastlane", ctx.CancellationToken);
        if (fastlane is null)
            return Skipped("fastlane not found on PATH. Install with: gem install fastlane (used for Play Store uploads).");

        var keyJson = SigningAssets.DecodeBase64(keyB64, "PlayStore ServiceAccountKey");
        var keyPath = SigningAssets.WriteAsset(ctx.ScratchDirectory, "playstore", "service-account.json", keyJson);

        var isAab = aab.EndsWith(".aab", StringComparison.OrdinalIgnoreCase);
        var args = new List<string>
        {
            "supply",
            "--json_key", keyPath,
            "--package_name", packageName,
            "--track", track,
            isAab ? "--aab" : "--apk", aab,
        };

        var result = await ctx.Process.RunAsync(fastlane, args, log: ctx.Log, ct: ctx.CancellationToken);
        return result.Success
            ? Succeeded(detail: $"Uploaded to Play Store track '{track}'.")
            : Failed($"fastlane supply exit {result.ExitCode}: {result.StdErr}");
    }
}
