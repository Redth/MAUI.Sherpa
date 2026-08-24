using System.Text.Json;
using FluentAssertions;
using MauiSherpa.Bundles;
using MauiSherpa.Cli;

namespace MauiSherpa.Cli.Tests;

[Collection("Console")]
public class BundleCommandTests
{
    [Fact]
    public async Task Features_Manifest_IncludesBundleCapabilities()
    {
        var result = await CliTestHost.InvokeAsync(["features"]);

        result.ExitCode.Should().Be(0, "stdout: {0}\nstderr: {1}", result.StdOut, result.StdErr);
        using var json = JsonDocument.Parse(result.StdOut);
        json.RootElement.GetProperty("description").GetString()
            .Should().Contain("Expedition Packs");

        var bundles = json.RootElement.GetProperty("features")
            .EnumerateArray()
            .Single(feature => feature.GetProperty("id").GetString() == "expedition-packs");

        bundles.GetProperty("commands").EnumerateArray()
            .Select(command => command.GetProperty("command").GetString())
            .Should()
            .Contain([
                "maui-sherpa pack validate <file> [--password-stdin]",
                "maui-sherpa pack split <file> [--max-value-length 44000] [--output <dir>]",
                "maui-sherpa pack install <file> --environment <name> [-p <platform>...]",
                "maui-sherpa pack build <file> --environment <name> [--source <dir>] [--project <path>] [--output <dir>]",
                "maui-sherpa pack deploy <file> --environment <name> --artifact <path>",
                "maui-sherpa pack run <file> --environment <name> [--phase <phase>...] [--parallel]"
            ]);
        bundles.GetProperty("options").EnumerateArray()
            .Select(option => option.GetProperty("option").GetString())
            .Should()
            .Contain(["--password-stdin", "--from-env", "--pack-env-prefix", "--environment, -e", "--platform, -p", "--json / --agent"]);
    }

    [Fact]
    public async Task Validate_WithPasswordStdin_PrefersStdinAndReturnsStructuredSuccess()
    {
        using var workspace = TestWorkspace.Create();
        var bundlePath = workspace.WriteBundle(CreateBundle(), "stdin-password");

        var result = await CliTestHost.InvokeAsync(
            ["pack", "validate", bundlePath, "--password-stdin", "--json"],
            stdin: "stdin-password\n",
            environmentPassword: "wrong-password");

        result.ExitCode.Should().Be(0, "stdout: {0}\nstderr: {1}", result.StdOut, result.StdErr);
        result.StdErr.Should().BeEmpty();

        using var json = JsonDocument.Parse(result.StdOut);
        json.RootElement.GetProperty("status").GetString().Should().Be("ok");
        json.RootElement.GetProperty("command").GetString().Should().Be("pack.validate");
        json.RootElement.GetProperty("exitCode").GetInt32().Should().Be(0);

        var payload = json.RootElement.GetProperty("result");
        payload.GetProperty("valid").GetBoolean().Should().BeTrue();
        payload.GetProperty("name").GetString().Should().Be("CLI");
        payload.GetProperty("environments").EnumerateArray().Select(item => item.GetString())
            .Should().Contain("production");
    }

    [Theory]
    [InlineData("--json")]
    [InlineData("--agent")]
    public async Task Validate_WithoutPassword_ReturnsStructuredError(string flag)
    {
        using var workspace = TestWorkspace.Create();
        var bundlePath = workspace.WriteBundle(CreateBundle(), "password");

        var result = await CliTestHost.InvokeAsync(["pack", "validate", bundlePath, flag]);

        result.ExitCode.Should().Be(2);
        using var json = JsonDocument.Parse(result.StdOut);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("command").GetString().Should().Be("pack.validate");
        json.RootElement.GetProperty("exitCode").GetInt32().Should().Be(2);
        json.RootElement.GetProperty("error").GetProperty("message").GetString()
            .Should().Be("Set SHERPA_PACK_PASSWORD or pass --password-stdin.");
        result.StdErr.Should().BeEmpty();
    }

    [Fact]
    public async Task Run_MissingRequiredEnvironment_ReturnsStructuredParseError()
    {
        var result = await CliTestHost.InvokeAsync(["pack", "run", "pack.sherpapack", "--json"]);

        result.ExitCode.Should().Be(2);
        using var json = JsonDocument.Parse(result.StdOut);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("command").GetString().Should().Be("pack.run");
        json.RootElement.GetProperty("exitCode").GetInt32().Should().Be(2);
        json.RootElement.GetProperty("errors").EnumerateArray()
            .Select(error => error.GetString())
            .Should().Contain(message => message!.Contains("--environment", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Install_DryRun_ReturnsStructuredSuccess()
    {
        using var workspace = TestWorkspace.Create();
        Directory.CreateDirectory(workspace.GetPath("source"));
        var bundlePath = workspace.WriteBundle(CreateBundle(), "password");

        var result = await CliTestHost.InvokeAsync(
            ["pack", "install", bundlePath, "-e", "production", "--source", workspace.GetPath("source"), "--dry-run", "--json"],
            environmentPassword: "password");

        result.ExitCode.Should().Be(0);
        using var json = JsonDocument.Parse(result.StdOut);
        var phase = GetSinglePhase(json.RootElement.GetProperty("result"));
        phase.GetProperty("phase").GetString().Should().Be("Install");
        phase.GetProperty("message").GetString().Should().Be("Install plan validated.");
    }

    [Fact]
    public async Task Build_DryRun_UsesSourceProjectAndRepeatedEnvironment()
    {
        using var workspace = TestWorkspace.Create();
        workspace.CreateProject("src/App/App.csproj", "net10.0-android");
        workspace.CreateProject("src/Other/Other.csproj", "net10.0-android");
        var bundlePath = workspace.WriteBundle(CreateBundle(), "password");
        var outputPath = workspace.GetPath("artifacts", "persisted");

        var result = await CliTestHost.InvokeAsync(
            [
                "pack", "build", bundlePath,
                "-e", "production",
                "-e", "production",
                "--source", workspace.GetPath("src"),
                "--project", "App/App.csproj",
                "--output", outputPath,
                "--dry-run",
                "--json"
            ],
            environmentPassword: "password");

        result.ExitCode.Should().Be(0);
        using var json = JsonDocument.Parse(result.StdOut);
        json.RootElement.GetProperty("status").GetString().Should().Be("ok");
        json.RootElement.GetProperty("command").GetString().Should().Be("pack.build");
        GetSinglePhase(json.RootElement.GetProperty("result")).GetProperty("phase").GetString().Should().Be("Build");
    }

    [Fact]
    public async Task Deploy_WithoutArtifact_ReturnsDeterministicFailure()
    {
        using var workspace = TestWorkspace.Create();
        Directory.CreateDirectory(workspace.GetPath("source"));
        var bundlePath = workspace.WriteBundle(CreateBundle(withDeployment: true), "password");

        var result = await CliTestHost.InvokeAsync(
            ["pack", "deploy", bundlePath, "-e", "production", "--source", workspace.GetPath("source"), "--json"],
            environmentPassword: "password");

        result.ExitCode.Should().Be(1);
        using var json = JsonDocument.Parse(result.StdOut);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("command").GetString().Should().Be("pack.deploy");
        json.RootElement.GetProperty("exitCode").GetInt32().Should().Be(1);

        var phase = GetSinglePhase(json.RootElement.GetProperty("result"));
        phase.GetProperty("phase").GetString().Should().Be("Deploy");
        phase.GetProperty("message").GetString()
            .Should().Contain("Deploy requires a build artifact from the current run or an explicit artifact path.");
    }

    [Fact]
    public async Task Run_DryRun_ParsesRepeatedOptionsAndParallelFlag()
    {
        using var workspace = TestWorkspace.Create();
        workspace.CreateProject("src/App.csproj", "net10.0-android");
        var bundlePath = workspace.WriteBundle(CreateBundle(withDeployment: true), "password");

        var result = await CliTestHost.InvokeAsync(
            [
                "pack", "run", bundlePath,
                "-e", "production",
                "-e", "production",
                "-p", "android",
                "-p", "android",
                "--phase", "build",
                "--phase", "deploy",
                "--variable", "BuildNumber=42",
                "--variable", "ApplicationDisplayVersion=1.2.3",
                "--parallel",
                "--source", workspace.GetPath("src"),
                "--project", "App.csproj",
                "--output", workspace.GetPath("artifacts"),
                "--dry-run",
                "--json"
            ],
            environmentPassword: "password");

        result.ExitCode.Should().Be(0);
        using var json = JsonDocument.Parse(result.StdOut);
        json.RootElement.GetProperty("status").GetString().Should().Be("ok");
        json.RootElement.GetProperty("command").GetString().Should().Be("pack.run");

        var phases = GetPhases(json.RootElement.GetProperty("result"))
            .Select(phase => phase.GetProperty("phase").GetString())
            .ToArray();
        phases.Should().Equal("Build", "Deploy");
    }

    [Fact]
    public async Task Validate_WhenCancelled_ReturnsStructuredCancellation()
    {
        using var workspace = TestWorkspace.Create();
        var bundlePath = workspace.WriteBundle(CreateBundle(), "password");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await CliTestHost.InvokeAsync(
            ["pack", "validate", bundlePath, "--json"],
            environmentPassword: "password",
            cancellationToken: cts.Token);

        result.ExitCode.Should().Be(130);
        using var json = JsonDocument.Parse(result.StdOut);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("command").GetString().Should().Be("pack.validate");
        json.RootElement.GetProperty("exitCode").GetInt32().Should().Be(130);
    }

    [Fact]
    public async Task BundleAlias_AndLegacyPasswordVariable_RemainCompatible()
    {
        using var workspace = TestWorkspace.Create();
        var packPath = workspace.WriteBundle(CreateBundle(), "legacy-password");

        var result = await CliTestHost.InvokeAsync(
            ["bundle", "validate", packPath, "--json"],
            legacyEnvironmentPassword: "legacy-password");

        result.ExitCode.Should().Be(0);
        using var json = JsonDocument.Parse(result.StdOut);
        json.RootElement.GetProperty("command").GetString().Should().Be("pack.validate");
    }

    [Fact]
    public async Task Validate_FromNumberedEnvironmentParts_ReassemblesPack()
    {
        using var workspace = TestWorkspace.Create();
        var packPath = workspace.WriteBundle(CreateBundle(), "password");
        var packBytes = File.ReadAllBytes(packPath);
        var parts = SherpaPackText.Split(packBytes, "CUSTOM_PACK", maximumValueLength: 300);

        var result = await CliTestHost.InvokeAsync(
            ["pack", "validate", "--from-env", "--pack-env-prefix", "CUSTOM_PACK", "--json"],
            environmentPassword: "password",
            environment: parts.ToDictionary(part => part.Name, part => part.Value));

        result.ExitCode.Should().Be(0);
        using var json = JsonDocument.Parse(result.StdOut);
        json.RootElement.GetProperty("result").GetProperty("valid").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Split_WithOutput_WritesSecretFilesAndWorkflowSnippet()
    {
        using var workspace = TestWorkspace.Create();
        var packPath = workspace.WriteBundle(CreateBundle() with
        {
            Variables = new Dictionary<string, string>
            {
                ["Entropy"] = Convert.ToBase64String(
                    System.Security.Cryptography.RandomNumberGenerator.GetBytes(4096))
            }
        }, "password");
        var output = workspace.GetPath("secrets");

        var result = await CliTestHost.InvokeAsync(
            ["pack", "split", packPath, "--max-value-length", "300", "--output", output, "--json"]);

        result.ExitCode.Should().Be(0);
        using var json = JsonDocument.Parse(result.StdOut);
        json.RootElement.GetProperty("result").GetProperty("partCount").GetInt32()
            .Should().BeGreaterThan(1);
        File.Exists(Path.Combine(output, "SHERPA_PACK_1.txt")).Should().BeTrue();
        var snippet = await File.ReadAllTextAsync(Path.Combine(output, "github-actions-env.yml"));
        snippet.Should().Contain("SHERPA_PACK_1: ${{ secrets.SHERPA_PACK_1 }}")
            .And.Contain("SHERPA_PACK_PASSWORD");
    }

    private static JsonElement GetSinglePhase(JsonElement result) => GetPhases(result).Single();

    private static JsonElement[] GetPhases(JsonElement result) =>
        result.GetProperty("platforms").EnumerateArray().First().GetProperty("phases").EnumerateArray().ToArray();

    private static SherpaBundle CreateBundle(bool withDeployment = false) => new()
    {
        Name = "CLI",
        Environments = new Dictionary<string, SherpaBundleEnvironment>
        {
            ["production"] = new()
            {
                Platforms = new Dictionary<BundlePlatform, BundlePlatformConfiguration>
                {
                    [BundlePlatform.Android] = new()
                    {
                        Build = new BundleBuildConfiguration
                        {
                            Project = "App.csproj"
                        },
                        Deploy = withDeployment
                            ?
                            [
                                new BundleDeploymentTarget
                                {
                                    Provider = BundleDeploymentProvider.GooglePlay,
                                    Artifact = "MyApp.aab",
                                    Variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                                    {
                                        ["packageName"] = "com.example.app",
                                        ["track"] = "internal",
                                        ["serviceAccountJsonPath"] = "service-account.json"
                                    }
                                }
                            ]
                            : []
                    }
                }
            }
        }
    };
}

[CollectionDefinition("Console", DisableParallelization = true)]
public sealed class ConsoleCollection;

internal sealed class CliInvocationResult(int exitCode, string stdOut, string stdErr)
{
    public int ExitCode { get; } = exitCode;
    public string StdOut { get; } = stdOut;
    public string StdErr { get; } = stdErr;
}

internal static class CliTestHost
{
    public static async Task<CliInvocationResult> InvokeAsync(
        string[] args,
        string? stdin = null,
        string? environmentPassword = null,
        string? legacyEnvironmentPassword = null,
        IReadOnlyDictionary<string, string>? environment = null,
        CancellationToken cancellationToken = default)
    {
        var originalIn = Console.In;
        var originalOut = Console.Out;
        var originalError = Console.Error;
        var originalPassword = Environment.GetEnvironmentVariable("SHERPA_PACK_PASSWORD");
        var originalLegacyPassword = Environment.GetEnvironmentVariable("SHERPA_BUNDLE_PASSWORD");
        var originalEnvironment = environment?.Keys.ToDictionary(
            key => key,
            Environment.GetEnvironmentVariable);

        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        try
        {
            Console.SetIn(new StringReader(stdin ?? string.Empty));
            Console.SetOut(stdout);
            Console.SetError(stderr);
            Environment.SetEnvironmentVariable("SHERPA_PACK_PASSWORD", environmentPassword);
            Environment.SetEnvironmentVariable("SHERPA_BUNDLE_PASSWORD", legacyEnvironmentPassword);
            if (environment is not null)
            {
                foreach (var (key, value) in environment)
                    Environment.SetEnvironmentVariable(key, value);
            }

            var exitCode = await Program.InvokeAsync(args, cancellationToken);
            return new CliInvocationResult(exitCode, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("SHERPA_PACK_PASSWORD", originalPassword);
            Environment.SetEnvironmentVariable("SHERPA_BUNDLE_PASSWORD", originalLegacyPassword);
            if (originalEnvironment is not null)
            {
                foreach (var (key, value) in originalEnvironment)
                    Environment.SetEnvironmentVariable(key, value);
            }
            Console.SetIn(originalIn);
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }
}

internal sealed class TestWorkspace : IDisposable
{
    private readonly DirectoryInfo _root;

    private TestWorkspace(DirectoryInfo root) => _root = root;

    public static TestWorkspace Create()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestScratch", Guid.NewGuid().ToString("N"));
        var directory = Directory.CreateDirectory(path);
        return new TestWorkspace(directory);
    }

    public string GetPath(params string[] parts)
    {
        var path = _root.FullName;
        foreach (var part in parts)
            path = Path.Combine(path, part);
        return path;
    }

    public void CreateProject(string relativePath, string targetFramework)
    {
        var fullPath = GetPath(relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(
            fullPath,
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>{{targetFramework}}</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
    }

    public string WriteBundle(SherpaBundle bundle, string password)
    {
        var path = GetPath($"{Guid.NewGuid():N}.sherpapack");
        File.WriteAllBytes(path, SherpaBundleFile.Encrypt(bundle, password));
        return path;
    }

    public void Dispose()
    {
        if (_root.Exists)
            _root.Delete(recursive: true);
    }
}
