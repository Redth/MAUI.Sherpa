using System.Net;
using System.Text;
using FluentAssertions;
using MauiSherpa.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace MauiSherpa.Core.Tests.Services.Inspector;

/// <summary>
/// Response shapes taken from the DevFlow agent's /api/v1/profiler handlers.
/// </summary>
public class DevFlowV1ProfilerTests
{
    private const string LeaseGranted = """{"ok":true,"allowed":true,"youHold":true}""";

    [Fact]
    public async Task StartProfilingAsync_ReadsSessionFromAgentEnvelope()
    {
        var client = CreateV1Client(req => req.RequestUri!.AbsolutePath == DevFlowMutationLease.LeasePath
            ? Json(HttpStatusCode.OK, LeaseGranted)
            : Json(HttpStatusCode.OK, """
                {
                  "session": { "SessionId": "s-1", "StartedAtUtc": "2026-01-01T00:00:00Z", "SampleIntervalMs": 500, "IsActive": true },
                  "capabilities": { "available": true, "supportedInBuild": true, "featureEnabled": true }
                }
                """));

        var session = await client.StartProfilingAsync(500);

        session.SessionId.Should().Be("s-1");
        session.SampleIntervalMs.Should().Be(500);
        session.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task StartProfilingAsync_WhenProfilerDisabled_ThrowsWithAgentMessage()
    {
        var client = CreateV1Client(req => req.RequestUri!.AbsolutePath == DevFlowMutationLease.LeasePath
            ? Json(HttpStatusCode.OK, LeaseGranted)
            : Json(HttpStatusCode.BadRequest,
                """{"success":false,"error":"Profiler is disabled. Set AgentOptions.EnableProfiler=true"}"""));

        var act = () => client.StartProfilingAsync(500);

        var ex = await act.Should().ThrowAsync<DevFlowAgentException>();
        ex.Which.StatusCode.Should().Be(400);
        ex.Which.Message.Should().Be("Profiler is disabled. Set AgentOptions.EnableProfiler=true");
    }

    [Fact]
    public async Task GetProfilerCapabilitiesAsync_ReportsDisabledProfiler()
    {
        var client = CreateV1Client(_ => Json(HttpStatusCode.OK,
            """{"available":false,"supportedInBuild":true,"featureEnabled":false,"platform":"Android"}"""));

        var capabilities = await client.GetProfilerCapabilitiesAsync();

        capabilities.Available.Should().BeFalse();
        capabilities.SupportedInBuild.Should().BeTrue();
        capabilities.FeatureEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task GetProfilerCapabilitiesAsync_WithoutAvailabilityFlags_TreatsProfilerAsAvailable()
    {
        var client = CreateV1Client(_ => Json(HttpStatusCode.OK, """{"platform":"Android","managedMemorySupported":true}"""));

        var capabilities = await client.GetProfilerCapabilitiesAsync();

        capabilities.Available.Should().BeTrue();
        capabilities.SupportedInBuild.Should().BeTrue();
        capabilities.FeatureEnabled.Should().BeTrue();
        capabilities.ManagedMemorySupported.Should().BeTrue();
    }

    [Fact]
    public async Task GetProfilerSamplesAsync_ReadsSpanThreadIdAsNumber()
    {
        var client = CreateV1Client(_ => Json(HttpStatusCode.OK, """
            {
              "SessionId": "s-1",
              "Samples": [],
              "Markers": [],
              "Spans": [
                {
                  "SpanId": "sp-1", "StartTsUtc": "2026-01-01T00:00:00Z", "EndTsUtc": "2026-01-01T00:00:00.120Z",
                  "DurationMs": 120, "Kind": "ui.scroll", "Name": "scroll-view", "Status": "ok", "ThreadId": 1
                }
              ],
              "SampleCursor": 0, "MarkerCursor": 0, "SpanCursor": 1, "IsActive": true
            }
            """));

        var batch = await client.GetProfilerSamplesAsync("s-1");

        batch.Spans.Should().ContainSingle().Which.ThreadId.Should().Be(1);
    }

    private static DevFlowV1Client CreateV1Client(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false));
        return new DevFlowV1Client("localhost", 9223, httpClientFactory.Object, NullLogger.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
