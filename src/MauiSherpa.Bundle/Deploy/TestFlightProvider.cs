using MauiSherpa.Bundle.Models;
using MauiSherpa.Bundle.Steps;

namespace MauiSherpa.Bundle.Deploy;

/// <summary>
/// Uploads an <c>.ipa</c> to TestFlight via <c>xcrun altool</c> using an App
/// Store Connect API key (spec §4: TestFlight → ApiKey/IssuerId/KeyId).
/// </summary>
public sealed class TestFlightProvider : DeployProviderBase
{
    public override string Name => "TestFlight";
    public override bool Supports(SherpaPlatform platform) => platform == SherpaPlatform.IOS;

    public override async Task<DeployOutcome> DeployAsync(DeployContext ctx)
    {
        if (!OperatingSystem.IsMacOS())
            return Skipped("TestFlight uploads require macOS (xcrun altool).");

        var keyId = ctx.RequireField("KeyId");
        var issuerId = ctx.RequireField("IssuerId");
        var apiKeyB64 = ctx.RequireField("ApiKey");

        var ipa = ctx.PrimaryArtifact("Ipa");
        if (ipa is null)
            return Failed("No .ipa artifact was produced to upload.");

        // altool resolves AuthKey_<KeyId>.p8 from ~/.appstoreconnect/private_keys.
        var keyDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".appstoreconnect", "private_keys");
        Directory.CreateDirectory(keyDir);
        var keyPath = Path.Combine(keyDir, $"AuthKey_{keyId}.p8");
        File.WriteAllBytes(keyPath, SigningAssets.DecodeBase64(apiKeyB64, "TestFlight ApiKey (.p8)"));

        var result = await ctx.Process.RunAsync(
            "xcrun",
            new[] { "altool", "--upload-app", "-f", ipa, "-t", "ios", "--apiKey", keyId, "--apiIssuer", issuerId },
            log: ctx.Log,
            ct: ctx.CancellationToken);

        return result.Success
            ? Succeeded(detail: "Uploaded to App Store Connect / TestFlight.")
            : Failed($"altool exit {result.ExitCode}: {Trim(result.StdErr.Length > 0 ? result.StdErr : result.StdOut)}");
    }

    private static string Trim(string s) => s.Length > 500 ? s[^500..] : s;
}
