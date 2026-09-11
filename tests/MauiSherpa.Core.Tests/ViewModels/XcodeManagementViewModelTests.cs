using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.ViewModels;
using Moq;
using Shiny.Mediator;

namespace MauiSherpa.Core.Tests.ViewModels;

public class XcodeManagementViewModelTests
{
    [Theory]
    [InlineData("27A266a", true)]
    [InlineData("27A5252f", false)]
    [InlineData("27A5237l", false)]
    [InlineData("27A5228h", false)]
    public void IsInstalled_MatchesExactBuildRatherThanSharedVersion(string buildNumber, bool expected)
    {
        var viewModel = CreateViewModel();
        viewModel.InstalledXcodes =
        [
            new("/Applications/Xcode.app", "27.0", "27A266a", true),
            new("/Applications/Xcode_26.6.app", "26.6", "17G100", false)
        ];

        viewModel.IsInstalled(CreateRelease("27.0", buildNumber)).Should().Be(expected);
    }

    [Fact]
    public void IsInstalled_MatchesBuildEvenWhenVersionFormattingDiffers()
    {
        var viewModel = CreateViewModel();
        viewModel.InstalledXcodes = [new("/Applications/Xcode.app", "27", "27A266a", true)];

        viewModel.IsInstalled(CreateRelease("27.0", "27A266a")).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void IsInstalled_DoesNotMatchUnknownBuilds(string buildNumber)
    {
        var viewModel = CreateViewModel();
        viewModel.InstalledXcodes = [new("/Applications/Xcode.app", "27.0", buildNumber, true)];

        viewModel.IsInstalled(CreateRelease("27.0", buildNumber)).Should().BeFalse();
    }

    [Fact]
    public void IsInstalled_WithNoInstallations_ReturnsFalse()
    {
        var viewModel = CreateViewModel();

        viewModel.IsInstalled(CreateRelease("27.0", "27A266a")).Should().BeFalse();
    }

    private static XcodeManagementViewModel CreateViewModel() => new(
        Mock.Of<IXcodeService>(),
        Mock.Of<IMediator>(),
        Mock.Of<IAppleDownloadAuthService>(),
        Mock.Of<IAlertService>(),
        Mock.Of<ILoggingService>());

    private static XcodeRelease CreateRelease(string version, string buildNumber) =>
        new(version, buildNumber, DateTime.UtcNow, true, null, null, null, null, [], []);
}
