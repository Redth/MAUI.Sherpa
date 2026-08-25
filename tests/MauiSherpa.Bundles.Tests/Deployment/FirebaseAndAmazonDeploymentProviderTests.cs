using System.Text.Json;
using System.Net;
using System.Net.Http;
using FluentAssertions;
using MauiSherpa.Bundles.Tests.Deployment;

namespace MauiSherpa.Bundles.Tests;

public class FirebaseAndAmazonDeploymentProviderTests
{
    [Fact]
    public async Task Firebase_DeployAsync_DryRunSkipsProcess()
    {
        var runner = new FakeBundleProcessRunner(_ => throw new InvalidOperationException("Should not run."));
        var provider = new FirebaseAppDistributionDeploymentProvider(runner);
        var context = DeploymentTestData.CreateContext(
            BundleDeploymentProvider.FirebaseAppDistribution,
            BundlePlatform.Android,
            artifactPath: "app.apk",
            kind: "apk",
            dryRun: true,
            settings: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["appId"] = DeploymentTestData.Setting("1:123:android:abc"),
                ["groups"] = JsonSerializer.SerializeToElement(new[] { "qa-team" }),
                ["releaseNotes"] = DeploymentTestData.Setting("Bug fixes")
            });

        var result = await provider.DeployAsync(context);

        result.Succeeded.Should().BeTrue();
        result.Message.Should().Contain("validated");
        runner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Firebase_DeployAsync_ReturnsFailureOnNonZeroExit()
    {
        using var workspace = new DeploymentTestWorkspace();
        var artifactPath = workspace.CreateFile("MyApp.apk");
        var runner = new FakeBundleProcessRunner(_ => new BundleProcessResult(2, string.Empty, "distribution failed"));
        var provider = new FirebaseAppDistributionDeploymentProvider(runner);
        var context = DeploymentTestData.CreateContext(
            BundleDeploymentProvider.FirebaseAppDistribution,
            BundlePlatform.Android,
            artifactPath,
            kind: "apk",
            settings: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["appId"] = DeploymentTestData.Setting("1:123:android:abc"),
                ["testers"] = DeploymentTestData.Setting("qa@example.com")
            });

        var result = await provider.DeployAsync(context);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("distribution failed");
        runner.Requests.Should().ContainSingle();
        runner.Requests[0].Arguments.Should().Equal(
            "appdistribution:distribute",
            artifactPath,
            "--app", "1:123:android:abc",
            "--testers", "qa@example.com");
    }

    [Fact]
    public void Amazon_Validate_RejectsAabAndMissingCredentials()
    {
        var provider = new AmazonAppstoreDeploymentProvider(new FakeBundleProcessRunner());
        var context = DeploymentTestData.CreateContext(
            BundleDeploymentProvider.AmazonAppstore,
            BundlePlatform.Android,
            artifactPath: "MyApp.aab",
            kind: "aab",
            settings: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["applicationId"] = DeploymentTestData.Setting("amzn1.devportal.mobileapp.123")
            });

        var errors = provider.Validate(context);

        errors.Should().Contain(error => error.Contains("'.apk'"));
        errors.Should().Contain(error => error.Contains("clientId"));
        errors.Should().Contain(error => error.Contains("clientSecret"));
    }

    [Fact]
    public async Task Amazon_DeployAsync_UsesOfficialRestFlowAndExpandsVariables()
    {
        using var workspace = new DeploymentTestWorkspace();
        var artifactPath = workspace.CreateFile("MyApp.apk", "apk-binary");
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            if (request.Uri.AbsoluteUri == "https://api.amazon.com/auth/o2/token")
            {
                request.Method.Should().Be(HttpMethod.Post);
                request.BodyText.Should().Contain("client_id=client-123");
                request.BodyText.Should().Contain("client_secret=super-secret");
                request.BodyText.Should().Contain("scope=appstore%3A%3Aapps%3Areadwrite");
                return FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, """{"access_token":"token-1"}""");
            }

            request.Headers["Authorization"].Single().Should().StartWith("Bearer ");

            return request.Uri.AbsolutePath switch
            {
                "/api/appstore/v1/applications/amzn1.devportal.mobileapp.123/edits"
                    when request.Method == HttpMethod.Post
                    => FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, """{"id":"edit-123"}"""),
                "/api/appstore/v1/applications/amzn1.devportal.mobileapp.123/edits/edit-123/apks/upload"
                    when request.Method == HttpMethod.Post
                    => ValidateUpload(request, artifactPath),
                "/api/appstore/v1/applications/amzn1.devportal.mobileapp.123/edits/edit-123"
                    when request.Method == HttpMethod.Get
                    => FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, """{"id":"edit-123"}""", "\"etag-123\""),
                "/api/appstore/v1/applications/amzn1.devportal.mobileapp.123/edits/edit-123/validate"
                    when request.Method == HttpMethod.Post
                    => FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, """{"id":"edit-123"}"""),
                "/api/appstore/v1/applications/amzn1.devportal.mobileapp.123/edits/edit-123/commit"
                    when request.Method == HttpMethod.Post
                    => ValidateCommit(request),
                _ => throw new Xunit.Sdk.XunitException($"Unexpected request: {request.Method} {request.Uri}")
            };
        });

        var provider = CreateAmazonProvider(new HttpClient(handler));
        var context = DeploymentTestData.CreateContext(
            BundleDeploymentProvider.AmazonAppstore,
            BundlePlatform.Android,
            artifactPath,
            kind: "apk",
            variables: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AmazonClientId"] = "client-123",
                ["AmazonClientSecret"] = "super-secret",
                ["AppSuffix"] = "123"
            },
            settings: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["clientId"] = DeploymentTestData.Setting("${AmazonClientId}"),
                ["clientSecret"] = DeploymentTestData.Setting("{{ AmazonClientSecret }}"),
                ["applicationId"] = DeploymentTestData.Setting("${ResolvedApplicationId}")
            },
            targetVariables: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ResolvedApplicationId"] = "amzn1.devportal.mobileapp.${AppSuffix}"
            });

        var result = await provider.DeployAsync(context);

        result.Succeeded.Should().BeTrue();
        result.ReleaseId.Should().Be("edit-123");
        handler.Requests.Should().HaveCount(6);
    }

    [Fact]
    public async Task Amazon_DeployAsync_DeletesEditOnFailureAndRedactsSecrets()
    {
        using var workspace = new DeploymentTestWorkspace();
        var artifactPath = workspace.CreateFile("MyApp.apk", "apk-binary");
        var secret = "super-secret";
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            if (request.Uri.AbsoluteUri == "https://api.amazon.com/auth/o2/token")
                return FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, """{"access_token":"token-1"}""");

            return request.Uri.AbsolutePath switch
            {
                "/api/appstore/v1/applications/amzn1.devportal.mobileapp.123/edits"
                    when request.Method == HttpMethod.Post
                    => FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, """{"id":"edit-123"}"""),
                "/api/appstore/v1/applications/amzn1.devportal.mobileapp.123/edits/edit-123/apks/upload"
                    when request.Method == HttpMethod.Post
                    => FakeHttpMessageHandler.EmptyResponse(HttpStatusCode.OK),
                "/api/appstore/v1/applications/amzn1.devportal.mobileapp.123/edits/edit-123"
                    when request.Method == HttpMethod.Get
                    => FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, """{"id":"edit-123"}""", "\"etag-123\""),
                "/api/appstore/v1/applications/amzn1.devportal.mobileapp.123/edits/edit-123/validate"
                    when request.Method == HttpMethod.Post
                    => FakeHttpMessageHandler.JsonResponse(HttpStatusCode.Forbidden, $$"""{"message":"validation failed for {{secret}}"}"""),
                "/api/appstore/v1/applications/amzn1.devportal.mobileapp.123/edits/edit-123"
                    when request.Method == HttpMethod.Delete
                    => request.Headers["If-Match"].Single() == "\"etag-123\""
                        ? FakeHttpMessageHandler.EmptyResponse(HttpStatusCode.NoContent)
                        : throw new Xunit.Sdk.XunitException("Delete missing If-Match."),
                _ => throw new Xunit.Sdk.XunitException($"Unexpected request: {request.Method} {request.Uri}")
            };
        });

        var provider = CreateAmazonProvider(new HttpClient(handler));
        var context = DeploymentTestData.CreateContext(
            BundleDeploymentProvider.AmazonAppstore,
            BundlePlatform.Android,
            artifactPath,
            kind: "apk",
            settings: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["clientId"] = DeploymentTestData.Setting("client-123"),
                ["clientSecret"] = DeploymentTestData.Setting(secret),
                ["applicationId"] = DeploymentTestData.Setting("amzn1.devportal.mobileapp.123")
            });

        var result = await provider.DeployAsync(context);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("validation failed");
        result.Message.Should().NotContain(secret);
        handler.Requests.Should().ContainSingle(request => request.Method == HttpMethod.Delete);
    }

    private static HttpResponseMessage ValidateUpload(RecordedHttpRequest request, string artifactPath)
    {
        request.Headers["fileName"].Single().Should().Be(Path.GetFileName(artifactPath));
        request.ContentHeaders["Content-Type"].Single().Should().Be("application/octet-stream");
        request.BodyBytes.Should().Equal(File.ReadAllBytes(artifactPath));
        return FakeHttpMessageHandler.EmptyResponse(HttpStatusCode.OK);
    }

    private static HttpResponseMessage ValidateCommit(RecordedHttpRequest request)
    {
        request.Headers["If-Match"].Single().Should().Be("\"etag-123\"");
        return FakeHttpMessageHandler.JsonResponse(HttpStatusCode.OK, """{"id":"edit-123"}""");
    }

    private static AmazonAppstoreDeploymentProvider CreateAmazonProvider(HttpClient httpClient) =>
        (AmazonAppstoreDeploymentProvider)typeof(AmazonAppstoreDeploymentProvider)
            .GetConstructor(
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                binder: null,
                [typeof(IBundleProcessRunner), typeof(HttpClient)],
                modifiers: null)!
            .Invoke([new FakeBundleProcessRunner(), httpClient]);
}
