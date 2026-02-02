using FluentAssertions;
using MauiSherpa.Core.Handlers.Android;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests.Android;
using Moq;
using Shiny.Mediator;

namespace MauiSherpa.Core.Tests.Handlers.Android;

public class GetDeviceDefinitionsHandlerTests
{
    private readonly Mock<IAndroidSdkService> _mockSdkService;
    private readonly Mock<IMediatorContext> _mockContext;
    private readonly GetDeviceDefinitionsHandler _handler;
    private readonly GetAvdSkinsHandler _skinsHandler;

    public GetDeviceDefinitionsHandlerTests()
    {
        _mockSdkService = new Mock<IAndroidSdkService>();
        _mockContext = new Mock<IMediatorContext>();
        _handler = new GetDeviceDefinitionsHandler(_mockSdkService.Object);
        _skinsHandler = new GetAvdSkinsHandler(_mockSdkService.Object);
    }

    [Fact]
    public async Task Handle_ReturnsDeviceDefinitionsFromService()
    {
        // Arrange
        var expectedDefinitions = new List<AvdDeviceDefinition>
        {
            new AvdDeviceDefinition("pixel_4", "Pixel 4", "Google", 1),
            new AvdDeviceDefinition("pixel_6", "Pixel 6", "Google", 2),
            new AvdDeviceDefinition("medium_tablet", "Medium Tablet", null, 3)
        };
        _mockSdkService.Setup(s => s.GetAvdDeviceDefinitionsAsync())
            .ReturnsAsync(expectedDefinitions);

        var request = new GetDeviceDefinitionsRequest();

        // Act
        var result = await _handler.Handle(request, _mockContext.Object, CancellationToken.None);

        // Assert
        result.Should().BeEquivalentTo(expectedDefinitions);
        _mockSdkService.Verify(s => s.GetAvdDeviceDefinitionsAsync(), Times.Once);
    }

    [Fact]
    public async Task GetAvdSkins_ReturnsSkinsFromService()
    {
        // Arrange
        var expectedSkins = new List<string> { "pixel_4", "pixel_6_pro", "nexus_5" };
        _mockSdkService.Setup(s => s.GetAvdSkinsAsync())
            .ReturnsAsync(expectedSkins);

        var request = new GetAvdSkinsRequest();

        // Act
        var result = await _skinsHandler.Handle(request, _mockContext.Object, CancellationToken.None);

        // Assert
        result.Should().BeEquivalentTo(expectedSkins);
        _mockSdkService.Verify(s => s.GetAvdSkinsAsync(), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsEmptyList_WhenNoDefinitions()
    {
        // Arrange
        _mockSdkService.Setup(s => s.GetAvdDeviceDefinitionsAsync())
            .ReturnsAsync(new List<AvdDeviceDefinition>());

        var request = new GetDeviceDefinitionsRequest();

        // Act
        var result = await _handler.Handle(request, _mockContext.Object, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }
}
