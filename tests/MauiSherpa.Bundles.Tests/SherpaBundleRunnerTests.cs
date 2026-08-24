using FluentAssertions;

namespace MauiSherpa.Bundles.Tests;

public class SherpaBundleRunnerTests
{
    [Fact]
    public async Task RunAsync_DryRun_ValidatesAllPhasesWithoutExecutingProcesses()
    {
        var source = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(
            Path.Combine(source, "App.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0-android</TargetFramework></PropertyGroup></Project>");
        try
        {
            var processRunner = new RejectingProcessRunner();
            var provider = new FakeDeploymentProvider();
            var runner = new SherpaBundleRunner(
                new BundleToolchainInstaller(processRunner),
                new BundleBuildService(processRunner),
                new BundleDeploymentRegistry([provider]));

            var result = await runner.RunAsync(
                CreateBundle(),
                new BundleRunRequest
                {
                    Environment = "production",
                    Platforms = [BundlePlatform.Android],
                    SourceDirectory = source,
                    DryRun = true
                });

            result.Succeeded.Should().BeTrue();
            result.Platforms.Single().Phases.Select(phase => phase.Phase)
                .Should().Equal(BundlePhase.Install, BundlePhase.Build, BundlePhase.Deploy);
            provider.DeployCount.Should().Be(1);
        }
        finally
        {
            Directory.Delete(source, recursive: true);
        }
    }

    [Fact]
    public void InferTargetFramework_SelectsPlatformFramework()
    {
        var project = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csproj");
        File.WriteAllText(
            project,
            "<Project><PropertyGroup><TargetFrameworks>net10.0-android;net10.0-ios</TargetFrameworks></PropertyGroup></Project>");
        try
        {
            BundleBuildService.InferTargetFramework(project, BundlePlatform.Ios)
                .Should().Be("net10.0-ios");
        }
        finally
        {
            File.Delete(project);
        }
    }

    [Fact]
    public void SecretRedactor_ReplacesLongerSecretsFirst()
    {
        var redactor = new SecretRedactor(["token", "token-value"]);

        redactor.Redact("Authorization: token-value; fallback token")
            .Should().Be("Authorization: ***; fallback ***");
    }

    private static SherpaBundle CreateBundle() => new()
    {
        Name = "Runner",
        Environments = new Dictionary<string, SherpaBundleEnvironment>
        {
            ["production"] = new()
            {
                Platforms = new Dictionary<BundlePlatform, BundlePlatformConfiguration>
                {
                    [BundlePlatform.Android] = new()
                    {
                        Build = new BundleBuildConfiguration { Project = "App.csproj" },
                        Deploy =
                        [
                            new BundleDeploymentTarget
                            {
                                Provider = BundleDeploymentProvider.GooglePlay
                            }
                        ]
                    }
                }
            }
        }
    };

    private sealed class RejectingProcessRunner : IBundleProcessRunner
    {
        public Task<BundleProcessResult> RunAsync(
            BundleProcessRequest request,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException($"Process '{request.FileName}' should not run during a dry run.");
    }

    private sealed class FakeDeploymentProvider : IBundleDeploymentProvider
    {
        public BundleDeploymentProvider Provider => BundleDeploymentProvider.GooglePlay;
        public int DeployCount { get; private set; }

        public IReadOnlyList<string> Validate(BundleDeploymentContext context) => [];

        public Task<BundleDeploymentResult> DeployAsync(
            BundleDeploymentContext context,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            DeployCount++;
            return Task.FromResult(new BundleDeploymentResult
            {
                Provider = Provider,
                Succeeded = true
            });
        }
    }
}
