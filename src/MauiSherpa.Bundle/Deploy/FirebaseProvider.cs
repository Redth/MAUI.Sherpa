using MauiSherpa.Bundle.Models;

namespace MauiSherpa.Bundle.Deploy;

/// <summary>
/// Distributes a build via Firebase App Distribution using the
/// <c>firebase</c> CLI (spec §4: Firebase → ApiKey/AppId/TesterGroups).
/// </summary>
public sealed class FirebaseProvider : DeployProviderBase
{
    public override string Name => "Firebase";
    public override bool Supports(SherpaPlatform platform)
        => platform is SherpaPlatform.IOS or SherpaPlatform.Android;

    public override async Task<DeployOutcome> DeployAsync(DeployContext ctx)
    {
        var firebase = await ctx.Process.WhichAsync("firebase", ctx.CancellationToken);
        if (firebase is null)
            return Skipped("Firebase CLI not found on PATH. Install with: npm i -g firebase-tools.");

        var appId = ctx.RequireField("AppId");
        var token = ctx.RequireField("ApiKey");
        var artifact = ctx.PrimaryArtifact("Aab", "Apk", "Ipa");
        if (artifact is null)
            return Failed("No artifact (.aab/.apk/.ipa) was produced to distribute.");

        var args = new List<string>
        {
            "appdistribution:distribute", artifact,
            "--app", appId,
            "--token", token,
        };

        var groups = ctx.Target.GetStringArray("TesterGroups");
        if (groups is { Count: > 0 })
        {
            args.Add("--groups");
            args.Add(string.Join(",", groups.Select(ctx.Variables.Resolve)));
        }

        var result = await ctx.Process.RunAsync(firebase, args, log: ctx.Log, ct: ctx.CancellationToken);
        return result.Success
            ? Succeeded(detail: "Distributed via Firebase App Distribution.")
            : Failed($"firebase exit {result.ExitCode}: {result.StdErr}");
    }
}
