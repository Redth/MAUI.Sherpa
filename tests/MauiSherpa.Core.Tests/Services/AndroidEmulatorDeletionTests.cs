using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using Moq;

namespace MauiSherpa.Core.Tests.Services;

public class AndroidEmulatorDeletionTests
{
    private static readonly AvdInfo Emulator = new("Pixel_API_35", "Pixel", "/avd/pixel.avd", "Android 35", null, []);
    private static readonly RunningAndroidEmulator Running = new("emulator-5554", Emulator.Name);
    private static readonly AndroidAdbSnapshot Stopped = new([], []);

    [Fact]
    public async Task StoppedEmulator_IsDeletedWithoutSendingShutdown()
    {
        var sdk = Sdk();
        var adb = Adb(Stopped, Stopped);

        (await DeleteAsync(sdk.Object, adb.Object, Mock.Of<IOperationContext>()))
            .Should().BeTrue();

        sdk.Verify(s => s.StopEmulatorAsync(It.IsAny<string>()), Times.Never);
        sdk.Verify(s => s.DeleteAvdAsync(Emulator.Name), Times.Once);
    }

    [Fact]
    public async Task RunningEmulator_IsStoppedAndConfirmedBeforeDeletion()
    {
        var sdk = Sdk();
        var calls = new List<string>();
        sdk.Setup(s => s.StopEmulatorAsync(Running.Serial)).Callback(() => calls.Add("stop")).ReturnsAsync(true);
        sdk.Setup(s => s.DeleteAvdAsync(Emulator.Name)).Callback(() => calls.Add("delete")).ReturnsAsync(true);
        var adb = Adb(Snapshot(Running), Snapshot(Running), Stopped, Stopped);

        (await DeleteAsync(sdk.Object, adb.Object, Mock.Of<IOperationContext>()))
            .Should().BeTrue();

        calls.Should().Equal("stop", "delete");
    }

    [Fact]
    public async Task MultipleInstances_AllStopBeforeDeletion()
    {
        var second = new RunningAndroidEmulator("emulator-5556", Emulator.Name);
        var sdk = Sdk();
        var adb = Adb(
            Snapshot(Running, second),
            Snapshot(Running, second), Snapshot(second),
            Snapshot(second), Stopped,
            Stopped);

        (await DeleteAsync(sdk.Object, adb.Object, Mock.Of<IOperationContext>()))
            .Should().BeTrue();

        sdk.Verify(s => s.StopEmulatorAsync(Running.Serial), Times.Once);
        sdk.Verify(s => s.StopEmulatorAsync(second.Serial), Times.Once);
        sdk.Verify(s => s.DeleteAvdAsync(Emulator.Name), Times.Once);
    }

    [Fact]
    public async Task UnidentifiedEmulator_PreventsDeletion()
    {
        var sdk = Sdk();
        var context = new Mock<IOperationContext>();
        var adb = Adb(Snapshot(new RunningAndroidEmulator("emulator-5554", null)));

        (await DeleteAsync(sdk.Object, adb.Object, context.Object)).Should().BeFalse();

        sdk.Verify(s => s.StopEmulatorAsync(It.IsAny<string>()), Times.Never);
        sdk.Verify(s => s.DeleteAvdAsync(It.IsAny<string>()), Times.Never);
        context.Verify(c => c.LogError(It.Is<string>(m => m.Contains("could not be identified"))), Times.Once);
    }

    [Fact]
    public async Task ShutdownFailure_PreventsDeletion()
    {
        var sdk = Sdk();
        sdk.Setup(s => s.StopEmulatorAsync(Running.Serial)).ReturnsAsync(false);
        var adb = Adb(Snapshot(Running), Snapshot(Running));

        (await DeleteAsync(sdk.Object, adb.Object, Mock.Of<IOperationContext>()))
            .Should().BeFalse();

        sdk.Verify(s => s.DeleteAvdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ShutdownTimeout_IsReportedRatherThanClaimingSuccess()
    {
        var sdk = Sdk();
        var adb = Adb(Snapshot(Running), Snapshot(Running));
        var context = new Mock<IOperationContext>();

        (await AndroidEmulatorShutdown.RunAsync(sdk.Object, adb.Object, Running.Serial, Emulator.Name,
            context.Object, TimeSpan.Zero, TimeSpan.Zero)).Should().BeFalse();

        context.Verify(c => c.LogError(It.Is<string>(m => m.Contains("did not finish stopping"))), Times.Once);
    }

    [Fact]
    public async Task ChangedSerialIdentity_IsNotStopped()
    {
        var sdk = Sdk();
        var adb = Adb(Snapshot(Running), Snapshot(Running with { AvdName = "Other_AVD" }));

        (await DeleteAsync(sdk.Object, adb.Object, Mock.Of<IOperationContext>()))
            .Should().BeFalse();

        sdk.Verify(s => s.StopEmulatorAsync(It.IsAny<string>()), Times.Never);
        sdk.Verify(s => s.DeleteAvdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task EmulatorThatStartsBeforeFinalCheck_IsNotDeleted()
    {
        var sdk = Sdk();
        var adb = Adb(Stopped, Snapshot(Running));

        (await DeleteAsync(sdk.Object, adb.Object, Mock.Of<IOperationContext>()))
            .Should().BeFalse();

        sdk.Verify(s => s.DeleteAvdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AdbFailure_PreventsDeletion()
    {
        var sdk = Sdk();
        var adb = new Mock<IAndroidAdbService>();
        adb.Setup(a => a.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AndroidAdbException("ADB unavailable"));
        var context = new Mock<IOperationContext>();

        (await DeleteAsync(sdk.Object, adb.Object, context.Object)).Should().BeFalse();

        sdk.Verify(s => s.DeleteAvdAsync(It.IsAny<string>()), Times.Never);
        context.Verify(c => c.LogError(It.Is<string>(m => m.Contains("ADB unavailable"))), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrReplacedEmulator_IsNotDeleted(bool replaced)
    {
        var sdk = Sdk();
        sdk.Setup(s => s.GetAvdsAsync()).ReturnsAsync(replaced
            ? [Emulator with { Path = "/other/path.avd" }]
            : Array.Empty<AvdInfo>());
        var adb = new Mock<IAndroidAdbService>(MockBehavior.Strict);

        (await DeleteAsync(sdk.Object, adb.Object, Mock.Of<IOperationContext>()))
            .Should().BeFalse();

        adb.VerifyNoOtherCalls();
        sdk.Verify(s => s.DeleteAvdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CancellationAfterShutdown_DoesNotDelete()
    {
        using var cancellation = new CancellationTokenSource();
        var sdk = Sdk();
        sdk.Setup(s => s.StopEmulatorAsync(Running.Serial)).Callback(cancellation.Cancel).ReturnsAsync(true);
        var adb = Adb(Snapshot(Running), Snapshot(Running));
        var context = new Mock<IOperationContext>();
        context.SetupGet(c => c.CancellationToken).Returns(cancellation.Token);

        var action = () => DeleteAsync(sdk.Object, adb.Object, context.Object);

        await action.Should().ThrowAsync<OperationCanceledException>();
        sdk.Verify(s => s.DeleteAvdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task BusyInstanceLock_PreventsDeletionAfterAdbDisconnects()
    {
        var sdk = Sdk();
        var adb = Adb(Stopped);
        var context = new Mock<IOperationContext>();

        var result = await AndroidEmulatorDeletion.RunAsync(sdk.Object, adb.Object, Emulator, context.Object,
            (_, _) => Task.FromResult<IDisposable?>(null));

        result.Should().BeFalse();
        sdk.Verify(s => s.DeleteAvdAsync(It.IsAny<string>()), Times.Never);
        context.Verify(c => c.LogError(It.Is<string>(m => m.Contains("still using its files"))), Times.Once);
    }

    [Fact]
    public async Task UncheckableInstanceLock_ReportsErrorAndPreventsDeletion()
    {
        var sdk = Sdk();
        var adb = Adb(Stopped);
        var context = new Mock<IOperationContext>();

        var result = await AndroidEmulatorDeletion.RunAsync(sdk.Object, adb.Object, Emulator, context.Object,
            (_, _) => throw new UnauthorizedAccessException("Access denied"));

        result.Should().BeFalse();
        sdk.Verify(s => s.DeleteAvdAsync(It.IsAny<string>()), Times.Never);
        context.Verify(c => c.LogError(It.Is<string>(m => m.Contains("Access denied"))), Times.Once);
    }

    [Fact]
    public async Task ReplacedEmulatorAfterLock_IsNotDeleted()
    {
        var sdk = Sdk();
        sdk.SetupSequence(s => s.GetAvdsAsync())
            .ReturnsAsync([Emulator])
            .ReturnsAsync([Emulator with { Path = "/other/path.avd" }]);
        var adb = Adb(Stopped, Stopped);

        (await DeleteAsync(sdk.Object, adb.Object, Mock.Of<IOperationContext>())).Should().BeFalse();

        sdk.Verify(s => s.DeleteAvdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task InstanceLock_IsHeldDuringDeletionExceptOnWindows()
    {
        var sdk = Sdk();
        var adb = Adb(Stopped, Stopped);
        var instanceLock = new Mock<IDisposable>();
        var disposed = false;
        instanceLock.Setup(l => l.Dispose()).Callback(() => disposed = true);
        sdk.Setup(s => s.DeleteAvdAsync(Emulator.Name))
            .Callback(() => disposed.Should().Be(OperatingSystem.IsWindows()))
            .ReturnsAsync(true);

        var result = await AndroidEmulatorDeletion.RunAsync(sdk.Object, adb.Object, Emulator,
            Mock.Of<IOperationContext>(), (_, _) => Task.FromResult<IDisposable?>(instanceLock.Object));

        result.Should().BeTrue();
        disposed.Should().BeTrue();
    }

    private static Task<bool> DeleteAsync(IAndroidSdkService sdk, IAndroidAdbService adb, IOperationContext context) =>
        AndroidEmulatorDeletion.RunAsync(sdk, adb, Emulator, context,
            (_, _) => Task.FromResult<IDisposable?>(Mock.Of<IDisposable>()));

    private static AndroidAdbSnapshot Snapshot(params RunningAndroidEmulator[] instances) => new([], instances);

    private static Mock<IAndroidSdkService> Sdk()
    {
        var sdk = new Mock<IAndroidSdkService>();
        sdk.Setup(s => s.GetAvdsAsync()).ReturnsAsync([Emulator]);
        sdk.Setup(s => s.StopEmulatorAsync(It.IsAny<string>())).ReturnsAsync(true);
        sdk.Setup(s => s.DeleteAvdAsync(It.IsAny<string>())).ReturnsAsync(true);
        return sdk;
    }

    private static Mock<IAndroidAdbService> Adb(params AndroidAdbSnapshot[] snapshots)
    {
        var adb = new Mock<IAndroidAdbService>();
        var sequence = adb.SetupSequence(a => a.GetSnapshotAsync(It.IsAny<CancellationToken>()));
        foreach (var snapshot in snapshots)
            sequence.ReturnsAsync(snapshot);
        return adb;
    }
}
