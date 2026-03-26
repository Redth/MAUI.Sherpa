using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Tests.Services;

public class XcodeServiceTests
{
    [Fact]
    public void FindBundledUnxipExecutable_WithRuntimeSpecificBundle_ReturnsBundledPath()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var bundledPath = CreateBundledExecutable(tempDir, "runtimes", "osx-arm64", "native", "unxip");

            var resolved = XcodeService.FindBundledUnxipExecutable(tempDir, "osx-arm64");

            resolved.Should().Be(bundledPath);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void FindBundledUnxipExecutable_WithMacCatalystRuntime_FallsBackToBundledOsxPath()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var bundledPath = CreateBundledExecutable(tempDir, "runtimes", "osx-arm64", "native", "unxip");

            var resolved = XcodeService.FindBundledUnxipExecutable(tempDir, "maccatalyst-arm64");

            resolved.Should().Be(bundledPath);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void CreateArchiveExtractionCommand_WithSystemPreference_UsesSystemXip()
    {
        var command = XcodeService.CreateArchiveExtractionCommand(
            "/tmp/Xcode_16.0.xip",
            XcodeArchiveExtractorOptions.SystemXip,
            null);

        command.FileName.Should().Be("xip");
        command.Arguments.Should().Be("--expand \"/tmp/Xcode_16.0.xip\"");
        command.Preference.Should().Be(XcodeArchiveExtractorOptions.SystemXip);
        command.FellBackToSystemXip.Should().BeFalse();
    }

    [Fact]
    public void CreateArchiveExtractionCommand_WithUnxipPreferenceAndExecutable_UsesUnxip()
    {
        var command = XcodeService.CreateArchiveExtractionCommand(
            "/tmp/Xcode_16.0.xip",
            XcodeArchiveExtractorOptions.Unxip,
            "/opt/homebrew/bin/unxip");

        command.FileName.Should().Be("/opt/homebrew/bin/unxip");
        command.Arguments.Should().Be("\"/tmp/Xcode_16.0.xip\"");
        command.Preference.Should().Be(XcodeArchiveExtractorOptions.Unxip);
        command.FellBackToSystemXip.Should().BeFalse();
    }

    [Fact]
    public void CreateArchiveExtractionCommand_WithUnxipPreferenceButNoExecutable_FallsBackToSystemXip()
    {
        var command = XcodeService.CreateArchiveExtractionCommand(
            "/tmp/Xcode_16.0.xip",
            XcodeArchiveExtractorOptions.Unxip,
            null);

        command.FileName.Should().Be("xip");
        command.Arguments.Should().Be("--expand \"/tmp/Xcode_16.0.xip\"");
        command.Preference.Should().Be(XcodeArchiveExtractorOptions.SystemXip);
        command.FellBackToSystemXip.Should().BeTrue();
    }

    private static string CreateTempDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "MauiSherpa-XcodeServiceTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    private static string CreateBundledExecutable(string baseDirectory, params string[] relativeSegments)
    {
        var path = Path.Combine(baseDirectory, Path.Combine(relativeSegments));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
        return path;
    }
}
