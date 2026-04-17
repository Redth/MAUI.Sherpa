using FluentAssertions;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Tests.Services;

public class CopilotServiceTests
{
    [Fact]
    public void BuildLaunchPath_RemovesDuplicates_WhileKeepingExistingEntries()
    {
        var basePath = string.Join(Path.PathSeparator, ["/usr/bin", "/bin", "/usr/bin"]);

        var launchPath = CopilotService.BuildLaunchPath(basePath, ["/bin"]);
        var entries = launchPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        entries.Should().OnlyHaveUniqueItems();
        entries.Should().Contain("/usr/bin");
        entries.Should().Contain("/bin");
    }

    [Fact]
    public void ResolveCopilotCliPath_UsesAdditionalSearchPaths()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var binaryName = OperatingSystem.IsWindows() ? "copilot.exe" : "copilot";
            var expectedPath = Path.Combine(tempDir, binaryName);
            File.WriteAllText(expectedPath, string.Empty);

            var resolvedPath = CopilotService.ResolveCopilotCliPath(
                pathEnv: string.Empty,
                baseDirectory: tempDir,
                additionalSearchPaths: [expectedPath]);

            resolvedPath.Should().Be(expectedPath);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
