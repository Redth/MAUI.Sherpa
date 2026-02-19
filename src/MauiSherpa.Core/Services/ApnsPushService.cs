using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public class ApnsPushService : IApnsPushService
{
    const string ProductionHost = "https://api.push.apple.com";
    const string SandboxHost = "https://api.sandbox.push.apple.com";

    readonly ILoggingService _logger;

    public ApnsPushService(ILoggingService logger)
    {
        _logger = logger;
    }

    public async Task<ApnsPushResult> SendPushAsync(ApnsPushRequest request, CancellationToken ct = default)
    {
        try
        {
            var jwt = GenerateJwt(request.P8Key, request.KeyId, request.TeamId);
            var host = request.UseSandbox ? SandboxHost : ProductionHost;
            var url = $"{host}/3/device/{request.DeviceToken}";

            using var handler = new SocketsHttpHandler
            {
                SslOptions = new System.Net.Security.SslClientAuthenticationOptions()
            };
            using var client = new HttpClient(handler)
            {
                DefaultRequestVersion = new Version(2, 0),
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
            };

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
            httpRequest.Version = new Version(2, 0);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("bearer", jwt);
            httpRequest.Headers.TryAddWithoutValidation("apns-topic", request.BundleId);
            httpRequest.Headers.TryAddWithoutValidation("apns-push-type", request.PushType);
            httpRequest.Headers.TryAddWithoutValidation("apns-priority", request.Priority.ToString());

            if (!string.IsNullOrWhiteSpace(request.CollapseId))
                httpRequest.Headers.TryAddWithoutValidation("apns-collapse-id", request.CollapseId);

            if (!string.IsNullOrWhiteSpace(request.NotificationId))
                httpRequest.Headers.TryAddWithoutValidation("apns-id", request.NotificationId);

            if (request.ExpirationSeconds.HasValue)
                httpRequest.Headers.TryAddWithoutValidation("apns-expiration", request.ExpirationSeconds.Value.ToString());

            httpRequest.Content = new StringContent(request.JsonPayload, Encoding.UTF8, "application/json");

            _logger.LogInformation($"Sending APNs push to {request.DeviceToken} via {(request.UseSandbox ? "sandbox" : "production")}");

            var response = await client.SendAsync(httpRequest, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            var apnsId = response.Headers.TryGetValues("apns-id", out var ids) ? ids.FirstOrDefault() : null;

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation($"APNs push sent successfully. apns-id: {apnsId}");
                return new ApnsPushResult(true, (int)response.StatusCode, apnsId, null, null);
            }

            string? reason = null;
            string? description = null;
            if (!string.IsNullOrEmpty(responseBody))
            {
                try
                {
                    using var doc = JsonDocument.Parse(responseBody);
                    reason = doc.RootElement.TryGetProperty("reason", out var r) ? r.GetString() : null;
                    description = responseBody;
                }
                catch
                {
                    description = responseBody;
                }
            }

            _logger.LogError($"APNs push failed. Status: {(int)response.StatusCode}, Reason: {reason ?? "unknown"}");
            return new ApnsPushResult(false, (int)response.StatusCode, apnsId, reason, description);
        }
        catch (Exception ex)
        {
            _logger.LogError($"APNs push exception: {ex.Message}", ex);
            return new ApnsPushResult(false, 0, null, "Exception", ex.Message);
        }
    }

    static string GenerateJwt(string p8Key, string keyId, string teamId)
    {
        var cleanKey = p8Key
            .Replace("-----BEGIN PRIVATE KEY-----", "")
            .Replace("-----END PRIVATE KEY-----", "")
            .Replace("\n", "")
            .Replace("\r", "")
            .Trim();

        var keyBytes = Convert.FromBase64String(cleanKey);

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(keyBytes, out _);

        var header = JsonSerializer.Serialize(new { alg = "ES256", kid = keyId });
        var payload = JsonSerializer.Serialize(new { iss = teamId, iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });

        var headerBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(header));
        var payloadBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payload));
        var unsignedToken = $"{headerBase64}.{payloadBase64}";

        var signature = ecdsa.SignData(Encoding.UTF8.GetBytes(unsignedToken), HashAlgorithmName.SHA256);
        var signatureBase64 = Base64UrlEncode(signature);

        return $"{unsignedToken}.{signatureBase64}";
    }

    static string Base64UrlEncode(byte[] data)
        => Convert.ToBase64String(data)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
}
