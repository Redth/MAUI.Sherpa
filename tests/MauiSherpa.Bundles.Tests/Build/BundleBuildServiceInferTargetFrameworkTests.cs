using FluentAssertions;

namespace MauiSherpa.Bundles.Tests.Build;

public class BundleBuildServiceInferTargetFrameworkTests
{
    [Theory]
    [InlineData(BundlePlatform.Android, "net10.0-android")]
    [InlineData(BundlePlatform.Ios, "net10.0-ios")]
    [InlineData(BundlePlatform.MacOS, "net10.0-macos")]
    [InlineData(BundlePlatform.MacCatalyst, "net10.0-maccatalyst")]
    [InlineData(BundlePlatform.Windows, "net10.0-windows10.0.19041.0")]
    public void InferTargetFramework_WithDeclaredFramework_ReturnsExactMatch(BundlePlatform platform, string declared)
    {
        using var workspace = new BundleBuildWorkspace();
        var project = workspace.CreateProject(
            "App.csproj",
            "net10.0-android;net10.0-ios;net10.0-macos;net10.0-maccatalyst;net10.0-windows10.0.19041.0");
        // Sanity: the declared value used for assertion must actually be one of the frameworks above.
        File.ReadAllText(project).Should().Contain(declared);

        var framework = BundleBuildService.InferTargetFramework(project, platform);

        framework.Should().Be(declared);
    }

    [Fact]
    public void InferTargetFramework_KeepsExistingBehavior_ForAndroidAndIosMultiTarget()
    {
        using var workspace = new BundleBuildWorkspace();
        var project = workspace.CreateProject("App.csproj", "net10.0-android;net10.0-ios");

        BundleBuildService.InferTargetFramework(project, BundlePlatform.Ios).Should().Be("net10.0-ios");
    }

    [Fact]
    public void InferTargetFramework_WhenPlatformNotDeclared_DerivesVersionFromExistingFramework()
    {
        using var workspace = new BundleBuildWorkspace();
        // Project only declares net9.0-android; asking for iOS (never declared) must reuse the
        // project's actual net9.0 SDK version rather than assuming net10.0.
        var project = workspace.CreateProject("App.csproj", "net9.0-android");

        var framework = BundleBuildService.InferTargetFramework(project, BundlePlatform.Ios);

        framework.Should().Be("net9.0-ios");
    }

    [Fact]
    public void InferTargetFramework_WhenNoFrameworkDeclared_FallsBackToNet10()
    {
        using var workspace = new BundleBuildWorkspace();
        var project = workspace.CreateFile(
            "App.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
              </PropertyGroup>
            </Project>
            """);

        var framework = BundleBuildService.InferTargetFramework(project, BundlePlatform.MacCatalyst);

        framework.Should().Be("net10.0-maccatalyst");
    }

    [Fact]
    public void InferTargetFramework_UsesSingleTargetFrameworkElement()
    {
        using var workspace = new BundleBuildWorkspace();
        var project = workspace.CreateFile(
            "App.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0-windows10.0.19041.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var framework = BundleBuildService.InferTargetFramework(project, BundlePlatform.Windows);

        framework.Should().Be("net9.0-windows10.0.19041.0");
    }
}
