using System.Text.Json;
using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using Moq;

namespace MauiSherpa.Core.Tests.Services;

public class SecretsPublisherServiceTests
{
    readonly Mock<ISecureStorageService> _secureStorage = new();
    readonly Mock<ISecretsPublisherFactory> _factory = new();
    readonly Mock<ILoggingService> _logger = new();
    readonly Mock<ISecretsPublisher> _publisher = new();
    readonly SecretsPublisherService _sut;

    static readonly SecretsPublisherConfig Config = new(
        Id: "publisher-1",
        ProviderId: "github",
        Name: "GitHub",
        Settings: new Dictionary<string, string>());

    public SecretsPublisherServiceTests()
    {
        _secureStorage.Setup(x => x.GetAsync("secrets_publishers"))
            .ReturnsAsync(JsonSerializer.Serialize(new List<SecretsPublisherConfig> { Config }));
        _factory.Setup(x => x.CreatePublisher(It.IsAny<SecretsPublisherConfig>()))
            .Returns(_publisher.Object);

        _sut = new SecretsPublisherService(
            _secureStorage.Object,
            _factory.Object,
            _logger.Object);
    }

    [Fact]
    public async Task PublishSecretsAsync_WithoutAPriorPublisherListing_StillResolvesThePublisher()
    {
        var secrets = new Dictionary<string, string> { ["KEY"] = "value" };

        // No GetPublishersAsync() call first — a batch publish reaches this cold.
        var publish = () => _sut.PublishSecretsAsync("publisher-1", "owner/repo", secrets);

        await publish.Should().NotThrowAsync();
        _publisher.Verify(
            x => x.PublishSecretsAsync(
                "owner/repo",
                secrets,
                It.IsAny<IProgress<string>?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PublishSecretsAsync_WithAnUnknownPublisher_Throws()
    {
        var publish = () => _sut.PublishSecretsAsync(
            "missing",
            "owner/repo",
            new Dictionary<string, string>());

        await publish.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*missing*");
    }
}
