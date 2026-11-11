using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using Moq;

namespace MauiSherpa.Core.Tests.Services;

public class SimulatorDeletionTests
{
    private static SimulatorDevice Simulator(string state = "Shutdown", bool available = true) =>
        new("sim-udid", "Test Simulator", state, available, "device-type", "runtime-id", "iOS", "iPhone", null);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShutdownSimulator_IsDeletedWithoutAnotherShutdown(bool available)
    {
        var simulator = Simulator(available: available);
        var service = Service(simulator);
        var context = new Mock<IOperationContext>();

        var success = await SimulatorDeletion.RunAsync(service.Object, simulator, context.Object);

        success.Should().BeTrue();
        service.Verify(s => s.ShutdownSimulatorAsync(It.IsAny<string>(), It.IsAny<IProgress<string>>()), Times.Never);
        service.Verify(s => s.DeleteSimulatorAsync(simulator.Udid, It.IsAny<IProgress<string>>()), Times.Once);
    }

    [Fact]
    public async Task RunningSimulator_IsStoppedBeforeDeletionUsingFreshState()
    {
        var snapshot = Simulator();
        var service = Service(snapshot with { State = "Booted" });
        var calls = new List<string>();
        service.Setup(s => s.ShutdownSimulatorAsync(snapshot.Udid, It.IsAny<IProgress<string>>()))
            .Callback(() => calls.Add("shutdown")).ReturnsAsync(true);
        service.Setup(s => s.DeleteSimulatorAsync(snapshot.Udid, It.IsAny<IProgress<string>>()))
            .Callback(() => calls.Add("delete")).ReturnsAsync(true);

        (await SimulatorDeletion.RunAsync(service.Object, snapshot, Mock.Of<IOperationContext>())).Should().BeTrue();

        calls.Should().Equal("shutdown", "delete");
    }

    [Fact]
    public async Task ShutdownFailure_PreventsDeletionAndReportsFailure()
    {
        var simulator = Simulator("Booted");
        var service = Service(simulator);
        service.Setup(s => s.ShutdownSimulatorAsync(simulator.Udid, It.IsAny<IProgress<string>>())).ReturnsAsync(false);
        var context = new Mock<IOperationContext>();

        (await SimulatorDeletion.RunAsync(service.Object, simulator, context.Object)).Should().BeFalse();

        service.Verify(s => s.DeleteSimulatorAsync(It.IsAny<string>(), It.IsAny<IProgress<string>>()), Times.Never);
        context.Verify(c => c.LogError(It.Is<string>(message => message.Contains("not deleted"))), Times.Once);
    }

    [Fact]
    public async Task DeleteFailure_IsReported()
    {
        var simulator = Simulator();
        var service = Service(simulator);
        service.Setup(s => s.DeleteSimulatorAsync(simulator.Udid, It.IsAny<IProgress<string>>())).ReturnsAsync(false);
        var context = new Mock<IOperationContext>();

        (await SimulatorDeletion.RunAsync(service.Object, simulator, context.Object)).Should().BeFalse();

        context.Verify(c => c.LogError(It.Is<string>(message => message.Contains("Could not delete"))), Times.Once);
    }

    [Theory]
    [InlineData("Booting")]
    [InlineData("Shutting Down")]
    public async Task TransitioningSimulator_IsNotDeleted(string state)
    {
        var simulator = Simulator(state);
        var service = Service(simulator);
        var context = new Mock<IOperationContext>();

        (await SimulatorDeletion.RunAsync(service.Object, simulator, context.Object)).Should().BeFalse();

        service.Verify(s => s.DeleteSimulatorAsync(It.IsAny<string>(), It.IsAny<IProgress<string>>()), Times.Never);
        context.Verify(c => c.LogError(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task MissingSimulator_IsNotDeleted()
    {
        var simulator = Simulator();
        var service = Service();
        var context = new Mock<IOperationContext>();

        (await SimulatorDeletion.RunAsync(service.Object, simulator, context.Object)).Should().BeFalse();

        service.Verify(s => s.DeleteSimulatorAsync(It.IsAny<string>(), It.IsAny<IProgress<string>>()), Times.Never);
        context.Verify(c => c.LogError(It.Is<string>(message => message.Contains("could not be found"))), Times.Once);
    }

    [Fact]
    public async Task CancellationBeforeStarting_DoesNotTouchSimulators()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new Mock<ISimulatorService>(MockBehavior.Strict);
        var context = new Mock<IOperationContext>();
        context.SetupGet(c => c.CancellationToken).Returns(cancellation.Token);

        var action = () => SimulatorDeletion.RunAsync(service.Object, Simulator(), context.Object);

        await action.Should().ThrowAsync<OperationCanceledException>();
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CancellationAfterShutdown_DoesNotDelete()
    {
        using var cancellation = new CancellationTokenSource();
        var simulator = Simulator("Booted");
        var service = Service(simulator);
        service.Setup(s => s.ShutdownSimulatorAsync(simulator.Udid, It.IsAny<IProgress<string>>()))
            .Callback(cancellation.Cancel).ReturnsAsync(true);
        var context = new Mock<IOperationContext>();
        context.SetupGet(c => c.CancellationToken).Returns(cancellation.Token);

        var action = () => SimulatorDeletion.RunAsync(service.Object, simulator, context.Object);

        await action.Should().ThrowAsync<OperationCanceledException>();
        service.Verify(s => s.DeleteSimulatorAsync(It.IsAny<string>(), It.IsAny<IProgress<string>>()), Times.Never);
    }

    private static Mock<ISimulatorService> Service(params SimulatorDevice[] simulators)
    {
        var service = new Mock<ISimulatorService>();
        service.Setup(s => s.GetSimulatorsAsync()).ReturnsAsync(simulators);
        service.Setup(s => s.DeleteSimulatorAsync(It.IsAny<string>(), It.IsAny<IProgress<string>>())).ReturnsAsync(true);
        return service;
    }
}
