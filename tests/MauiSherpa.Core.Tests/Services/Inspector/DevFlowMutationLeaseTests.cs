using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MauiSherpa.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace MauiSherpa.Core.Tests.Services.Inspector;

/// <summary>
/// DevFlow agents reject mutations with 409 unless the caller holds the mutation lease
/// (https://github.com/Redth/MAUI.Sherpa/issues/251).
/// </summary>
public class DevFlowMutationLeaseTests
{
    private const string PropertyPath = "/api/v1/ui/elements/el-1/properties/FontSize";

    [Fact]
    public async Task SetPropertyAsync_ClaimsLeaseBeforeMutating()
    {
        var handler = new RecordingHandler(req => req.RequestUri!.AbsolutePath == DevFlowMutationLease.LeasePath
            ? Json(HttpStatusCode.OK, """{"ok":true,"allowed":true,"youHold":true}""")
            : Json(HttpStatusCode.OK, """{"value":"24"}"""));
        var client = CreateV1Client(handler);

        await client.SetPropertyAsync("el-1", "FontSize", "24");

        handler.Requests.Select(r => (r.Method, r.Path)).Should().Equal(
            (HttpMethod.Post, DevFlowMutationLease.LeasePath),
            (HttpMethod.Put, PropertyPath));

        using var claim = JsonDocument.Parse(handler.Requests[0].Body!);
        claim.RootElement.GetProperty("action").GetString().Should().Be("claim");
        var leaseId = claim.RootElement.GetProperty("leaseId").GetString();
        leaseId.Should().NotBeNullOrEmpty();
        handler.Requests[1].LeaseHeader.Should().Be(leaseId);
    }

    [Fact]
    public async Task SetPropertyAsync_WhenAgentPredatesLeases_MutatesWithoutReclaiming()
    {
        var handler = new RecordingHandler(req => req.RequestUri!.AbsolutePath == DevFlowMutationLease.LeasePath
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : Json(HttpStatusCode.OK, """{"value":"24"}"""));
        var client = CreateV1Client(handler);

        await client.SetPropertyAsync("el-1", "FontSize", "24");
        await client.SetPropertyAsync("el-1", "FontSize", "25");

        handler.Requests.Select(r => r.Path).Should().Equal(
            DevFlowMutationLease.LeasePath,
            PropertyPath,
            PropertyPath);
    }

    [Fact]
    public async Task SetPropertyAsync_WhenAgentRejects_ThrowsWithAgentMessage()
    {
        var handler = new RecordingHandler(req => req.RequestUri!.AbsolutePath == DevFlowMutationLease.LeasePath
            ? Json(HttpStatusCode.OK, """{"ok":true,"allowed":false,"youHold":false,"heldByOther":true}""")
            : Json(HttpStatusCode.Conflict,
                """{"success":false,"error":"Another DevFlow session is driving this app. Take control before mutating it.","reason":"lease"}"""));
        var client = CreateV1Client(handler);

        var act = () => client.SetPropertyAsync("el-1", "FontSize", "24");

        var ex = await act.Should().ThrowAsync<DevFlowAgentException>();
        ex.Which.StatusCode.Should().Be(409);
        ex.Which.Message.Should().Be("Another DevFlow session is driving this app. Take control before mutating it.");
    }

    [Fact]
    public async Task LeaseHandler_ClaimsOnlyForV1Mutations()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, "{}"));
        using var http = new HttpClient(new DevFlowMutationLeaseHandler(new DevFlowMutationLease(), handler))
        {
            BaseAddress = new Uri("http://localhost:9223")
        };

        await http.GetAsync("/api/v1/profiler/sessions");
        await http.PostAsync("/api/profiler/start", null);
        await http.PostAsync("/api/v1/profiler/sessions", null);

        handler.Requests.Select(r => (r.Method, r.Path)).Should().Equal(
            (HttpMethod.Get, "/api/v1/profiler/sessions"),
            (HttpMethod.Post, "/api/profiler/start"),
            (HttpMethod.Post, DevFlowMutationLease.LeasePath),
            (HttpMethod.Post, "/api/v1/profiler/sessions"));
        handler.Requests.Should().OnlyContain(r => r.LeaseHeader != null);
    }

    private static DevFlowV1Client CreateV1Client(HttpMessageHandler handler)
    {
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false));
        return new DevFlowV1Client("localhost", 9223, httpClientFactory.Object, NullLogger.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed record RecordedRequest(HttpMethod Method, string Path, string? LeaseHeader, string? Body);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!.AbsolutePath,
                request.Headers.TryGetValues("X-DevFlow-Lease", out var lease) ? lease.Single() : null,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return _respond(request);
        }
    }
}
