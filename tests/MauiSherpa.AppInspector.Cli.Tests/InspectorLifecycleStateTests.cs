using FluentAssertions;

namespace Sherpa.AppInspector.Cli.Tests;

public class InspectorLifecycleStateTests
{
    [Fact]
    public void IsIdle_BeforeFirstHeartbeat_ReturnsFalse()
    {
        var state = new InspectorLifecycleState(TimeSpan.FromSeconds(1), noAutoExit: false);

        state.IsIdle(DateTimeOffset.UtcNow.AddMinutes(5)).Should().BeFalse();
    }

    [Fact]
    public void IsIdle_AfterTimeoutFromHeartbeat_ReturnsTrue()
    {
        var state = new InspectorLifecycleState(TimeSpan.FromMilliseconds(1), noAutoExit: false);
        state.MarkHeartbeat();

        state.IsIdle(DateTimeOffset.UtcNow.AddSeconds(1)).Should().BeTrue();
    }
}
