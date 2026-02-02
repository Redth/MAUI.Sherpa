using FluentAssertions;
using MauiSherpa.Core.Handlers.Apple;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests.Apple;
using Moq;
using Shiny.Mediator;

namespace MauiSherpa.Core.Tests.Handlers.Apple;

public class GetProfilesHandlerTests
{
    private readonly Mock<IAppleConnectService> _mockAppleService;
    private readonly Mock<IMediatorContext> _mockContext;
    private readonly GetProfilesHandler _handler;

    public GetProfilesHandlerTests()
    {
        _mockAppleService = new Mock<IAppleConnectService>();
        _mockContext = new Mock<IMediatorContext>();
        _handler = new GetProfilesHandler(_mockAppleService.Object);
    }

    [Fact]
    public async Task Handle_ReturnsProfilesFromService()
    {
        // Arrange
        var expectedProfiles = new List<AppleProfile>
        {
            new AppleProfile("profile1", "Development Profile", "IOS_APP_DEVELOPMENT", "IOS", "ACTIVE", 
                DateTime.UtcNow.AddYears(1), "com.example.app", "uuid-1"),
            new AppleProfile("profile2", "Distribution Profile", "APP_STORE", "IOS", "ACTIVE", 
                DateTime.UtcNow.AddMonths(6), "com.example.app", "uuid-2")
        };
        _mockAppleService.Setup(s => s.GetProfilesAsync())
            .ReturnsAsync(expectedProfiles);

        var request = new GetProfilesRequest("identity1");

        // Act
        var result = await _handler.Handle(request, _mockContext.Object, CancellationToken.None);

        // Assert
        result.Should().BeEquivalentTo(expectedProfiles);
        _mockAppleService.Verify(s => s.GetProfilesAsync(), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsEmptyList_WhenNoProfiles()
    {
        // Arrange
        _mockAppleService.Setup(s => s.GetProfilesAsync())
            .ReturnsAsync(new List<AppleProfile>());

        var request = new GetProfilesRequest("identity1");

        // Act
        var result = await _handler.Handle(request, _mockContext.Object, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }
}
