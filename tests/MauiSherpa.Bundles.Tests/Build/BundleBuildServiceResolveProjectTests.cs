using FluentAssertions;

namespace MauiSherpa.Bundles.Tests.Build;

public class BundleBuildServiceResolveProjectTests
{
    [Fact]
    public void ResolveProject_WithConfiguredProject_ReturnsFullPath()
    {
        using var workspace = new BundleBuildWorkspace();
        var expected = workspace.CreateProject("src/App/App.csproj", "net10.0-android");

        var resolved = BundleBuildService.ResolveProject(workspace.RootPath, "src/App/App.csproj");

        resolved.Should().Be(Path.GetFullPath(expected));
    }

    [Fact]
    public void ResolveProject_WithMissingConfiguredProject_ThrowsFileNotFound()
    {
        using var workspace = new BundleBuildWorkspace();

        var act = () => BundleBuildService.ResolveProject(workspace.RootPath, "src/App/Missing.csproj");

        act.Should().Throw<FileNotFoundException>();
    }

    [Theory]
    [InlineData("../Escape.csproj")]
    [InlineData("nested/../../Escape.csproj")]
    public void ResolveProject_WithPathEscapingWorkspace_ThrowsBundleValidationException(string configuredProject)
    {
        using var workspace = new BundleBuildWorkspace();

        var act = () => BundleBuildService.ResolveProject(workspace.RootPath, configuredProject);

        act.Should().Throw<BundleValidationException>();
    }

    [Fact]
    public void ResolveProject_WithRootedConfiguredProject_ThrowsBundleValidationException()
    {
        using var workspace = new BundleBuildWorkspace();
        var outsideProject = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.csproj");
        File.WriteAllText(outsideProject, "<Project />");
        try
        {
            var act = () => BundleBuildService.ResolveProject(workspace.RootPath, outsideProject);

            act.Should().Throw<BundleValidationException>();
        }
        finally
        {
            File.Delete(outsideProject);
        }
    }

    [Fact]
    public void ResolveProject_WithoutConfiguredProject_AutoDiscoversSingleProject()
    {
        using var workspace = new BundleBuildWorkspace();
        var expected = workspace.CreateProject("src/App/App.csproj", "net10.0-android");
        // Files under bin/obj must be ignored by auto-discovery.
        workspace.CreateProject("src/App/bin/Debug/App.csproj", "net10.0-android");
        workspace.CreateProject("src/App/obj/App.csproj", "net10.0-android");

        var resolved = BundleBuildService.ResolveProject(workspace.RootPath, configuredProject: null);

        resolved.Should().Be(Path.GetFullPath(expected));
    }

    [Fact]
    public void ResolveProject_WithoutConfiguredProject_AndNoProjects_ThrowsInvalidOperationException()
    {
        using var workspace = new BundleBuildWorkspace();

        var act = () => BundleBuildService.ResolveProject(workspace.RootPath, configuredProject: null);

        act.Should().Throw<InvalidOperationException>().WithMessage("*No project was found*");
    }

    [Fact]
    public void ResolveProject_WithoutConfiguredProject_AndMultipleProjects_ThrowsInvalidOperationException()
    {
        using var workspace = new BundleBuildWorkspace();
        workspace.CreateProject("src/App/App.csproj", "net10.0-android");
        workspace.CreateProject("src/Other/Other.csproj", "net10.0-android");

        var act = () => BundleBuildService.ResolveProject(workspace.RootPath, configuredProject: null);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Multiple projects were found*");
    }
}
