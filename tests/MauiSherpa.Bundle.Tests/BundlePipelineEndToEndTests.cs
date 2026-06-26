using System.Text;
using FluentAssertions;
using MauiSherpa.Bundle.Loading;
using MauiSherpa.Bundle.Models;
using MauiSherpa.Bundle.Pipeline;

namespace MauiSherpa.Bundle.Tests;

/// <summary>
/// End-to-end tests for the consume side that <c>sherpacli</c> drives:
/// <see cref="SherpaPipeline"/> loading a bundle (encrypted or plain JSON),
/// decrypting with the password, selecting the environment/platform, and issuing
/// the toolchain commands. A <see cref="RecordingProcessRunner"/> stands in for
/// <c>dotnet</c>/<c>security</c>/<c>xcodebuild</c> so no real build runs.
/// </summary>
public sealed class BundlePipelineEndToEndTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        foreach (var d in _tempDirs)
            try { if (Directory.Exists(d)) Directory.Delete(d, recursive: true); } catch { /* best effort */ }
    }

    private string NewProjectDir(string targetFrameworks)
    {
        var dir = Directory.CreateTempSubdirectory("sherpa-pipe-").FullName;
        _tempDirs.Add(dir);
        File.WriteAllText(
            Path.Combine(dir, "App.csproj"),
            $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFrameworks>{targetFrameworks}</TargetFrameworks></PropertyGroup></Project>");
        return dir;
    }

    private static SherpaRunOptions Options(
        string bundlePath, string projectDir, SherpaPlatform platform, string? password, params SherpaStep[] steps) => new()
    {
        BundlePath = bundlePath,
        Environment = "Production",
        Platforms = new[] { platform },
        Steps = steps,
        ProjectPath = Path.Combine(projectDir, "App.csproj"),
        Password = password,
    };

    private static SherpaBundle IOSBundle() => new()
    {
        Environments = new Dictionary<string, EnvironmentBlock>(StringComparer.OrdinalIgnoreCase)
        {
            ["Production"] = new EnvironmentBlock { IOS = new ApplePlatform() },
        },
    };

    private static SherpaBundle AndroidBundleWithKeystore() => new()
    {
        Environments = new Dictionary<string, EnvironmentBlock>(StringComparer.OrdinalIgnoreCase)
        {
            ["Production"] = new EnvironmentBlock
            {
                Android = new AndroidPlatform
                {
                    Setup = new AndroidSetup
                    {
                        Keystores = new List<Keystore>
                        {
                            new()
                            {
                                Content = Convert.ToBase64String(Encoding.UTF8.GetBytes("JKS-BYTES")),
                                KeyAlias = "upload",
                                StorePassword = "store-pass",
                                KeyPassword = "key-pass",
                            },
                        },
                    },
                },
            },
        },
    };

    [Fact]
    public async Task EncryptedBundle_buildStep_decrypts_and_invokes_publish_for_platform_tfm()
    {
        var dir = NewProjectDir("net10.0-android;net10.0-ios");
        var bundlePath = Path.Combine(dir, "app.sherpabundle");
        await SqlCipherBundleStore.SaveAsync(IOSBundle(), bundlePath, "pw");

        var process = new RecordingProcessRunner();
        var result = await new SherpaPipeline(process: process)
            .RunAsync(Options(bundlePath, dir, SherpaPlatform.IOS, "pw", SherpaStep.Build), NullSherpaLog.Instance);

        result.Environment.Should().Be("Production");
        result.Platforms.Should().ContainKey("iOS");

        var publish = process.AllOf("dotnet").Single(i => i.Arguments.Contains("publish"));
        publish.Arguments.Should().Contain("net10.0-ios");
        process.AllOf("dotnet").Should().Contain(i => i.Arguments.Contains("restore")); // workload restore (inference)
    }

    [Fact]
    public async Task EncryptedBundle_setupAndBuild_flows_android_keystore_into_signing_properties()
    {
        var dir = NewProjectDir("net10.0-android");
        var bundlePath = Path.Combine(dir, "app.sherpabundle");
        await SqlCipherBundleStore.SaveAsync(AndroidBundleWithKeystore(), bundlePath, "pw");

        var process = new RecordingProcessRunner();
        await new SherpaPipeline(process: process).RunAsync(
            Options(bundlePath, dir, SherpaPlatform.Android, "pw", SherpaStep.Setup, SherpaStep.Build),
            NullSherpaLog.Instance);

        var publish = process.AllOf("dotnet").Single(i => i.Arguments.Contains("publish"));
        publish.Arguments.Should().Contain("net10.0-android");
        // The keystore the bundle carried was materialized and passed to the build.
        publish.Arguments.Should().Contain(a => a.StartsWith("-p:AndroidSigningKeyStore="));
        publish.Arguments.Should().Contain("-p:AndroidSigningStorePass=store-pass");
        publish.Arguments.Should().Contain("-p:AndroidSigningKeyAlias=upload");
    }

    [Fact]
    public async Task EncryptedBundle_wrongPassword_fails_before_building()
    {
        var dir = NewProjectDir("net10.0-ios");
        var bundlePath = Path.Combine(dir, "app.sherpabundle");
        await SqlCipherBundleStore.SaveAsync(IOSBundle(), bundlePath, "correct");

        var process = new RecordingProcessRunner();
        var act = () => new SherpaPipeline(process: process)
            .RunAsync(Options(bundlePath, dir, SherpaPlatform.IOS, "wrong", SherpaStep.Build), NullSherpaLog.Instance);

        await act.Should().ThrowAsync<SherpaBundleException>();
        process.Invocations.Should().BeEmpty(); // never reached the toolchain
    }

    [Fact]
    public async Task EncryptedBundle_missingPassword_is_a_clear_error()
    {
        var dir = NewProjectDir("net10.0-ios");
        var bundlePath = Path.Combine(dir, "app.sherpabundle");
        await SqlCipherBundleStore.SaveAsync(IOSBundle(), bundlePath, "pw");

        var act = () => new SherpaPipeline(process: new RecordingProcessRunner())
            .RunAsync(Options(bundlePath, dir, SherpaPlatform.IOS, password: null, SherpaStep.Build), NullSherpaLog.Instance);

        (await act.Should().ThrowAsync<SherpaBundleException>())
            .Which.Message.Should().Contain("encrypted");
    }

    [Fact]
    public async Task PlainJsonBundle_still_loads_through_the_pipeline()
    {
        var dir = NewProjectDir("net10.0-android");
        var bundlePath = Path.Combine(dir, "app.sherpabundle");
        await File.WriteAllTextAsync(bundlePath, "{ \"Production\": { \"Android\": {} } }");

        var process = new RecordingProcessRunner();
        var result = await new SherpaPipeline(process: process).RunAsync(
            Options(bundlePath, dir, SherpaPlatform.Android, password: null, SherpaStep.Build),
            NullSherpaLog.Instance);

        result.Platforms.Should().ContainKey("Android");
        process.AllOf("dotnet").Should().Contain(i => i.Arguments.Contains("publish"));
    }
}
