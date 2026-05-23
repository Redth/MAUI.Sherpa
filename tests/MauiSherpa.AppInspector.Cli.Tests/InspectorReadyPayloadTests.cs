using FluentAssertions;

namespace MauiSherpa.AppInspector.Cli.Tests;

public class InspectorReadyPayloadTests
{
    [Fact]
    public void Create_IncludesUrlAgentAndShutdownInformation()
    {
        var options = InspectorCliOptions.Parse([
            "--agent-port", "9231",
            "--agent-id", "agent-1",
            "--project", "project",
            "--session-id", "session",
            "--app-name", "App"
        ]);

        var payload = InspectorReadyPayload.Create(
            options,
            ["http://127.0.0.1:5000"],
            "http://127.0.0.1:5000/inspector/devflow/agent-1/tree?token=abc",
            "abc");

        payload.Status.Should().Be("ready");
        payload.Url.Should().Contain("/inspector/devflow/");
        payload.Endpoints.Should().ContainSingle("http://127.0.0.1:5000");
        payload.Agent.Port.Should().Be(9231);
        payload.Agent.AgentId.Should().Be("agent-1");
        payload.Stop.ShutdownUrl.Should().Be("http://127.0.0.1:5000/internal/shutdown?token=abc");
        payload.Stop.AutoExitIdleSeconds.Should().Be(60);
    }
}
