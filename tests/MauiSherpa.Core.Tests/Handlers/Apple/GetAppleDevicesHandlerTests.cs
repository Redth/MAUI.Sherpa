using FluentAssertions;
using MauiSherpa.Core.Handlers.Apple;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests.Apple;
using Moq;
using Shiny.Mediator;

namespace MauiSherpa.Core.Tests.Handlers.Apple;

public class GetAppleDevicesHandlerTests
{
    private readonly Mock<IAppleConnectService> _mockAppleService;
    private readonly Mock<IMediatorContext> _mockContext;
    private readonly GetAppleDevicesHandler _handler;

    public GetAppleDevicesHandlerTests()
    {
        _mockAppleService = new Mock<IAppleConnectService>();
        _mockContext = new Mock<IMediatorContext>();
        _handler = new GetAppleDevicesHandler(_mockAppleService.Object);
    }

    [Fact]
    public async Task Handle_ReturnsDevicesFromService()
    {
        // Arrange
        var expectedDevices = new List<AppleDevice>
        {
            new AppleDevice("device1", "00001111-AAAA2222BBBB3333", "iPhone 15 Pro", "IOS", "IPHONE", "ENABLED", "iPhone15,2"),
            new AppleDevice("device2", "00004444-CCCC5555DDDD6666", "iPad Pro", "IOS", "IPAD", "ENABLED", "iPad14,1")
        };
        _mockAppleService.Setup(s => s.GetDevicesAsync())
            .ReturnsAsync(expectedDevices);

        var request = new GetAppleDevicesRequest("identity1");

        // Act
        var result = await _handler.Handle(request, _mockContext.Object, CancellationToken.None);

        // Assert
        result.Should().BeEquivalentTo(expectedDevices);
        _mockAppleService.Verify(s => s.GetDevicesAsync(), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsEmptyList_WhenNoDevices()
    {
        // Arrange
        _mockAppleService.Setup(s => s.GetDevicesAsync())
            .ReturnsAsync(new List<AppleDevice>());

        var request = new GetAppleDevicesRequest("identity1");

        // Act
        var result = await _handler.Handle(request, _mockContext.Object, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }
}
