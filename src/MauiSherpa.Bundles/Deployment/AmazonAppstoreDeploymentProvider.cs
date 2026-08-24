using System.Net.Http.Headers;
using System.Text.Json;

namespace MauiSherpa.Bundles;

public sealed class AmazonAppstoreDeploymentProvider : IBundleDeploymentProvider
{
    private readonly HttpClient _httpClient;

    public AmazonAppstoreDeploymentProvider(IBundleProcessRunner processRunner)
        : this(processRunner, new HttpClient())
    {
    }

    internal AmazonAppstoreDeploymentProvider(IBundleProcessRunner processRunner, HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(processRunner);
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
    }

    public BundleDeploymentProvider Provider => BundleDeploymentProvider.AmazonAppstore;

    public IReadOnlyList<string> Validate(BundleDeploymentContext context)
    {
        var errors = new List<string>();
        var variables = BundleDeploymentCommandSupport.MergeVariables(context);

        BundleDeploymentCommandSupport.ValidatePlatform(errors, Provider, context.Platform, BundlePlatform.Android);
        BundleDeploymentCommandSupport.ValidateArtifact(errors, Provider, context.Artifact, "apk");
        BundleDeploymentCommandSupport.RequireValue(
            errors,
            BundleDeploymentCommandSupport.GetScalar(context.Target, variables, "clientId"),
            "clientId");
        BundleDeploymentCommandSupport.RequireValue(
            errors,
            BundleDeploymentCommandSupport.GetScalar(context.Target, variables, "clientSecret"),
            "clientSecret");
        BundleDeploymentCommandSupport.RequireValue(
            errors,
            BundleDeploymentCommandSupport.GetScalar(context.Target, variables, "applicationId"),
            "applicationId");

        return errors;
    }

    public async Task<BundleDeploymentResult> DeployAsync(
        BundleDeploymentContext context,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var errors = Validate(context);
        if (errors.Count > 0)
        {
            return new BundleDeploymentResult
            {
                Provider = Provider,
                Succeeded = false,
                Message = string.Join(Environment.NewLine, errors)
            };
        }

        if (context.DryRun)
        {
            return new BundleDeploymentResult
            {
                Provider = Provider,
                Succeeded = true,
                Message = $"{Provider} deployment validated."
            };
        }

        var variables = BundleDeploymentCommandSupport.MergeVariables(context);
        var clientId = BundleDeploymentCommandSupport.GetScalar(context.Target, variables, "clientId")!;
        var clientSecret = BundleDeploymentCommandSupport.GetScalar(context.Target, variables, "clientSecret")!;
        var applicationId = BundleDeploymentCommandSupport.GetScalar(context.Target, variables, "applicationId")!;
        var secretValues = context.SecretValues
            .Concat([clientSecret])
            .ToHashSet(StringComparer.Ordinal);

        string? accessToken = null;
        string? editId = null;
        string? editEtag = null;
        var committed = false;

        try
        {
            progress?.Report("Authenticating with Amazon App Submission API.");
            accessToken = await RequestAccessTokenAsync(clientId, clientSecret, cancellationToken).ConfigureAwait(false);

            progress?.Report("Creating Amazon Appstore edit.");
            var createResponse = await SendJsonAsync(
                () => CreateRequest(HttpMethod.Post, EditCollectionUri(applicationId), accessToken),
                cancellationToken).ConfigureAwait(false);
            editId = GetRequiredJsonProperty(createResponse.Body, "id", "editId");

            progress?.Report("Uploading APK to Amazon Appstore edit.");
            await UploadApkAsync(applicationId, editId, accessToken, context.Artifact.Path, cancellationToken).ConfigureAwait(false);

            progress?.Report("Fetching edit metadata.");
            var editResponse = await SendJsonAsync(
                () => CreateRequest(HttpMethod.Get, EditUri(applicationId, editId), accessToken),
                cancellationToken).ConfigureAwait(false);
            editEtag = RequireEtag(editResponse.Response);

            progress?.Report("Validating Amazon Appstore edit.");
            await SendJsonAsync(
                () => CreateRequest(HttpMethod.Post, ValidateUri(applicationId, editId), accessToken),
                cancellationToken).ConfigureAwait(false);

            progress?.Report("Committing Amazon Appstore edit.");
            var commitResponse = await SendJsonAsync(
                () =>
                {
                    var request = CreateRequest(HttpMethod.Post, CommitUri(applicationId, editId), accessToken);
                    request.Headers.TryAddWithoutValidation("If-Match", editEtag);
                    return request;
                },
                cancellationToken).ConfigureAwait(false);
            committed = true;

            return new BundleDeploymentResult
            {
                Provider = Provider,
                Succeeded = true,
                ReleaseId = GetOptionalJsonProperty(commitResponse.Body, "id", "editId") ?? editId,
                Url = null,
                Message = "Amazon Appstore edit committed."
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException)
        {
            if (!committed && editId is not null && accessToken is not null)
                await TryDeleteEditAsync(applicationId, editId, accessToken, editEtag, cancellationToken).ConfigureAwait(false);

            return new BundleDeploymentResult
            {
                Provider = Provider,
                Succeeded = false,
                Message = new SecretRedactor(
                    accessToken is null ? secretValues : secretValues.Append(accessToken))
                    .Redact(ex.Message)
            };
        }
    }

    private async Task<string> RequestAccessTokenAsync(
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenUri)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["scope"] = "appstore::apps:readwrite"
            })
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await SendJsonAsync(() => request, cancellationToken).ConfigureAwait(false);
        return GetRequiredJsonProperty(response.Body, "access_token");
    }

    private async Task UploadApkAsync(
        string applicationId,
        string editId,
        string accessToken,
        string artifactPath,
        CancellationToken cancellationToken)
    {
        using var fileStream = new FileStream(
            artifactPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);
        using var request = CreateRequest(HttpMethod.Post, UploadApkUri(applicationId, editId), accessToken);
        request.Headers.TryAddWithoutValidation("fileName", Path.GetFileName(artifactPath));
        request.Content = new StreamContent(fileStream);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        await SendAsync(() => request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonResponse> SendJsonAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync(requestFactory, cancellationToken).ConfigureAwait(false);
        return new JsonResponse(response.Response, response.Body);
    }

    private async Task<ResponseEnvelope> SendAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        using var request = requestFactory();
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(BuildHttpErrorMessage(response, body), null, response.StatusCode);

        return new ResponseEnvelope(CloneResponseMetadata(response), body);
    }

    private async Task TryDeleteEditAsync(
        string applicationId,
        string editId,
        string accessToken,
        string? etag,
        CancellationToken cancellationToken)
    {
        try
        {
            var currentEtag = etag;
            if (string.IsNullOrWhiteSpace(currentEtag))
            {
                var editResponse = await SendAsync(
                    () => CreateRequest(HttpMethod.Get, EditUri(applicationId, editId), accessToken),
                    cancellationToken).ConfigureAwait(false);
                currentEtag = editResponse.Response.Headers.ETag?.ToString();
            }

            if (string.IsNullOrWhiteSpace(currentEtag))
                return;

            await SendAsync(
                () =>
                {
                    var request = CreateRequest(HttpMethod.Delete, EditUri(applicationId, editId), accessToken);
                    request.Headers.TryAddWithoutValidation("If-Match", currentEtag);
                    return request;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, Uri uri, string accessToken)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static HttpResponseMessage CloneResponseMetadata(HttpResponseMessage response)
    {
        var clone = new HttpResponseMessage(response.StatusCode)
        {
            ReasonPhrase = response.ReasonPhrase
        };
        foreach (var header in response.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return clone;
    }

    private static string BuildHttpErrorMessage(HttpResponseMessage response, string body)
    {
        var detail = GetOptionalJsonProperty(body, "message", "error_description", "error", "details")
            ?? FirstNonEmptyLine(body)
            ?? response.ReasonPhrase
            ?? "Request failed.";
        return $"Amazon Appstore API request failed with {(int)response.StatusCode} ({response.StatusCode}): {detail}";
    }

    private static string RequireEtag(HttpResponseMessage response) =>
        response.Headers.ETag?.ToString()
        ?? throw new InvalidOperationException("Amazon Appstore API did not return the edit ETag.");

    private static string GetRequiredJsonProperty(string? body, params string[] propertyNames) =>
        GetOptionalJsonProperty(body, propertyNames)
        ?? throw new InvalidOperationException($"Amazon Appstore API response did not contain '{propertyNames[0]}'.");

    private static string? GetOptionalJsonProperty(string? body, params string[] propertyNames)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        using var document = JsonDocument.Parse(body);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var propertyName in propertyNames)
        {
            if (document.RootElement.TryGetProperty(propertyName, out var property))
            {
                var value = property.ValueKind == JsonValueKind.String
                    ? property.GetString()
                    : property.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }

        return null;
    }

    private static string? FirstNonEmptyLine(string? body) =>
        body?.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

    private static Uri EditCollectionUri(string applicationId) =>
        new($"https://developer.amazon.com/api/appstore/v1/applications/{Uri.EscapeDataString(applicationId)}/edits");

    private static Uri EditUri(string applicationId, string editId) =>
        new($"https://developer.amazon.com/api/appstore/v1/applications/{Uri.EscapeDataString(applicationId)}/edits/{Uri.EscapeDataString(editId)}");

    private static Uri UploadApkUri(string applicationId, string editId) =>
        new($"https://developer.amazon.com/api/appstore/v1/applications/{Uri.EscapeDataString(applicationId)}/edits/{Uri.EscapeDataString(editId)}/apks/upload");

    private static Uri ValidateUri(string applicationId, string editId) =>
        new($"https://developer.amazon.com/api/appstore/v1/applications/{Uri.EscapeDataString(applicationId)}/edits/{Uri.EscapeDataString(editId)}/validate");

    private static Uri CommitUri(string applicationId, string editId) =>
        new($"https://developer.amazon.com/api/appstore/v1/applications/{Uri.EscapeDataString(applicationId)}/edits/{Uri.EscapeDataString(editId)}/commit");

    private static readonly Uri TokenUri = new("https://api.amazon.com/auth/o2/token");

    private sealed record ResponseEnvelope(HttpResponseMessage Response, string Body);
    private sealed record JsonResponse(HttpResponseMessage Response, string Body);
}
