using System.Security.Cryptography;
using FluentAssertions;

namespace MauiSherpa.Bundles.Tests.Build;

public class BundleBuildServiceBuildAsyncTests
{
    private static readonly IReadOnlyDictionary<string, string> Empty =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlySet<string> NoSecrets = new HashSet<string>(StringComparer.Ordinal);

    [Fact]
    public async Task BuildAsync_DryRun_DoesNotInvokeProcess_AndReturnsNoArtifacts()
    {
        using var workspace = new BundleBuildWorkspace();
        workspace.CreateProject("App.csproj", "net10.0-android");
        var service = new BundleBuildService(new RejectingBundleProcessRunner());

        var artifacts = await service.BuildAsync(
            BundlePlatform.Android,
            new BundleBuildConfiguration { Project = "App.csproj" },
            workspace.RootPath,
            workspace.CreateDirectory("out"),
            projectOverride: null,
            variables: Empty,
            preparationEnvironment: Empty,
            secretValues: NoSecrets,
            dryRun: true);

        artifacts.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildAsync_DryRun_WithInvalidPropertyName_ThrowsWithoutInvokingProcess()
    {
        using var workspace = new BundleBuildWorkspace();
        workspace.CreateProject("App.csproj", "net10.0-android");
        var service = new BundleBuildService(new RejectingBundleProcessRunner());
        var configuration = new BundleBuildConfiguration
        {
            Project = "App.csproj",
            Properties = new Dictionary<string, string> { ["Api-Version"] = "2" }
        };

        var act = () => service.BuildAsync(
            BundlePlatform.Android,
            configuration,
            workspace.RootPath,
            workspace.CreateDirectory("out"),
            projectOverride: null,
            variables: Empty,
            preparationEnvironment: Empty,
            secretValues: NoSecrets,
            dryRun: true);

        // Must fail with the validation error (surfaced even during a dry run), not the
        // RejectingBundleProcessRunner's "should not have been started" error - proving the
        // check runs before, and independently of, any process invocation.
        (await act.Should().ThrowAsync<BundleValidationException>())
            .Which.Errors.Should().ContainMatch("*Api-Version*");
    }

    [Fact]
    public async Task BuildAsync_WhenProcessFails_CombinesStandardOutputAndErrorAndIncludesExitCode()
    {
        using var workspace = new BundleBuildWorkspace();
        workspace.CreateProject("App.csproj", "net10.0-android");
        var runner = new FakeBundleProcessRunner(_ => new BundleProcessResult(
            ExitCode: 1,
            StandardOutput: "error CS1002: ; expected",
            StandardError: "MSBUILD : warning MSB1234: something incidental"));
        var service = new BundleBuildService(runner);

        var act = () => service.BuildAsync(
            BundlePlatform.Android,
            new BundleBuildConfiguration { Project = "App.csproj" },
            workspace.RootPath,
            workspace.CreateDirectory("out"),
            projectOverride: null,
            variables: Empty,
            preparationEnvironment: Empty,
            secretValues: NoSecrets,
            dryRun: false);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("exit code 1");
        // The real compiler/MSBuild error is typically on stdout; it must not be dropped in
        // favor of an incidental stderr warning.
        exception.Which.Message.Should().Contain("error CS1002");
        exception.Which.Message.Should().Contain("MSB1234");
    }

    [Fact]
    public async Task BuildAsync_Success_PersistsArtifacts_WithChecksumAndMetadataFromProperties()
    {
        using var workspace = new BundleBuildWorkspace();
        workspace.CreateProject("App.csproj", "net10.0-android");
        var artifactPath = Path.Combine(workspace.CreateDirectory("bin/Release/net10.0-android"), "App.apk");
        var artifactBytes = "hello-artifact"u8.ToArray();
        await File.WriteAllBytesAsync(artifactPath, artifactBytes);

        var runner = new FakeBundleProcessRunner(_ => new BundleProcessResult(0, string.Empty, string.Empty));
        var service = new BundleBuildService(runner);
        var variables = new Dictionary<string, string> { ["ApplicationId"] = "com.contoso.mobile" };
        var configuration = new BundleBuildConfiguration
        {
            Project = "App.csproj",
            // ApplicationDisplayVersion is supplied only via Properties, not Variables - the
            // returned artifact metadata must still pick it up.
            Properties = new Dictionary<string, string> { ["ApplicationDisplayVersion"] = "1.4.2" }
        };
        var outputDirectory = workspace.CreateDirectory("out");

        var artifacts = await service.BuildAsync(
            BundlePlatform.Android,
            configuration,
            workspace.RootPath,
            outputDirectory,
            projectOverride: null,
            variables: variables,
            preparationEnvironment: Empty,
            secretValues: NoSecrets,
            dryRun: false);

        artifacts.Should().HaveCount(1);
        var artifact = artifacts.Single();
        artifact.ApplicationId.Should().Be("com.contoso.mobile");
        artifact.Version.Should().Be("1.4.2");
        artifact.Kind.Should().Be("apk");
        artifact.Platform.Should().Be(BundlePlatform.Android);
        File.Exists(artifact.Path).Should().BeTrue();
        Path.GetDirectoryName(artifact.Path).Should().Be(Path.GetFullPath(outputDirectory));

        using var sha256 = SHA256.Create();
        var expectedHash = Convert.ToHexString(sha256.ComputeHash(artifactBytes)).ToLowerInvariant();
        artifact.Sha256.Should().Be(expectedHash);
    }

    [Fact]
    public async Task BuildAsync_WithNoMatchingArtifacts_ThrowsInvalidOperationException()
    {
        using var workspace = new BundleBuildWorkspace();
        workspace.CreateProject("App.csproj", "net10.0-android");
        var runner = new FakeBundleProcessRunner(_ => new BundleProcessResult(0, string.Empty, string.Empty));
        var service = new BundleBuildService(runner);

        var act = () => service.BuildAsync(
            BundlePlatform.Android,
            new BundleBuildConfiguration { Project = "App.csproj" },
            workspace.RootPath,
            workspace.CreateDirectory("out"),
            projectOverride: null,
            variables: Empty,
            preparationEnvironment: Empty,
            secretValues: NoSecrets,
            dryRun: false);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*No*artifacts were found*");
    }

    [Fact]
    public async Task BuildAsync_WithSameNamedArtifactsAtEqualTimestamps_AssignsDeterministicNames()
    {
        using var workspace = new BundleBuildWorkspace();
        workspace.CreateProject("App.csproj", "net10.0-android");
        var firstDirectory = workspace.CreateDirectory("bin/a");
        var secondDirectory = workspace.CreateDirectory("bin/b");
        var firstPath = Path.Combine(firstDirectory, "App.apk");
        var secondPath = Path.Combine(secondDirectory, "App.apk");
        await File.WriteAllTextAsync(firstPath, "first");
        await File.WriteAllTextAsync(secondPath, "second");
        // Force identical timestamps so ordering can only be resolved by the deterministic
        // ordinal path tiebreaker, not incidental filesystem mtime differences.
        var sharedTimestamp = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(firstPath, sharedTimestamp);
        File.SetLastWriteTimeUtc(secondPath, sharedTimestamp);

        var runner = new FakeBundleProcessRunner(_ => new BundleProcessResult(0, string.Empty, string.Empty));
        var service = new BundleBuildService(runner);
        var outputDirectory = workspace.CreateDirectory("out");

        var artifacts = await service.BuildAsync(
            BundlePlatform.Android,
            new BundleBuildConfiguration { Project = "App.csproj" },
            workspace.RootPath,
            outputDirectory,
            projectOverride: null,
            variables: Empty,
            preparationEnvironment: Empty,
            secretValues: NoSecrets,
            dryRun: false);

        artifacts.Should().HaveCount(2);
        // "bin/a/App.apk" sorts before "bin/b/App.apk" ordinally, so it deterministically wins
        // the canonical file name; the second gets a deterministic "-1" suffix, never a random
        // GUID.
        var canonical = Path.Combine(outputDirectory, "App.apk");
        var suffixed = Path.Combine(outputDirectory, "App-1.apk");
        File.Exists(canonical).Should().BeTrue();
        File.Exists(suffixed).Should().BeTrue();
        (await File.ReadAllTextAsync(canonical)).Should().Be("first");
        (await File.ReadAllTextAsync(suffixed)).Should().Be("second");
    }

    [Fact]
    public async Task BuildAsync_WhenOutputDirectoryAlreadyHasSameNamedFile_KeepsPriorFileAndAddsSuffixedCopy()
    {
        using var workspace = new BundleBuildWorkspace();
        workspace.CreateProject("App.csproj", "net10.0-android");
        var artifactPath = Path.Combine(workspace.CreateDirectory("bin"), "App.apk");
        await File.WriteAllTextAsync(artifactPath, "new-run");

        var outputDirectory = workspace.CreateDirectory("out");
        var priorRunPath = Path.Combine(outputDirectory, "App.apk");
        await File.WriteAllTextAsync(priorRunPath, "prior-run");

        var runner = new FakeBundleProcessRunner(_ => new BundleProcessResult(0, string.Empty, string.Empty));
        var service = new BundleBuildService(runner);

        await service.BuildAsync(
            BundlePlatform.Android,
            new BundleBuildConfiguration { Project = "App.csproj" },
            workspace.RootPath,
            outputDirectory,
            projectOverride: null,
            variables: Empty,
            preparationEnvironment: Empty,
            secretValues: NoSecrets,
            dryRun: false);

        // The prior run's artifact is never overwritten or deleted; the new copy lands alongside
        // it under a deterministic suffixed name.
        (await File.ReadAllTextAsync(priorRunPath)).Should().Be("prior-run");
        (await File.ReadAllTextAsync(Path.Combine(outputDirectory, "App-1.apk"))).Should().Be("new-run");
    }

    [Fact]
    public async Task BuildAsync_WhenCancelledAfterProcessCompletes_ThrowsAndPersistsNoArtifacts()
    {
        using var workspace = new BundleBuildWorkspace();
        workspace.CreateProject("App.csproj", "net10.0-android");
        await File.WriteAllBytesAsync(Path.Combine(workspace.CreateDirectory("bin"), "App.apk"), [1, 2, 3]);

        using var cts = new CancellationTokenSource();
        var runner = new FakeBundleProcessRunner(_ =>
        {
            // Simulate cancellation being requested while the build process is in flight: the
            // process still reports success, but the token is already cancelled by the time
            // BuildAsync resumes to discover/copy artifacts.
            cts.Cancel();
            return new BundleProcessResult(0, string.Empty, string.Empty);
        });
        var service = new BundleBuildService(runner);
        var outputDirectory = workspace.CreateDirectory("out");

        var act = () => service.BuildAsync(
            BundlePlatform.Android,
            new BundleBuildConfiguration { Project = "App.csproj" },
            workspace.RootPath,
            outputDirectory,
            projectOverride: null,
            variables: Empty,
            preparationEnvironment: Empty,
            secretValues: NoSecrets,
            dryRun: false,
            progress: null,
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        Directory.GetFiles(outputDirectory).Should().BeEmpty();
    }

    [Fact]
    public async Task BuildAsync_WhenAlreadyCancelledBeforeStarting_NeverInvokesProcess()
    {
        using var workspace = new BundleBuildWorkspace();
        workspace.CreateProject("App.csproj", "net10.0-android");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var service = new BundleBuildService(new RejectingBundleProcessRunner());

        var act = () => service.BuildAsync(
            BundlePlatform.Android,
            new BundleBuildConfiguration { Project = "App.csproj" },
            workspace.RootPath,
            workspace.CreateDirectory("out"),
            projectOverride: null,
            variables: Empty,
            preparationEnvironment: Empty,
            secretValues: NoSecrets,
            dryRun: false,
            progress: null,
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
