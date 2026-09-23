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
    private readonly Mock<ILocalCertificateService> _mockLocalCertificates;
    private readonly Mock<ILoggingService> _mockLogger;
    private readonly Mock<IMediatorContext> _mockContext;
    private readonly GetCertificatesHandler _handler;

    public GetCertificatesHandlerTests()
    {
        _mockAppleService = new Mock<IAppleConnectService>();
        _mockLocalCertificates = new Mock<ILocalCertificateService>();
        _mockLogger = new Mock<ILoggingService>();
        _mockContext = new Mock<IMediatorContext>();
        _mockLocalCertificates.SetupGet(s => s.IsSupported).Returns(false);
        _handler = new GetCertificatesHandler(
            _mockAppleService.Object,
            _mockLocalCertificates.Object,
            _mockLogger.Object);
    }

    static LocalSigningIdentity Identity(string commonName, string serialNumber) => new(
        Identity: commonName,
        CommonName: commonName,
        TeamId: "T9KLD6CCM9",
        SerialNumber: serialNumber,
        ExpirationDate: DateTime.UtcNow.AddYears(2),
        IsValid: true);

    [Fact]
    public async Task Handle_AddsDeveloperIdInstallerCertificatesFromTheKeychain()
    {
        // App Store Connect does not return Developer ID Installer certificates.
        _mockAppleService.Setup(s => s.GetCertificatesAsync())
            .ReturnsAsync(new List<AppleCertificate>
            {
                new("cert1", "Developer ID Application", "DEVELOPER_ID_APPLICATION_G2", "MAC_OS", DateTime.UtcNow.AddYears(1), "ABC123")
            });
        _mockLocalCertificates.SetupGet(s => s.IsSupported).Returns(true);
        _mockLocalCertificates.Setup(s => s.GetSigningIdentitiesAsync())
            .ReturnsAsync(new List<LocalSigningIdentity>
            {
                Identity("Developer ID Installer: Allan Ritchie (T9KLD6CCM9)", "FEDCBA"),
                Identity("Developer ID Application: Allan Ritchie (T9KLD6CCM9)", "ABC123"),
                Identity("localhost", "999999")
            });

        var result = await _handler.Handle(
            new GetCertificatesRequest("identity1"),
            _mockContext.Object,
            CancellationToken.None);

        result.Should().HaveCount(2);
        var installer = result.Single(certificate => certificate.SerialNumber == "FEDCBA");
        installer.CertificateType.Should().Be("DEVELOPER_ID_INSTALLER");
        installer.IsLocalOnly.Should().BeTrue();
        result.Should().ContainSingle(certificate => certificate.SerialNumber == "ABC123");
    }

    [Fact]
    public async Task Handle_DoesNotDuplicateAnInstallerCertificateTheApiAlreadyReturned()
    {
        _mockAppleService.Setup(s => s.GetCertificatesAsync())
            .ReturnsAsync(new List<AppleCertificate>
            {
                new("cert1", "Installer", "DEVELOPER_ID_INSTALLER", "MAC_OS", DateTime.UtcNow.AddYears(1), "00FEDCBA")
            });
        _mockLocalCertificates.SetupGet(s => s.IsSupported).Returns(true);
        _mockLocalCertificates.Setup(s => s.GetSigningIdentitiesAsync())
            .ReturnsAsync(new List<LocalSigningIdentity>
            {
                Identity("Developer ID Installer: Allan Ritchie (T9KLD6CCM9)", "fedcba")
            });

        var result = await _handler.Handle(
            new GetCertificatesRequest("identity1"),
            _mockContext.Object,
            CancellationToken.None);

        result.Should().ContainSingle();
        result.Single().IsLocalOnly.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_StillReturnsApiCertificates_WhenTheKeychainCannotBeRead()
    {
        _mockAppleService.Setup(s => s.GetCertificatesAsync())
            .ReturnsAsync(new List<AppleCertificate>
            {
                new("cert1", "Development", "IOS_DEVELOPMENT", "IOS", DateTime.UtcNow.AddYears(1), "ABC123")
            });
        _mockLocalCertificates.SetupGet(s => s.IsSupported).Returns(true);
        _mockLocalCertificates.Setup(s => s.GetSigningIdentitiesAsync())
            .ThrowsAsync(new InvalidOperationException("keychain locked"));

        var result = await _handler.Handle(
            new GetCertificatesRequest("identity1"),
            _mockContext.Object,
            CancellationToken.None);

        result.Should().ContainSingle();
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
