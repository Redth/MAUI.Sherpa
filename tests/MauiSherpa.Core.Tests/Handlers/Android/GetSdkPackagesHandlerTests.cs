using FluentAssertions;
using MauiSherpa.Core.Handlers.Android;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests.Android;
using Moq;
using Shiny.Mediator;

namespace MauiSherpa.Core.Tests.Handlers.Android;

public class GetSdkPackagesHandlerTests
{
    private readonly Mock<IAndroidSdkService> _mockSdkService;
    private readonly Mock<IMediatorContext> _mockContext;
    private readonly GetInstalledPackagesHandler _installedHandler;
    private readonly GetAvailablePackagesHandler _availableHandler;

    public GetSdkPackagesHandlerTests()
    {
        _mockSdkService = new Mock<IAndroidSdkService>();
        _mockContext = new Mock<IMediatorContext>();
        _installedHandler = new GetInstalledPackagesHandler(_mockSdkService.Object);
        _availableHandler = new GetAvailablePackagesHandler(_mockSdkService.Object);
    }

    [Fact]
    public async Task GetInstalledPackages_ReturnsPackagesFromService()
    {
        // Arrange
        var expectedPackages = new List<SdkPackageInfo>
        {
            new SdkPackageInfo("platforms;android-34", "Android SDK Platform 34", "34.0.0", "platforms", true),
            new SdkPackageInfo("build-tools;34.0.0", "Android SDK Build-Tools 34", "34.0.0", "build-tools", true)
        };
        _mockSdkService.Setup(s => s.GetInstalledPackagesAsync())
            .ReturnsAsync(expectedPackages);

        var request = new GetInstalledPackagesRequest();

        // Act
        var result = await _installedHandler.Handle(request, _mockContext.Object, CancellationToken.None);

        // Assert
        result.Should().BeEquivalentTo(expectedPackages);
        _mockSdkService.Verify(s => s.GetInstalledPackagesAsync(), Times.Once);
    }

    [Fact]
    public async Task GetAvailablePackages_ReturnsPackagesFromService()
    {
        // Arrange
        var expectedPackages = new List<SdkPackageInfo>
        {
            new SdkPackageInfo("platforms;android-35", "Android SDK Platform 35", "35.0.0", "platforms", false),
            new SdkPackageInfo("ndk;26.0.0", "NDK 26", "26.0.0", "ndk", false)
        };
        _mockSdkService.Setup(s => s.GetAvailablePackagesAsync())
            .ReturnsAsync(expectedPackages);

        var request = new GetAvailablePackagesRequest();

        // Act
        var result = await _availableHandler.Handle(request, _mockContext.Object, CancellationToken.None);

        // Assert
        result.Should().BeEquivalentTo(expectedPackages);
        _mockSdkService.Verify(s => s.GetAvailablePackagesAsync(), Times.Once);
    }

    [Fact]
    public async Task GetInstalledPackages_ReturnsEmptyList_WhenNoPackages()
    {
        // Arrange
        _mockSdkService.Setup(s => s.GetInstalledPackagesAsync())
            .ReturnsAsync(new List<SdkPackageInfo>());

        var request = new GetInstalledPackagesRequest();

        // Act
        var result = await _installedHandler.Handle(request, _mockContext.Object, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }
}
