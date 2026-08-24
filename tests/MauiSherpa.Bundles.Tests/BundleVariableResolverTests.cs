using FluentAssertions;

namespace MauiSherpa.Bundles.Tests;

public class BundleVariableResolverTests
{
    [Fact]
    public void Resolve_MergesLayersAndExpandsReferences()
    {
        var bundle = CreateBundle();

        var result = BundleVariableResolver.Resolve(
            bundle,
            "production",
            BundlePlatform.Android,
            BundlePhase.Build,
            new Dictionary<string, string> { ["BuildNumber"] = "99" });

        result.Values["Name"].Should().Be("Platform");
        result.Values["Version"].Should().Be("1.0.99");
        result.SecretValues.Should().Contain("secret-value");
    }

    [Fact]
    public void Resolve_WithCycle_ThrowsValidationError()
    {
        var bundle = CreateBundle() with
        {
            Variables = new Dictionary<string, string>
            {
                ["A"] = "${B}",
                ["B"] = "{{ A }}"
            }
        };

        var act = () => BundleVariableResolver.Resolve(
            bundle, "production", BundlePlatform.Android, BundlePhase.Build);

        act.Should().Throw<BundleValidationException>().WithMessage("*cycle*");
    }

    [Fact]
    public async Task Staging_ReplacesTokensWithoutChangingSource()
    {
        var source = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(source);
        var sourceFile = Path.Combine(source, "Constants.cs");
        await File.WriteAllTextAsync(sourceFile, "const string Value = \"${Secret}\";");
        try
        {
            await using var workspace = await BundleStagingWorkspace.CreateAsync(
                source,
                [new BundleReplacement { Path = "Constants.cs" }],
                new Dictionary<string, string> { ["Secret"] = "expanded" });

            (await File.ReadAllTextAsync(Path.Combine(workspace.RootPath, "Constants.cs")))
                .Should().Contain("expanded");
            (await File.ReadAllTextAsync(sourceFile)).Should().Contain("${Secret}");
        }
        finally
        {
            Directory.Delete(source, recursive: true);
        }
    }

    private static SherpaBundle CreateBundle() => new()
    {
        Name = "Variables",
        Variables = new Dictionary<string, string>
        {
            ["Name"] = "Global",
            ["BuildNumber"] = "1",
            ["Version"] = "1.0.${BuildNumber}",
            ["Secret"] = "secret-value"
        },
        SecretVariables = new HashSet<string> { "Secret" },
        Environments = new Dictionary<string, SherpaBundleEnvironment>
        {
            ["production"] = new()
            {
                Variables = new Dictionary<string, string> { ["Name"] = "Environment" },
                Platforms = new Dictionary<BundlePlatform, BundlePlatformConfiguration>
                {
                    [BundlePlatform.Android] = new()
                    {
                        Variables = new Dictionary<string, string> { ["Name"] = "Platform" },
                        Build = new BundleBuildConfiguration()
                    }
                }
            }
        }
    };
}
