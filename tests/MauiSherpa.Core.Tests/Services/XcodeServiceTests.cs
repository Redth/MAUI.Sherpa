using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Tests.Services;

public class XcodeServiceTests
{
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
}
