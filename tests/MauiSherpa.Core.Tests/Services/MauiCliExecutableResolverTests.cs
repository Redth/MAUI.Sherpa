using FluentAssertions;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Tests.Services;

public class MauiCliExecutableResolverTests
{
    [Fact]
    public void Resolve_PrefersGlobalToolShim()
    {
        var root = Path.Combine(Path.GetTempPath(), $"maui-cli-resolver-{Guid.NewGuid():N}");
        var toolDirectory = Path.Combine(root, ".dotnet", "tools");
        Directory.CreateDirectory(toolDirectory);
        var toolPath = Path.Combine(toolDirectory, "maui");
        File.WriteAllText(toolPath, string.Empty);

        try
        {
            var result = MauiCliExecutableResolver.Resolve(
                root,
                pathEnvironment: string.Empty,
                isWindows: false);

            result.Should().Be(toolPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Resolve_FallsBackToPath()
    {
        var root = Path.Combine(Path.GetTempPath(), $"maui-cli-path-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var toolPath = Path.Combine(root, "maui.exe");
        File.WriteAllText(toolPath, string.Empty);

        try
        {
            var result = MauiCliExecutableResolver.Resolve(
                userProfile: Path.Combine(root, "missing"),
                pathEnvironment: root,
                isWindows: true);

            result.Should().Be(toolPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
