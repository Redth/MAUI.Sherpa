using FluentAssertions;
using MauiSherpa.Core.Handlers.Android;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests.Android;
using Moq;
using Shiny.Mediator;

namespace MauiSherpa.Core.Tests.Handlers.Android;

public class GetAndroidDevicesHandlerTests
{
    private readonly Mock<IAndroidSdkService> _mockSdkService;
    private readonly Mock<IMediatorContext> _mockContext;
    private readonly GetAndroidDevicesHandler _handler;

    public GetAndroidDevicesHandlerTests()
    {
        _mockSdkService = new Mock<IAndroidSdkService>();
        _mockContext = new Mock<IMediatorContext>();
        _handler = new GetAndroidDevicesHandler(_mockSdkService.Object);
    }

    [Fact]
    public async Task Handle_ReturnsDevicesFromService()
    {
        // Arrange
        var expectedDevices = new List<DeviceInfo>
        {
            new DeviceInfo("emulator-5554", "Pixel_4_API_30", "device", true),
            new DeviceInfo("abc123", "Samsung Galaxy", "device", false)
        };
        _mockSdkService.Setup(s => s.GetDevicesAsync())
            .ReturnsAsync(expectedDevices);

        var request = new GetAndroidDevicesRequest();

        // Act
        var result = await _handler.Handle(request, _mockContext.Object, CancellationToken.None);

        // Assert
        result.Should().BeEquivalentTo(expectedDevices);
        _mockSdkService.Verify(s => s.GetDevicesAsync(), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsEmptyList_WhenNoDevices()
    {
        // Arrange
        _mockSdkService.Setup(s => s.GetDevicesAsync())
            .ReturnsAsync(new List<DeviceInfo>());

        var request = new GetAndroidDevicesRequest();

        // Act
        var result = await _handler.Handle(request, _mockContext.Object, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }
}
