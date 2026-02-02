using FluentAssertions;
using MauiSherpa.Core.Handlers.Android;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests.Android;
using Moq;
using Shiny.Mediator;

namespace MauiSherpa.Core.Tests.Handlers.Android;

public class GetEmulatorsHandlerTests
{
    private readonly Mock<IAndroidSdkService> _mockSdkService;
    private readonly Mock<IMediatorContext> _mockContext;
    private readonly GetEmulatorsHandler _handler;

    public GetEmulatorsHandlerTests()
    {
        _mockSdkService = new Mock<IAndroidSdkService>();
        _mockContext = new Mock<IMediatorContext>();
        _handler = new GetEmulatorsHandler(_mockSdkService.Object);
    }

    [Fact]
    public async Task Handle_ReturnsEmulatorsFromService()
    {
        // Arrange
        var expectedEmulators = new List<AvdInfo>
        {
            new AvdInfo("Pixel_4_API_30", "pixel_4", "/path/to/avd", "android-30", "google_apis/x86_64", new Dictionary<string, string>
            {
                { "hw.device.name", "pixel_4" },
                { "image.sysdir.1", "system-images/android-30/google_apis/x86_64" }
            }),
            new AvdInfo("Nexus_5_API_29", "nexus_5", "/path/to/avd2", "android-29", null, new Dictionary<string, string>())
        };
        _mockSdkService.Setup(s => s.GetAvdsAsync())
            .ReturnsAsync(expectedEmulators);

        var request = new GetEmulatorsRequest();

        // Act
        var result = await _handler.Handle(request, _mockContext.Object, CancellationToken.None);

        // Assert
        result.Should().BeEquivalentTo(expectedEmulators);
        _mockSdkService.Verify(s => s.GetAvdsAsync(), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsEmptyList_WhenNoEmulators()
    {
        // Arrange
        _mockSdkService.Setup(s => s.GetAvdsAsync())
            .ReturnsAsync(new List<AvdInfo>());

        var request = new GetEmulatorsRequest();

        // Act
        var result = await _handler.Handle(request, _mockContext.Object, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }
}
