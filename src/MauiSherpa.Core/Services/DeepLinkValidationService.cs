using System.Security.Cryptography.Pkcs;
using System.Text;
using System.Text.Json;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public class DeepLinkValidationService : IDeepLinkValidationService
{
    static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    public async Task<AasaValidationResult> ValidateAppleAppSiteAssociationAsync(string domain)
    {
        try
        {
            var url = $"https://{domain}/.well-known/apple-app-site-association";
            var response = await Http.GetAsync(url).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return new AasaValidationResult(false, false, null, Array.Empty<AasaAppEntry>(),
                    $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}", false);
            }

            var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            var contentType = response.Content.Headers.ContentType?.MediaType;
            var (json, wasSigned) = ExtractAasaJson(bytes, contentType);
            using var doc = JsonDocument.Parse(json);
            var apps = new List<AasaAppEntry>();

            if (doc.RootElement.TryGetProperty("applinks", out var applinks) &&
                applinks.TryGetProperty("details", out var details))
            {
                foreach (var detail in details.EnumerateArray())
                {
                    var appIds = new List<string>();
                    var paths = new List<string>();

                    // Modern format: appIDs array
                    if (detail.TryGetProperty("appIDs", out var appIdsEl))
                    {
                        foreach (var id in appIdsEl.EnumerateArray())
                            appIds.Add(id.GetString() ?? "");
                    }
                    // Legacy format: appID string
                    else if (detail.TryGetProperty("appID", out var appIdEl))
                    {
                        appIds.Add(appIdEl.GetString() ?? "");
                    }

                    // Modern format: components array with path
                    if (detail.TryGetProperty("components", out var components))
                    {
                        foreach (var comp in components.EnumerateArray())
                        {
                            if (comp.TryGetProperty("/", out var pathEl))
                                paths.Add(pathEl.GetString() ?? "");
                        }
                    }
                    // Legacy format: paths array
                    else if (detail.TryGetProperty("paths", out var pathsEl))
                    {
                        foreach (var p in pathsEl.EnumerateArray())
                            paths.Add(p.GetString() ?? "");
                    }

                    foreach (var appId in appIds)
                        apps.Add(new AasaAppEntry(appId, paths));

                    if (appIds.Count == 0 && paths.Count > 0)
                        apps.Add(new AasaAppEntry("(unknown)", paths));
                }
            }

            return new AasaValidationResult(true, apps.Count > 0, json, apps, null, wasSigned);
        }
        catch (TaskCanceledException)
        {
            return new AasaValidationResult(false, false, null, Array.Empty<AasaAppEntry>(), "Request timed out", false);
        }
        catch (Exception ex)
        {
            return new AasaValidationResult(false, false, null, Array.Empty<AasaAppEntry>(), ex.Message, false);
        }
    }

    static (string Json, bool WasSigned) ExtractAasaJson(byte[] bytes, string? contentType)
    {
        // AASA may be served as a CMS/PKCS7-signed blob (application/pkcs7-mime) or raw JSON.
        // DER-encoded CMS starts with 0x30 (SEQUENCE); raw JSON starts with '{' or whitespace.
        var looksSigned =
            string.Equals(contentType, "application/pkcs7-mime", StringComparison.OrdinalIgnoreCase) ||
            (bytes.Length > 0 && bytes[0] == 0x30);

        if (looksSigned)
        {
            try
            {
                var cms = new SignedCms();
                cms.Decode(bytes);
                var inner = cms.ContentInfo.Content;
                if (inner is { Length: > 0 })
                    return (Encoding.UTF8.GetString(inner), true);
            }
            catch
            {
                // Fall through and try to parse as raw JSON anyway.
            }
        }

        return (Encoding.UTF8.GetString(bytes), false);
    }

    public async Task<AssetLinksValidationResult> ValidateAssetLinksAsync(string domain)
    {
        try
        {
            var url = $"https://{domain}/.well-known/assetlinks.json";
            var response = await Http.GetAsync(url).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return new AssetLinksValidationResult(false, false, null, Array.Empty<AssetLinksEntry>(),
                    $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var entries = new List<AssetLinksEntry>();

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (!element.TryGetProperty("relation", out var relation))
                    continue;

                var hasHandleAll = false;
                foreach (var rel in relation.EnumerateArray())
                {
                    if (rel.GetString()?.Contains("delegate_permission/common.handle_all_urls") == true)
                    {
                        hasHandleAll = true;
                        break;
                    }
                }

                if (!hasHandleAll || !element.TryGetProperty("target", out var target))
                    continue;

                var package = target.TryGetProperty("package_name", out var pkg) ? pkg.GetString() : null;
                if (package == null) continue;

                string? fingerprint = null;
                if (target.TryGetProperty("sha256_cert_fingerprints", out var fps) && fps.GetArrayLength() > 0)
                    fingerprint = fps[0].GetString();

                entries.Add(new AssetLinksEntry(package, fingerprint));
            }

            return new AssetLinksValidationResult(true, entries.Count > 0, json, entries, null);
        }
        catch (TaskCanceledException)
        {
            return new AssetLinksValidationResult(false, false, null, Array.Empty<AssetLinksEntry>(), "Request timed out");
        }
        catch (Exception ex)
        {
            return new AssetLinksValidationResult(false, false, null, Array.Empty<AssetLinksEntry>(), ex.Message);
        }
    }
}
