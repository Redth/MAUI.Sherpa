using FluentAssertions;
using MauiSherpa.Core.Handlers.Apple;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests.Apple;
using Moq;
using Shiny.Mediator;

namespace MauiSherpa.Core.Tests.Handlers.Apple;

public class GetBundleIdsHandlerTests
{
    private readonly Mock<IAppleConnectService> _mockAppleService;
    private readonly Mock<IMediatorContext> _mockContext;
    private readonly GetBundleIdsHandler _handler;

    public GetBundleIdsHandlerTests()
    {
        _mockAppleService = new Mock<IAppleConnectService>();
        _mockContext = new Mock<IMediatorContext>();
        _handler = new GetBundleIdsHandler(_mockAppleService.Object);
    }

    [Fact]
    public async Task Handle_ReturnsBundleIdsFromService()
    {
        // Arrange
        var expectedBundleIds = new List<AppleBundleId>
        {
            new AppleBundleId("bundle1", "com.example.app", "Example App", "IOS", "ABCD12"),
            new AppleBundleId("bundle2", "com.example.app.widgets", "Example Widgets", "IOS", null)
        };
        _mockAppleService.Setup(s => s.GetBundleIdsAsync())
            .ReturnsAsync(expectedBundleIds);

        var request = new GetBundleIdsRequest("identity1");

        // Act
        var result = await _handler.Handle(request, _mockContext.Object, CancellationToken.None);

        // Assert
        result.Should().BeEquivalentTo(expectedBundleIds);
        _mockAppleService.Verify(s => s.GetBundleIdsAsync(), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsEmptyList_WhenNoBundleIds()
    {
        // Arrange
        _mockAppleService.Setup(s => s.GetBundleIdsAsync())
            .ReturnsAsync(new List<AppleBundleId>());

        var request = new GetBundleIdsRequest("identity1");

        // Act
        var result = await _handler.Handle(request, _mockContext.Object, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }
}
