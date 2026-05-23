using FluentAssertions;

namespace Sherpa.AppInspector.Cli.Tests;

public class InspectorCliOptionsTests
{
    [Fact]
    public void Parse_WhenAgentPortMissing_ReturnsError()
    {
        var options = InspectorCliOptions.Parse(["serve"]);

        options.Error.Should().Contain("--agent-port");
    }

    [Fact]
    public void Parse_WithMinimumPortAlias_ReturnsDefaults()
    {
        var options = InspectorCliOptions.Parse(["--port", "9231"]);

        options.Error.Should().BeNull();
        options.AgentHost.Should().Be("localhost");
        options.AgentPort.Should().Be(9231);
        options.ListenHost.Should().Be("127.0.0.1");
        options.ListenPort.Should().Be(0);
        options.IdleTimeout.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void Parse_WithMetadata_PreservesValues()
    {
        var options = InspectorCliOptions.Parse([
            "serve",
            "--agent-port", "9231",
            "--agent-host", "127.0.0.1",
            "--agent-id", "agent-1",
            "--project", "my-project",
            "--session-id", "session-1",
            "--app-name", "My App",
            "--tab", "logs",
            "--listen-port", "5055",
            "--idle-timeout", "15"
        ]);

        options.Error.Should().BeNull();
        options.AgentHost.Should().Be("127.0.0.1");
        options.AgentId.Should().Be("agent-1");
        options.Project.Should().Be("my-project");
        options.SessionId.Should().Be("session-1");
        options.AppName.Should().Be("My App");
        options.Tab.Should().Be("logs");
        options.ListenPort.Should().Be(5055);
        options.IdleTimeout.Should().Be(TimeSpan.FromSeconds(15));
    }
}
