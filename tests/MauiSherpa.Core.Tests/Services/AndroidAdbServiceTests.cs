using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using Moq;

namespace MauiSherpa.Core.Tests.Services;

public class AndroidAdbServiceTests
{
    [Fact]
    public async Task Snapshot_UsesExactAvdNameAndKeepsUnidentifiedEmulators()
    {
        var runner = new RecordingRunner(
            new(0, "List of devices attached\n192.168.1.5:5555 device model:Pixel_8\nemulator-5554 device model:sdk_gphone64\nemulator-5556 offline\n", ""),
            new(0, "My_AVD\nOK\n", ""),
            new(1, "", "device offline"));
        var service = CreateService(runner);

        var snapshot = await service.GetSnapshotAsync();

        snapshot.Devices.Should().HaveCount(3);
        snapshot.Devices[0].Model.Should().Be("Pixel 8");
        snapshot.Devices[0].IsWireless.Should().BeTrue();
        snapshot.Devices[2].State.Should().Be("offline");
        snapshot.Emulators.Should().Equal(
            new RunningAndroidEmulator("emulator-5554", "My_AVD"),
            new RunningAndroidEmulator("emulator-5556", null));
        runner.Calls[1].Arguments.Should().Equal("-s", "emulator-5554", "emu", "avd", "name");
    }

    [Theory]
    [InlineData(1, "List of devices attached\n")]
    [InlineData(0, "unexpected response")]
    [InlineData(0, "List of devices attached\nemulator-5554\n")]
    public async Task Snapshot_RejectsFailedOrUnreadableDeviceLists(int exitCode, string output)
    {
        var service = CreateService(new RecordingRunner(new AndroidAdbCommandResult(exitCode, output, "")));

        var action = () => service.GetSnapshotAsync();

        await action.Should().ThrowAsync<AndroidAdbException>();
    }

    [Fact]
    public async Task PairingCode_IsSentOnlyThroughStandardInput()
    {
        var runner = new RecordingRunner(new AndroidAdbCommandResult(0, "Enter pairing code: Successfully paired to device", ""));
        var service = CreateService(runner);

        await service.PairAsync("192.168.1.5:12345", "012345");

        runner.Calls.Should().ContainSingle();
        runner.Calls[0].Arguments.Should().Equal("pair", "192.168.1.5:12345");
        runner.Calls[0].Input.Should().Be("012345");
    }

    [Fact]
    public async Task PairingFailure_DoesNotExposeCommandOutputOrCode()
    {
        var service = CreateService(new RecordingRunner(new AndroidAdbCommandResult(1, "secret 012345", "secret 012345")));

        var action = () => service.PairAsync("192.168.1.5:12345", "012345");

        var error = await action.Should().ThrowAsync<AndroidAdbException>();
        error.Which.Message.Should().NotContain("012345").And.NotContain("secret");
    }

    [Theory]
    [InlineData("connected to 192.168.1.5:12345", true)]
    [InlineData("already connected to 192.168.1.5:12345", true)]
    [InlineData("failed to connect to 192.168.1.5:12345", false)]
    public async Task Connect_ChecksOutputEvenWhenAdbReturnsZero(string output, bool success)
    {
        var service = CreateService(new RecordingRunner(new AndroidAdbCommandResult(0, output, "")));
        var action = () => service.ConnectAsync("192.168.1.5:12345");

        if (success)
            await action.Should().NotThrowAsync();
        else
            await action.Should().ThrowAsync<AndroidAdbException>();
    }

    [Theory]
    [InlineData("192.168.1.5:12345", true)]
    [InlineData("phone.local:5555", true)]
    [InlineData("[::1]:5555", true)]
    [InlineData("192.168.1.5", false)]
    [InlineData("192.168.1.5:0", false)]
    [InlineData("192.168.1.5:65536", false)]
    [InlineData("::1:5555", false)]
    [InlineData("host:123\nother", false)]
    [InlineData("-option:123", false)]
    public void EndpointValidation_RequiresAddressAndPort(string endpoint, bool valid) =>
        AndroidAdbService.IsValidEndpoint(endpoint).Should().Be(valid);

    [Theory]
    [InlineData("012345", true)]
    [InlineData("12345", false)]
    [InlineData("12345a", false)]
    [InlineData("１２３４５６", false)]
    public void PairingCodeValidation_RequiresSixAsciiDigits(string code, bool valid) =>
        AndroidAdbService.IsValidPairingCode(code).Should().Be(valid);

    [Theory]
    [InlineData("R123456", false)]
    [InlineData("192.168.1.5:5555", true)]
    [InlineData("adb-serial-abc._adb-tls-connect._tcp", true)]
    public void PhysicalDeviceTransport_RecognizesWirelessSerials(string serial, bool wireless) =>
        new DeviceInfo(serial, "device", null, false).IsWireless.Should().Be(wireless);

    private static AndroidAdbService CreateService(RecordingRunner runner)
    {
        var sdk = new Mock<IAndroidSdkService>();
        sdk.SetupGet(s => s.SdkPath).Returns("/sdk");
        return new AndroidAdbService(sdk.Object, runner);
    }

    private sealed class RecordingRunner(params AndroidAdbCommandResult[] results) : IAndroidAdbRunner
    {
        private readonly Queue<AndroidAdbCommandResult> _results = new(results);
        public List<(IReadOnlyList<string> Arguments, string? Input)> Calls { get; } = [];

        public Task<AndroidAdbCommandResult> RunAsync(
            string adbPath, IReadOnlyList<string> arguments, string? standardInput, TimeSpan timeout, CancellationToken ct)
        {
            Calls.Add((arguments, standardInput));
            return Task.FromResult(_results.Dequeue());
        }
    }
}
