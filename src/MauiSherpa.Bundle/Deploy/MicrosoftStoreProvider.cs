using System.Text.Json;
using MauiSherpa.Bundle.Models;

namespace MauiSherpa.Bundle.Deploy;

/// <summary>
/// Microsoft Store / Partner Center (spec §4: MicrosoftStore →
/// TenantId/ClientId/ClientSecret). Acquires an Azure AD client-credentials
/// token for the Store management API to validate credentials, then reports the
/// remaining submission steps. Full submission is not auto-committed.
/// </summary>
public sealed class MicrosoftStoreProvider : DeployProviderBase
{
    public override string Name => "MicrosoftStore";
    public override bool Supports(SherpaPlatform platform) => platform == SherpaPlatform.Windows;

    public override async Task<DeployOutcome> DeployAsync(DeployContext ctx)
    {
        var tenantId = ctx.RequireField("TenantId");
        var clientId = ctx.RequireField("ClientId");
        var clientSecret = ctx.RequireField("ClientSecret");

        var artifact = ctx.PrimaryArtifact("MsixBundle", "Msix", "Appx");
        if (artifact is null)
            return Failed("No .msix/.msixbundle/.appx artifact was produced to submit.");

        using var http = new HttpClient();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["resource"] = "https://manage.devcenter.microsoft.com",
        });

        try
        {
            var url = $"https://login.microsoftonline.com/{tenantId}/oauth2/token";
            using var resp = await http.PostAsync(url, form, ctx.CancellationToken);
            var body = await resp.Content.ReadAsStringAsync(ctx.CancellationToken);
            if (!resp.IsSuccessStatusCode)
                return Failed($"Azure AD auth failed ({(int)resp.StatusCode}): {body}");

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("access_token", out _))
                return Failed("Azure AD response did not contain an access_token.");

            ctx.Log.Success("Partner Center credentials validated.");
            return Skipped(
                "Authenticated with Partner Center. Submission requires the Store submission API " +
                "(create submission → upload package zip → commit), which is not auto-committed by sherpacli. " +
                $"Artifact ready: {Path.GetFileName(artifact)}.");
        }
        catch (Exception ex)
        {
            return Failed($"Microsoft Store deploy error: {ex.Message}");
        }
    }
}
