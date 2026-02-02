using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using Moq;

namespace MauiSherpa.Core.Tests.Services;

public class AppleRootCertServiceTests
{
    private readonly Mock<ILoggingService> _mockLogger;
    private readonly Mock<IPlatformService> _mockPlatform;

    public AppleRootCertServiceTests()
    {
        _mockLogger = new Mock<ILoggingService>();
        _mockPlatform = new Mock<IPlatformService>();
        _mockPlatform.Setup(p => p.IsMacCatalyst).Returns(true);
    }

    [Fact]
    public void GetAvailableCertificates_ReturnsExpectedCertificates()
    {
        // Arrange
        var service = new AppleRootCertService(_mockLogger.Object, _mockPlatform.Object);

        // Act
        var result = service.GetAvailableCertificates();

        // Assert
        result.Should().NotBeEmpty();
        result.Should().Contain(c => c.Name == "Apple Inc. Root");
        result.Should().Contain(c => c.Type == "Root");
        result.Should().Contain(c => c.Type == "Intermediate");
    }

    [Fact]
    public void GetAvailableCertificates_ContainsWWDRCertificates()
    {
        // Arrange
        var service = new AppleRootCertService(_mockLogger.Object, _mockPlatform.Object);

        // Act
        var result = service.GetAvailableCertificates();

        // Assert
        result.Should().Contain(c => c.Name.Contains("WWDR"));
        result.Should().Contain(c => c.Name == "WWDR - G6");
    }

    [Fact]
    public void GetAvailableCertificates_ContainsDeveloperIDCertificates()
    {
        // Arrange
        var service = new AppleRootCertService(_mockLogger.Object, _mockPlatform.Object);

        // Act
        var result = service.GetAvailableCertificates();

        // Assert
        result.Should().Contain(c => c.Name == "Developer ID - G1");
        result.Should().Contain(c => c.Name == "Developer ID - G2");
    }

    [Fact]
    public void GetAvailableCertificates_HasValidUrls()
    {
        // Arrange
        var service = new AppleRootCertService(_mockLogger.Object, _mockPlatform.Object);

        // Act
        var result = service.GetAvailableCertificates();

        // Assert
        result.Should().AllSatisfy(c =>
        {
            c.Url.Should().StartWith("https://www.apple.com/");
            c.Url.Should().EndWith(".cer");
        });
    }
}
