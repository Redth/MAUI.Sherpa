using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Google.Apis.Auth.OAuth2;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public class FirebasePushService : IFirebasePushService
{
    const string ServiceAccountKey = "fcm_service_account_json";
    const string V1EndpointTemplate = "https://fcm.googleapis.com/v1/projects/{0}/messages:send";
    static readonly string[] FcmScopes = ["https://www.googleapis.com/auth/firebase.messaging"];

    readonly ISecureStorageService _secureStorage;
    readonly HttpClient _httpClient;

    public FirebasePushService(ISecureStorageService secureStorage)
    {
        _secureStorage = secureStorage;
        _httpClient = new HttpClient();
    }

    public async Task<bool> HasCredentialsAsync()
        => !string.IsNullOrEmpty(await _secureStorage.GetAsync(ServiceAccountKey));

    public async Task<string?> GetProjectIdAsync()
    {
        var json = await _secureStorage.GetAsync(ServiceAccountKey);
        if (string.IsNullOrEmpty(json)) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("project_id", out var pid) ? pid.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveServiceAccountAsync(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("project_id", out _))
            throw new ArgumentException("Service account JSON must contain a 'project_id' field.");

        await _secureStorage.SetAsync(ServiceAccountKey, json);
    }

    public async Task ClearCredentialsAsync()
    {
        await _secureStorage.RemoveAsync(ServiceAccountKey);
    }

    public async Task<FcmPushResponse> SendPushAsync(FcmPushRequest request)
    {
        var serviceAccountJson = await _secureStorage.GetAsync(ServiceAccountKey);
        if (string.IsNullOrEmpty(serviceAccountJson))
            return new FcmPushResponse(false, null, "No service account configured", null, DateTime.UtcNow);

        try
        {
            var credential = GoogleCredential.FromJson(serviceAccountJson).CreateScoped(FcmScopes);
            var accessToken = await credential.UnderlyingCredential.GetAccessTokenForRequestAsync();

            using var doc = JsonDocument.Parse(serviceAccountJson);
            var projectId = doc.RootElement.GetProperty("project_id").GetString()!;
            var endpoint = string.Format(V1EndpointTemplate, projectId);

            var message = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(request.Topic))
                message["topic"] = request.Topic;
            else
                message["token"] = request.DeviceToken;

            if (!string.IsNullOrEmpty(request.Title) || !string.IsNullOrEmpty(request.Body))
            {
                var notification = new Dictionary<string, string>();
                if (!string.IsNullOrEmpty(request.Title)) notification["title"] = request.Title;
                if (!string.IsNullOrEmpty(request.Body)) notification["body"] = request.Body;
                if (!string.IsNullOrEmpty(request.ImageUrl)) notification["image"] = request.ImageUrl;
                message["notification"] = notification;
            }

            if (request.Data is { Count: > 0 })
                message["data"] = request.Data;

            var payload = JsonSerializer.Serialize(new { message }, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            httpRequest.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(httpRequest);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                string? messageId = null;
                try
                {
                    using var respDoc = JsonDocument.Parse(responseBody);
                    messageId = respDoc.RootElement.TryGetProperty("name", out var name) ? name.GetString() : null;
                }
                catch { }

                return new FcmPushResponse(true, messageId, null, (int)response.StatusCode, DateTime.UtcNow);
            }

            return new FcmPushResponse(false, null, responseBody, (int)response.StatusCode, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            return new FcmPushResponse(false, null, ex.Message, null, DateTime.UtcNow);
        }
    }

    public async Task<FcmPushResponse> SendRawJsonAsync(string messageJson)
    {
        var serviceAccountJson = await _secureStorage.GetAsync(ServiceAccountKey);
        if (string.IsNullOrEmpty(serviceAccountJson))
            return new FcmPushResponse(false, null, "No service account configured", null, DateTime.UtcNow);

        try
        {
            // Validate the user's JSON is parseable
            using var userDoc = JsonDocument.Parse(messageJson);

            var credential = GoogleCredential.FromJson(serviceAccountJson).CreateScoped(FcmScopes);
            var accessToken = await credential.UnderlyingCredential.GetAccessTokenForRequestAsync();

            using var saDoc = JsonDocument.Parse(serviceAccountJson);
            var projectId = saDoc.RootElement.GetProperty("project_id").GetString()!;
            var endpoint = string.Format(V1EndpointTemplate, projectId);

            // Wrap in {"message": ...} if the user provided the inner message object directly
            string payload;
            if (userDoc.RootElement.TryGetProperty("message", out _))
                payload = messageJson;
            else
                payload = JsonSerializer.Serialize(new { message = userDoc.RootElement });

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            httpRequest.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(httpRequest);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                string? messageId = null;
                try
                {
                    using var respDoc = JsonDocument.Parse(responseBody);
                    messageId = respDoc.RootElement.TryGetProperty("name", out var name) ? name.GetString() : null;
                }
                catch { }

                return new FcmPushResponse(true, messageId, null, (int)response.StatusCode, DateTime.UtcNow);
            }

            return new FcmPushResponse(false, null, responseBody, (int)response.StatusCode, DateTime.UtcNow);
        }
        catch (JsonException)
        {
            return new FcmPushResponse(false, null, "Invalid JSON", null, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            return new FcmPushResponse(false, null, ex.Message, null, DateTime.UtcNow);
        }
    }
}
