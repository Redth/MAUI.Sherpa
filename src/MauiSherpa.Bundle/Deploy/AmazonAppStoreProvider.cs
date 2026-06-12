using System.Text.Json;
using MauiSherpa.Bundle.Models;

namespace MauiSherpa.Bundle.Deploy;

/// <summary>
/// Amazon Appstore (spec §4: AmazonAppStore → ClientId/ClientSecret). Acquires a
/// Login-with-Amazon client-credentials token to validate the credentials, then
/// reports the remaining Submission-API steps. Full edit/commit submission is
/// intentionally not auto-committed.
/// </summary>
public sealed class AmazonAppStoreProvider : DeployProviderBase
{
    public override string Name => "AmazonAppStore";
    public override bool Supports(SherpaPlatform platform) => platform == SherpaPlatform.Android;

    public override async Task<DeployOutcome> DeployAsync(DeployContext ctx)
    {
        var clientId = ctx.RequireField("ClientId");
        var clientSecret = ctx.RequireField("ClientSecret");

        var artifact = ctx.PrimaryArtifact("Apk", "Aab");
        if (artifact is null)
            return Failed("No .apk/.aab artifact was produced to submit.");

        using var http = new HttpClient();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["scope"] = "appstore::apps:readwrite",
        });

        try
        {
            using var resp = await http.PostAsync("https://api.amazon.com/auth/o2/token", form, ctx.CancellationToken);
            var body = await resp.Content.ReadAsStringAsync(ctx.CancellationToken);
            if (!resp.IsSuccessStatusCode)
                return Failed($"Amazon LWA auth failed ({(int)resp.StatusCode}): {body}");

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("access_token", out _))
                return Failed("Amazon LWA response did not contain an access_token.");

            ctx.Log.Success("Amazon LWA credentials validated.");
            return Skipped(
                "Authenticated with Amazon. Artifact submission requires the Appstore Submission API " +
                "(create edit → upload APK → commit), which is not auto-committed by sherpacli. " +
                $"Artifact ready: {Path.GetFileName(artifact)}.");
        }
        catch (Exception ex)
        {
            return Failed($"Amazon Appstore deploy error: {ex.Message}");
        }
    }
}
