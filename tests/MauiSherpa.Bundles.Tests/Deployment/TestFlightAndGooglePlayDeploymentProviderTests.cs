using System.Text.Json;
using FluentAssertions;
using MauiSherpa.Bundles.Tests.Deployment;

namespace MauiSherpa.Bundles.Tests;

public class TestFlightAndGooglePlayDeploymentProviderTests
{
    [Fact]
    public void TestFlight_Validate_ReportsPlatformArtifactAndApiKeyProblems()
    {
        var provider = new TestFlightDeploymentProvider(new FakeBundleProcessRunner());
        var context = DeploymentTestData.CreateContext(
            BundleDeploymentProvider.TestFlight,
            BundlePlatform.Android,
            artifactPath: "app.apk",
            kind: "apk",
            settings: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["apiKey"] = DeploymentTestData.Setting("ABC123"),
                ["apiIssuer"] = DeploymentTestData.Setting("issuer")
            });

        var errors = provider.Validate(context);

        errors.Should().Contain(error => error.Contains("does not support platform"));
        errors.Should().Contain(error => error.Contains("requires '.ipa'"));
        errors.Should().Contain(error => error.Contains("AppleApiKeyPath"));
    }

    [Fact]
    public void TestFlight_Validate_AcceptsArbitraryP8FileName()
    {
        using var workspace = new DeploymentTestWorkspace();
        var artifactPath = workspace.CreateFile("MyApp.ipa");
        var apiKeyPath = workspace.CreateFile("keys/custom-name.p8", "secret");
        var provider = new TestFlightDeploymentProvider(new FakeBundleProcessRunner());
        var context = DeploymentTestData.CreateContext(
            BundleDeploymentProvider.TestFlight,
            BundlePlatform.Ios,
            artifactPath,
            kind: "ipa",
            variables: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AppleApiKeyPath"] = apiKeyPath
            },
            settings: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["apiKey"] = DeploymentTestData.Setting("ABC123"),
                ["apiIssuer"] = DeploymentTestData.Setting("issuer-456")
            });

        var errors = provider.Validate(context);

        errors.Should().BeEmpty();
    }

    [Fact]
    public async Task TestFlight_DeployAsync_MaterializesKeyFileAndCleansUp()
    {
        using var workspace = new DeploymentTestWorkspace();
        var artifactPath = workspace.CreateFile("MyApp.ipa");
        var apiKeyPath = workspace.CreateFile("keys/custom-name.p8", "secret");
        string? materializedDirectory = null;
        var runner = new FakeBundleProcessRunner(request =>
        {
            materializedDirectory = request.Environment["API_PRIVATE_KEYS_DIR"];
            materializedDirectory.Should().StartWith(workspace.RootPath);
            Directory.Exists(materializedDirectory).Should().BeTrue();

            var copiedKeyPath = Path.Combine(materializedDirectory, "AuthKey_ABC123.p8");
            File.Exists(copiedKeyPath).Should().BeTrue();
            File.ReadAllText(copiedKeyPath).Should().Be("secret");
            File.Exists(apiKeyPath).Should().BeTrue();

            if (!OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(materializedDirectory);
                var disallowed = mode & ~(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                disallowed.Should().Be(0);
            }

            return new BundleProcessResult(
                0,
                "Upload complete https://appstoreconnect.apple.com/apps/123/testflight/ios",
                string.Empty);
        });
        var provider = new TestFlightDeploymentProvider(runner);
        var context = DeploymentTestData.CreateContext(
            BundleDeploymentProvider.TestFlight,
            BundlePlatform.Ios,
            artifactPath,
            kind: "ipa",
            variables: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AppleApiKeyPath"] = apiKeyPath
            },
            settings: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["apiKey"] = DeploymentTestData.Setting("ABC123"),
                ["apiIssuer"] = DeploymentTestData.Setting("issuer-456")
            });

        var result = await provider.DeployAsync(context);

        result.Succeeded.Should().BeTrue();
        result.Url.Should().Be("https://appstoreconnect.apple.com/apps/123/testflight/ios");
        runner.Requests.Should().ContainSingle();
        var request = runner.Requests[0];
        request.FileName.Should().Be("xcrun");
        request.Arguments.Should().Equal(
            "altool",
            "--upload-app",
            "-f", artifactPath,
            "-t", "ios",
            "--apiKey", "ABC123",
            "--apiIssuer", "issuer-456");
        request.Environment.Should().ContainKey("API_PRIVATE_KEYS_DIR")
            .WhoseValue.Should().Be(materializedDirectory);
        materializedDirectory.Should().NotBeNull();
        Directory.Exists(materializedDirectory!).Should().BeFalse();
        Directory.Exists(Path.Combine(workspace.RootPath, ".bundle-deployment", "apple-api-keys")).Should().BeFalse();
    }

    [Fact]
    public async Task GooglePlay_DeployAsync_UsesSupplyCommand()
    {
        using var workspace = new DeploymentTestWorkspace();
        var artifactPath = workspace.CreateFile("MyApp.aab");
        var serviceAccountPath = workspace.CreateFile("play-service-account.json", "{}");
        var runner = new FakeBundleProcessRunner(_ => new BundleProcessResult(
            0,
            "Release available at https://play.google.com/console/u/0/developers/1/app/2/releases/3",
            string.Empty));
        var provider = new GooglePlayDeploymentProvider(runner);
        var context = DeploymentTestData.CreateContext(
            BundleDeploymentProvider.GooglePlay,
            BundlePlatform.Android,
            artifactPath,
            kind: "aab",
            settings: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["executable"] = DeploymentTestData.Setting("bundle-fastlane"),
                ["packageName"] = DeploymentTestData.Setting("com.example.app"),
                ["track"] = DeploymentTestData.Setting("internal"),
                ["serviceAccountJsonPath"] = DeploymentTestData.Setting(serviceAccountPath)
            });

        var result = await provider.DeployAsync(context);

        result.Succeeded.Should().BeTrue();
        result.Url.Should().Be("https://play.google.com/console/u/0/developers/1/app/2/releases/3");
        runner.Requests.Should().ContainSingle();
        runner.Requests[0].FileName.Should().Be("bundle-fastlane");
        runner.Requests[0].Arguments.Should().Equal(
            "supply",
            "--aab", artifactPath,
            "--package_name", "com.example.app",
            "--track", "internal",
            "--json_key", serviceAccountPath);
    }
}
