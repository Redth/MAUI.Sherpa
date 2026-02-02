using FluentAssertions;
using MauiSherpa.Core.Handlers.Android;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests.Android;
using Moq;
using Shiny.Mediator;

namespace MauiSherpa.Core.Tests.Handlers.Android;

public class GetSdkPathHandlerTests
{
    private readonly Mock<IAndroidSdkSettingsService> _mockSettingsService;
    private readonly Mock<IMediatorContext> _mockContext;
    private readonly GetSdkPathHandler _handler;

    public GetSdkPathHandlerTests()
    {
        _mockSettingsService = new Mock<IAndroidSdkSettingsService>();
        _mockContext = new Mock<IMediatorContext>();
        _handler = new GetSdkPathHandler(_mockSettingsService.Object);
    }

    [Fact]
    public async Task Handle_ReturnsSdkPath_WhenConfigured()
    {
        // Arrange
        var expectedPath = "/Users/test/Library/Android/sdk";
        _mockSettingsService.Setup(s => s.InitializeAsync())
            .Returns(Task.CompletedTask);
        _mockSettingsService.Setup(s => s.GetEffectiveSdkPathAsync())
            .ReturnsAsync(expectedPath);

        var request = new GetSdkPathRequest();

        // Act
        var result = await _handler.Handle(request, _mockContext.Object, CancellationToken.None);

        // Assert
        result.Should().Be(expectedPath);
        _mockSettingsService.Verify(s => s.InitializeAsync(), Times.Once);
        _mockSettingsService.Verify(s => s.GetEffectiveSdkPathAsync(), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsNull_WhenSdkNotFound()
    {
        // Arrange
        _mockSettingsService.Setup(s => s.InitializeAsync())
            .Returns(Task.CompletedTask);
        _mockSettingsService.Setup(s => s.GetEffectiveSdkPathAsync())
            .ReturnsAsync((string?)null);

        var request = new GetSdkPathRequest();

        // Act
        var result = await _handler.Handle(request, _mockContext.Object, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }
}
