using System.Net;
using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Inspector;
using MauiSherpa.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;

namespace MauiSherpa.Core.Tests.Services.Inspector;

/// <summary>
/// Verifies the factory probes v1 first and falls back to legacy.
/// </summary>
public class AppInspectorClientFactoryTests
{
    [Fact]
    public async Task CreateAsync_WhenV1EndpointResponds_ReturnsV1Client()
    {
        var handler = new StubHandler(req =>
            req.RequestUri!.AbsolutePath switch
            {
                "/api/v1/agent/status" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"running\":true}", System.Text.Encoding.UTF8, "application/json")
                },
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            });

        var factory = BuildFactory(handler);

        var client = await factory.CreateAsync("localhost", 9223);

        client.Should().NotBeNull();
        client.ProtocolVersion.Should().Be(InspectorProtocolVersion.V1);
    }

    [Fact]
    public async Task CreateAsync_WhenOnlyLegacyResponds_ReturnsLegacyClient()
    {
        var handler = new StubHandler(req =>
            req.RequestUri!.AbsolutePath switch
            {
                "/api/v1/agent/status" => new HttpResponseMessage(HttpStatusCode.NotFound),
                "/api/status" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"running\":true}", System.Text.Encoding.UTF8, "application/json")
                },
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            });

        var factory = BuildFactory(handler);

        var client = await factory.CreateAsync("localhost", 9223);

        client.Should().NotBeNull();
        client.ProtocolVersion.Should().Be(InspectorProtocolVersion.Legacy);
    }

    [Fact]
    public async Task CreateAsync_WhenNeitherResponds_DefaultsToV1()
    {
        var handler = new StubHandler(_ =>
            throw new HttpRequestException("connection refused"));

        var factory = BuildFactory(handler);

        var client = await factory.CreateAsync("localhost", 9223);

        client.Should().NotBeNull();
        client.ProtocolVersion.Should().Be(InspectorProtocolVersion.V1);
    }

    private static AppInspectorClientFactory BuildFactory(StubHandler handler)
    {
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false));
        return new AppInspectorClientFactory(httpClientFactory.Object, NullLogger<AppInspectorClientFactory>.Instance);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_respond(request));
    }
}
