using System.Text.Json;
using System.Net;
using System.Net.Http.Headers;

namespace MauiSherpa.Bundles.Tests.Deployment;

internal sealed class FakeBundleProcessRunner(Func<BundleProcessRequest, BundleProcessResult>? handler = null)
    : IBundleProcessRunner
{
    private readonly Func<BundleProcessRequest, BundleProcessResult> _handler = handler ?? (_ => new BundleProcessResult(0, string.Empty, string.Empty));

    public List<BundleProcessRequest> Requests { get; } = [];

    public Task<BundleProcessResult> RunAsync(
        BundleProcessRequest request,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return Task.FromResult(_handler(request));
    }
}

internal sealed class DeploymentTestWorkspace : IDisposable
{
    public DeploymentTestWorkspace()
    {
        RootPath = Path.Combine(AppContext.BaseDirectory, "deployment-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }

    public string CreateFile(string relativePath, string content = "test")
    {
        var path = Path.Combine(RootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(RootPath))
            Directory.Delete(RootPath, recursive: true);
    }
}

internal static class DeploymentTestData
{
    public static BundleDeploymentContext CreateContext(
        BundleDeploymentProvider provider,
        BundlePlatform platform,
        string artifactPath,
        string kind,
        bool dryRun = false,
        IReadOnlyDictionary<string, string>? variables = null,
        IReadOnlyDictionary<string, JsonElement>? settings = null,
        IReadOnlyDictionary<string, string>? targetVariables = null) =>
        new()
        {
            Platform = platform,
            DryRun = dryRun,
            WorkingDirectory = Path.GetDirectoryName(artifactPath) ?? AppContext.BaseDirectory,
            SecretValues = new HashSet<string>(StringComparer.Ordinal),
            Variables = variables ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Artifact = new BundleArtifact
            {
                Path = artifactPath,
                Platform = platform,
                Kind = kind
            },
            Target = new BundleDeploymentTarget
            {
                Provider = provider,
                Variables = targetVariables is null
                    ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(targetVariables, StringComparer.OrdinalIgnoreCase),
                Settings = settings is null
                    ? new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, JsonElement>(settings, StringComparer.OrdinalIgnoreCase)
            }
        };

    public static JsonElement Setting(string value) => JsonSerializer.SerializeToElement(value);
}

internal sealed class RecordedHttpRequest
{
    public required HttpMethod Method { get; init; }
    public required Uri Uri { get; init; }
    public Dictionary<string, string[]> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string[]> ContentHeaders { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string? BodyText { get; init; }
    public byte[]? BodyBytes { get; init; }
}

internal sealed class FakeHttpMessageHandler(
    Func<RecordedHttpRequest, CancellationToken, HttpResponseMessage> handler)
    : HttpMessageHandler
{
    public List<RecordedHttpRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var bodyBytes = request.Content is null
            ? null
            : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var recorded = new RecordedHttpRequest
        {
            Method = request.Method,
            Uri = request.RequestUri!,
            Headers = request.Headers.ToDictionary(
                header => header.Key,
                header => header.Value.ToArray(),
                StringComparer.OrdinalIgnoreCase),
            ContentHeaders = request.Content?.Headers.ToDictionary(
                header => header.Key,
                header => header.Value.ToArray(),
                StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase),
            BodyBytes = bodyBytes,
            BodyText = bodyBytes is null ? null : TryReadUtf8(bodyBytes, request.Content?.Headers.ContentType)
        };
        Requests.Add(recorded);
        return handler(recorded, cancellationToken);
    }

    public static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json, string? etag = null)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        if (!string.IsNullOrWhiteSpace(etag))
            response.Headers.ETag = new EntityTagHeaderValue(etag);
        return response;
    }

    public static HttpResponseMessage EmptyResponse(HttpStatusCode statusCode, string? etag = null)
    {
        var response = new HttpResponseMessage(statusCode);
        if (!string.IsNullOrWhiteSpace(etag))
            response.Headers.ETag = new EntityTagHeaderValue(etag);
        return response;
    }

    private static string? TryReadUtf8(byte[] bytes, MediaTypeHeaderValue? contentType)
    {
        if (bytes.Length == 0)
            return string.Empty;

        if (contentType?.MediaType?.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase) == true)
            return null;

        return System.Text.Encoding.UTF8.GetString(bytes);
    }
}
