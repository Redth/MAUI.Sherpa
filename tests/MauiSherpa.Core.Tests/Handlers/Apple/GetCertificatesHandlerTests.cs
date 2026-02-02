using FluentAssertions;
using MauiSherpa.Core.Handlers.Apple;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests.Apple;
using Moq;
using Shiny.Mediator;

namespace MauiSherpa.Core.Tests.Handlers.Apple;

public class GetCertificatesHandlerTests
{
    private readonly Mock<IAppleConnectService> _mockAppleService;
    private readonly Mock<IMediatorContext> _mockContext;
    private readonly GetCertificatesHandler _handler;

    public GetCertificatesHandlerTests()
    {
        _mockAppleService = new Mock<IAppleConnectService>();
        _mockContext = new Mock<IMediatorContext>();
        _handler = new GetCertificatesHandler(_mockAppleService.Object);
    }

    [Fact]
    public async Task Handle_ReturnsCertificatesFromService()
    {
        // Arrange
        var expectedCerts = new List<AppleCertificate>
        {
            new AppleCertificate("cert1", "Development", "IOS_DEVELOPMENT", "IOS", DateTime.UtcNow.AddYears(1), "ABC123"),
            new AppleCertificate("cert2", "Distribution", "IOS_DISTRIBUTION", "IOS", DateTime.UtcNow.AddMonths(6), "DEF456")
        };
        _mockAppleService.Setup(s => s.GetCertificatesAsync())
            .ReturnsAsync(expectedCerts);

        var request = new GetCertificatesRequest("identity1");

        // Act
        var result = await _handler.Handle(request, _mockContext.Object, CancellationToken.None);

        // Assert
        result.Should().BeEquivalentTo(expectedCerts);
        _mockAppleService.Verify(s => s.GetCertificatesAsync(), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsEmptyList_WhenNoCertificates()
    {
        // Arrange
        _mockAppleService.Setup(s => s.GetCertificatesAsync())
            .ReturnsAsync(new List<AppleCertificate>());

        var request = new GetCertificatesRequest("identity1");

        // Act
        var result = await _handler.Handle(request, _mockContext.Object, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }
}
