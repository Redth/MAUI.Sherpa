using System.Diagnostics;
using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using Moq;
using static MauiSherpa.Core.Services.AndroidWirelessPairingService;

namespace MauiSherpa.Core.Tests.Services;

public class AndroidWirelessPairingServiceTests
{
    private readonly Mock<ILoggingService> logger = new();
    private readonly List<(ProcessStartInfo Start, string? Input)> requests = [];
    private readonly Queue<AdbCommandResult> results = new();

    private AndroidWirelessPairingService CreateService(string? adbPath = "/sdk/platform-tools/adb") =>
        new(logger.Object, () => adbPath, (start, input, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            requests.Add((start, input));
            return Task.FromResult(results.Dequeue());
        });

    [Theory]
    [InlineData("192.168.1.20:37123", "192.168.1.20:37123")]
    [InlineData(" 192.168.1.20:037123 ", "192.168.1.20:37123")]
    [InlineData("device.local:1", "device.local:1")]
    [InlineData("[fe80::1234%en0]:65535", "[fe80::1234%en0]:65535")]
    [InlineData("[::1]:5555", "[::1]:5555")]
    public void TryNormalizeEndpoint_AcceptsExplicitHostAndValidPort(string input, string expected)
    {
        TryNormalizeEndpoint(input, out var endpoint).Should().BeTrue();
        endpoint.Should().Be(expected);
    }

    [Theory]
    [InlineData("192.168.2.169:46167", true)]
    [InlineData("adb-38020DLJG004ZG-6tWvZf._adb-tls-connect._tcp", true)]
    [InlineData("adb-starkb590._adb._tcp", true)]
    [InlineData("38020DLJG004ZG", false)]
    [InlineData("emulator-5554", false)]
    public void IsWirelessSerial_DistinguishesWiFiFromUsb(string serial, bool wireless)
    {
        IsWirelessSerial(serial).Should().Be(wireless);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("192.168.1.20")]
    [InlineData("192.168.1.20:0")]
    [InlineData("192.168.1.20:65536")]
    [InlineData("192.168.1.20:-1")]
    [InlineData("192.168.1.20:+5555")]
    [InlineData("192.168.1.20: 5555")]
    [InlineData("192.168.1.20:5555;touch file")]
    [InlineData("--help:5555")]
    [InlineData("http://device.local:5555")]
    [InlineData("host with spaces:5555")]
    [InlineData("fe80::1234:5555")]
    [InlineData("[device.local]:5555")]
    public void TryNormalizeEndpoint_RejectsMissingOrInvalidAddress(string? input)
    {
        TryNormalizeEndpoint(input, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    [InlineData("１２３４５６")]
    public async Task PairAsync_RejectsInvalidCodeWithoutRunningAdb(string code)
    {
        var result = await CreateService().PairAsync("192.168.1.20:37123", code);

        result.State.Should().Be(AndroidWirelessOperationState.Failed);
        result.Message.Should().Contain("six-digit");
        requests.Should().BeEmpty();
    }

    [Fact]
    public async Task PairAsync_UsesStdinAndDoesNotTreatPairingAsConnected()
    {
        results.Enqueue(new(0, "Enter pairing code: Successfully paired to 192.168.1.20:37123 [guid=adb-device]\n"));

        var result = await CreateService().PairAsync("192.168.1.20:37123", "123456");

        result.Success.Should().BeTrue();
        result.State.Should().Be(AndroidWirelessOperationState.Paired);
        var request = requests.Should().ContainSingle().Which;
        request.Start.FileName.Should().Be("/sdk/platform-tools/adb");
        request.Start.UseShellExecute.Should().BeFalse();
        request.Start.ArgumentList.Should().Equal("pair", "192.168.1.20:37123");
        request.Input.Should().Be("123456");
        request.Start.Arguments.Should().NotContain("123456");
        logger.Invocations.SelectMany(i => i.Arguments).OfType<string>()
            .Should().NotContain(s => s.Contains("123456", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PairAsync_RedactsTheCodeFromAdbFailureOutputAndLogs()
    {
        results.Enqueue(new(1, "Code 123456 was rejected", "Failed with code 123456"));

        var result = await CreateService().PairAsync("192.168.1.20:37123", "123456");

        result.Success.Should().BeFalse();
        result.Message.Should().NotContain("123456").And.Contain("[redacted]");
        logger.Invocations.SelectMany(i => i.Arguments).OfType<string>()
            .Should().NotContain(s => s.Contains("123456", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PairAsync_DoesNotTrustExitCodeAlone()
    {
        results.Enqueue(new(0, "Failed: wrong password"));
        var result = await CreateService().PairAsync("192.168.1.20:37123", "123456");
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task MissingAdb_ProvidesInstallationGuidance()
    {
        var result = await CreateService(null).PairAsync("192.168.1.20:37123", "123456");
        result.Message.Should().Contain("platform-tools").And.Contain("Settings");
        result.Success.Should().BeFalse();
        requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ConnectAsync_UsesDebuggingPortAndVerifiesDeviceIsOnline()
    {
        results.Enqueue(new(0, "connected to 192.168.1.20:40555"));
        results.Enqueue(new(0, "device\n"));

        var result = await CreateService().ConnectAsync("192.168.1.20:40555");

        result.State.Should().Be(AndroidWirelessOperationState.Connected);
        requests[0].Start.ArgumentList.Should().Equal("connect", "192.168.1.20:40555");
        requests[1].Start.ArgumentList.Should().Equal("-s", "192.168.1.20:40555", "get-state");
        requests.Should().OnlyContain(r => r.Input == null);
    }

    [Fact]
    public async Task ConnectAsync_RecognizesAnAlreadyConnectedDevice()
    {
        results.Enqueue(new(0, "already connected to 192.168.1.20:40555"));
        results.Enqueue(new(0, "device"));
        (await CreateService().ConnectAsync("192.168.1.20:40555")).Success.Should().BeTrue();
    }

    [Fact]
    public async Task ConnectAsync_DoesNotReportOfflineDeviceAsConnected()
    {
        results.Enqueue(new(0, "connected to 192.168.1.20:40555"));
        results.Enqueue(new(0, "offline"));
        results.Enqueue(new(0, "unauthorized"));
        results.Enqueue(new(1, "", "error: device not found"));

        var result = await CreateService().ConnectAsync("192.168.1.20:40555");

        result.State.Should().Be(AndroidWirelessOperationState.ConnectionPending);
        result.Success.Should().BeFalse();
        requests.Should().HaveCount(4);
    }

    [Fact]
    public async Task ConnectAsync_DoesNotTrustZeroExitCodeForARefusedConnection()
    {
        results.Enqueue(new(0, "failed to connect to 192.168.1.20:40555: Connection refused"));

        var result = await CreateService().ConnectAsync("192.168.1.20:40555");

        result.State.Should().Be(AndroidWirelessOperationState.Failed);
        result.Message.Should().Contain("Connection refused");
        requests.Should().ContainSingle();
    }

    [Fact]
    public async Task DiscoverAsync_SeparatesPairingAndConnectionPortsAndIgnoresLegacyAdb()
    {
        results.Enqueue(new(0, """
            List of discovered mdns services
            adb-device    _adb-tls-pairing._tcp    192.168.1.20:37123
            adb-device-connect    _adb-tls-connect._tcp.    192.168.1.20:40555
            adb-other    _adb-tls-connect._tcp    [fe80::1234%en0]:44444
            old-device    _adb._tcp    192.168.1.30:5555
            adb-device    _adb-tls-pairing._tcp    192.168.1.20:37123
            """));

        var result = await CreateService().DiscoverAsync();

        result.Success.Should().BeTrue();
        result.Services.Should().HaveCount(3);
        result.Services.Where(s => s.IsPairing).Should().ContainSingle()
            .Which.Endpoint.Should().Be("192.168.1.20:37123");
        result.Services.Where(s => !s.IsPairing).Select(s => s.Endpoint)
            .Should().Equal("192.168.1.20:40555", "[fe80::1234%en0]:44444");
        requests.Should().ContainSingle().Which.Start.ArgumentList.Should().Equal("mdns", "services");
    }

    [Fact]
    public async Task DiscoverAsync_ReportsNoDevicesWithoutInventingAnAddress()
    {
        results.Enqueue(new(0, "List of discovered mdns services\n"));
        var result = await CreateService().DiscoverAsync();
        result.Success.Should().BeTrue();
        result.Services.Should().BeEmpty();
    }

    [Theory]
    [InlineData(1, "adb: unknown command mdns")]
    [InlineData(0, "ERROR: mdns unavailable")]
    [InlineData(0, "unrecognized output")]
    [InlineData(0, "adb-device _adb-tls-pairing._tcp bad-address")]
    public async Task DiscoverAsync_ProvidesManualFallbackForFailures(int exitCode, string output)
    {
        results.Enqueue(new(exitCode, output));
        var result = await CreateService().DiscoverAsync();
        result.Success.Should().BeFalse();
        result.Services.Should().BeEmpty();
        result.Message.Should().Contain("manually");
    }

    [Fact]
    public async Task CancelledOperation_DoesNotStartAdb()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var action = () => CreateService().PairAsync("192.168.1.20:37123", "123456", cts.Token);
        await action.Should().ThrowAsync<OperationCanceledException>();
        requests.Should().BeEmpty();
    }

    [Fact]
    public async Task CommandTimeout_ReturnsActionableFailure()
    {
        var service = new AndroidWirelessPairingService(logger.Object, () => "/sdk/adb",
            (_, _, _) => throw new OperationCanceledException());

        var result = await service.PairAsync("192.168.1.20:37123", "123456");

        result.State.Should().Be(AndroidWirelessOperationState.Failed);
        result.Message.Should().Contain("30 seconds");
    }

    [Fact]
    public async Task CancellationDuringCommand_PropagatesInsteadOfReportingFailure()
    {
        using var cts = new CancellationTokenSource();
        var service = new AndroidWirelessPairingService(logger.Object, () => "/sdk/adb",
            (_, _, ct) =>
            {
                cts.Cancel();
                return Task.FromCanceled<AdbCommandResult>(ct);
            });

        var action = () => service.PairAsync("192.168.1.20:37123", "123456", cts.Token);
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ProcessFailure_IsReportedWithoutEchoingSensitiveInput()
    {
        var service = new AndroidWirelessPairingService(logger.Object, () => "/sdk/adb",
            (_, _, _) => throw new System.ComponentModel.Win32Exception("Cannot launch adb"));

        var result = await service.PairAsync("192.168.1.20:37123", "123456");

        result.State.Should().Be(AndroidWirelessOperationState.Failed);
        result.Message.Should().Contain("Could not run ADB").And.NotContain("123456");
    }

    [Fact]
    public void CreateQrPairingSession_GeneratesFreshPrivateCredentialsAndPortablePng()
    {
        var service = CreateService();
        var first = service.CreateQrPairingSession();
        var second = service.CreateQrPairingSession();

        first.ServiceName.Should().MatchRegex("^studio-sherpa-[a-f0-9]{20}$").And.NotBe(second.ServiceName);
        first.Password.Should().MatchRegex("^[a-f0-9]{32}$").And.NotBe(second.Password);
        first.ImageDataUrl.Should().StartWith("data:image/png;base64,").And.NotBe(second.ImageDataUrl);
        var png = Convert.FromBase64String(first.ImageDataUrl["data:image/png;base64,".Length..]);
        png.Take(8).Should().Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        first.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(3), TimeSpan.FromSeconds(2));
        first.ToString().Should().NotContain(first.Password).And.NotContain(first.ImageDataUrl);
        requests.Should().BeEmpty();
        logger.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task PairQrAsync_PairsOnlyTheMatchingQrServiceThroughStdin()
    {
        var service = CreateService();
        var session = service.CreateQrPairingSession();
        results.Enqueue(new(0, $"""
            List of discovered mdns services
            studio-another-device _adb-tls-pairing._tcp 192.168.1.21:11111
            {session.ServiceName} _adb-tls-connect._tcp 192.168.1.20:40555
            {session.ServiceName} _adb-tls-pairing._tcp. 192.168.1.20:37123
            """));
        results.Enqueue(new(0, "Enter pairing code: Successfully paired to 192.168.1.20:37123 [guid=adb-device]\n"));

        var result = await service.PairQrAsync(session);

        result.State.Should().Be(AndroidWirelessOperationState.Paired);
        requests.Should().HaveCount(2);
        requests[0].Start.ArgumentList.Should().Equal("mdns", "services");
        requests[1].Start.ArgumentList.Should().Equal("pair", "192.168.1.20:37123");
        requests[1].Input.Should().Be(session.Password);
        requests[1].Start.ArgumentList.Should().NotContain(session.Password);
        result.Message.Should().NotContain(session.Password);
        logger.Invocations.SelectMany(i => i.Arguments).OfType<string>()
            .Should().NotContain(s => s.Contains(session.Password, StringComparison.Ordinal));
    }

    [Fact]
    public async Task PairQrAsync_PollsUntilTheDeviceAdvertisesTheQrService()
    {
        var service = CreateService();
        var session = service.CreateQrPairingSession();
        results.Enqueue(new(0, "List of discovered mdns services\n"));
        results.Enqueue(new(0, $"List of discovered mdns services\n{session.ServiceName} _adb-tls-pairing._tcp 192.168.1.20:37123"));
        results.Enqueue(new(0, "Successfully paired to 192.168.1.20:37123"));

        var result = await service.PairQrAsync(session);

        result.State.Should().Be(AndroidWirelessOperationState.Paired);
        requests.Should().HaveCount(3);
        requests.Take(2).Should().OnlyContain(r => r.Input == null);
    }

    [Fact]
    public async Task PairQrAsync_RedactsQrPasswordsFromPairingFailures()
    {
        var service = CreateService();
        var session = service.CreateQrPairingSession();
        results.Enqueue(new(0, $"List of discovered mdns services\n{session.ServiceName} _adb-tls-pairing._tcp 192.168.1.20:37123"));
        results.Enqueue(new(1, $"Pairing password {session.Password} was rejected", session.Password));

        var result = await service.PairQrAsync(session);

        result.State.Should().Be(AndroidWirelessOperationState.Failed);
        result.Message.Should().NotContain(session.Password).And.Contain("[redacted]")
            .And.Contain("QR scanner").And.NotContain("pairing-code screen");
    }

    [Fact]
    public async Task PairQrAsync_DiscoveryFailureOffersPairingCodeFallbackWithoutLeakingPassword()
    {
        var service = CreateService();
        var session = service.CreateQrPairingSession();
        results.Enqueue(new(1, $"Discovery failed {session.Password}"));

        var result = await service.PairQrAsync(session);

        result.State.Should().Be(AndroidWirelessOperationState.Failed);
        result.Message.Should().Contain("pairing by code").And.NotContain(session.Password);
        requests.Should().ContainSingle();
    }

    [Fact]
    public async Task PairQrAsync_DoesNotReuseACompletedQrSession()
    {
        var service = CreateService();
        var session = service.CreateQrPairingSession();
        results.Enqueue(new(0, $"List of discovered mdns services\n{session.ServiceName} _adb-tls-pairing._tcp 192.168.1.20:37123"));
        results.Enqueue(new(0, "Successfully paired to 192.168.1.20:37123"));
        (await service.PairQrAsync(session)).Success.Should().BeTrue();

        var result = await service.PairQrAsync(session);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("already used");
        requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task PairQrAsync_ExpiredSessionDoesNotStartAdb()
    {
        var session = new AndroidWirelessQrSession("studio-test", "secret", "", DateTimeOffset.UtcNow.AddSeconds(-1));

        var result = await CreateService().PairQrAsync(session);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("expired");
        requests.Should().BeEmpty();
    }

    [Fact]
    public async Task PairQrAsync_ExpiresWhileWaitingInsteadOfPairingAnUnrelatedDevice()
    {
        var session = new AndroidWirelessQrSession("studio-test", "secret", "", DateTimeOffset.UtcNow.AddMilliseconds(100));
        results.Enqueue(new(0, "List of discovered mdns services\nstudio-someone-else _adb-tls-pairing._tcp 192.168.1.21:11111"));

        var result = await CreateService().PairQrAsync(session);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("expired").And.Contain("pair by code");
        requests.Should().ContainSingle().Which.Input.Should().BeNull();
    }

    [Fact]
    public async Task PairQrAsync_CancelledSessionDoesNotStartAdb()
    {
        var service = CreateService();
        var session = service.CreateQrPairingSession();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var action = () => service.PairQrAsync(session, cts.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        requests.Should().BeEmpty();
    }

    [Fact]
    public async Task PairQrAsync_CancellationWhilePollingStopsAndInvalidatesTheOldCode()
    {
        var service = CreateService();
        var session = service.CreateQrPairingSession();
        results.Enqueue(new(0, "List of discovered mdns services\n"));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var action = () => service.PairQrAsync(session, cts.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        (await service.PairQrAsync(session)).Success.Should().BeFalse();
        requests.Should().ContainSingle();
    }

    [Fact]
    public async Task PairQrAsync_CancellationDuringPairingPropagates()
    {
        using var cts = new CancellationTokenSource();
        AndroidWirelessQrSession? session = null;
        var service = new AndroidWirelessPairingService(logger.Object, () => "/sdk/adb", (start, input, ct) =>
        {
            if (input == null)
                return Task.FromResult(new AdbCommandResult(0,
                    $"List of discovered mdns services\n{session!.ServiceName} _adb-tls-pairing._tcp 192.168.1.20:37123"));
            cts.Cancel();
            return Task.FromCanceled<AdbCommandResult>(ct);
        });
        session = service.CreateQrPairingSession();

        var action = () => service.PairQrAsync(session, cts.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    private AndroidWirelessPairingService CreateFreshDiscoveryService(TimeSpan? connectAfterPair = null) =>
        new(logger.Object, () => "/sdk/platform-tools/adb", (start, input, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            requests.Add((start, input));
            return Task.FromResult(results.Dequeue());
        }, DiscoveryServerPort, connectAfterPair);

    private static bool UsesDiscoveryServer(ProcessStartInfo start) =>
        start.ArgumentList.Take(2).SequenceEqual(["-P", DiscoveryServerPort.ToString()]);

    [Fact]
    public async Task DiscoverAsync_FindsDevicesTheStaleMainServerMisses()
    {
        results.Enqueue(new(0, "List of discovered mdns services\n"));
        results.Enqueue(new(0, "List of discovered mdns services\nstudio-sherpa-1\t_adb-tls-pairing._tcp\t192.168.2.169:36811\n"));

        var result = await CreateFreshDiscoveryService().DiscoverAsync();

        result.Success.Should().BeTrue();
        result.Services.Should().ContainSingle().Which.Should().Be(new AndroidWirelessService("studio-sherpa-1", "192.168.2.169:36811", true));
        requests[0].Start.ArgumentList.Should().Equal("mdns", "services");
        requests[0].Start.Environment.Should().NotContainKey("ADB_USB");
        requests[1].Start.ArgumentList.Should().Equal("-P", DiscoveryServerPort.ToString(), "mdns", "services");
        requests[1].Start.Environment["ADB_USB"].Should().Be("0");
        requests[1].Start.Environment["ADB_EMU"].Should().Be("0");
        requests[1].Start.Environment["ADB_MDNS_AUTO_CONNECT"].Should().Be("0");
        if (OperatingSystem.IsMacOS())
            requests[1].Start.Environment["ADB_MDNS_OPENSCREEN"].Should().Be("0");
        else
            requests[1].Start.Environment.Should().NotContainKey("ADB_MDNS_OPENSCREEN");
    }

    [Fact]
    public async Task DiscoverAsync_MergesBothServersWithoutDuplicates()
    {
        const string both = "List of discovered mdns services\nadb-a _adb-tls-connect._tcp 192.168.1.20:40555\n";
        results.Enqueue(new(0, both));
        results.Enqueue(new(0, both + "adb-b _adb-tls-pairing._tcp 192.168.1.21:37123\n"));

        var result = await CreateFreshDiscoveryService().DiscoverAsync();

        result.Services.Select(s => s.Name).Should().Equal("adb-a", "adb-b");
    }

    [Fact]
    public async Task DiscoverAsync_FallsBackWhenEitherServerFails()
    {
        results.Enqueue(new(0, "List of discovered mdns services\nadb-a _adb-tls-connect._tcp 192.168.1.20:40555\n"));
        results.Enqueue(new(1, "", "cannot bind"));
        var mainOnly = await CreateFreshDiscoveryService().DiscoverAsync();
        mainOnly.Success.Should().BeTrue();
        mainOnly.Services.Should().ContainSingle();

        results.Enqueue(new(1, "adb: unknown command mdns"));
        results.Enqueue(new(0, "List of discovered mdns services\nadb-b _adb-tls-pairing._tcp 192.168.1.21:37123\n"));
        var freshOnly = await CreateFreshDiscoveryService().DiscoverAsync();
        freshOnly.Success.Should().BeTrue();
        freshOnly.Services.Should().ContainSingle().Which.Name.Should().Be("adb-b");
    }

    [Fact]
    public async Task StopDiscoveryAsync_StopsOnlyThePrivateServerItStarted()
    {
        var service = CreateFreshDiscoveryService();
        await service.StopDiscoveryAsync();
        requests.Should().BeEmpty();

        results.Enqueue(new(0, "List of discovered mdns services\n"));
        results.Enqueue(new(0, "List of discovered mdns services\n"));
        results.Enqueue(new(0, ""));
        await service.DiscoverAsync();
        await service.StopDiscoveryAsync();
        await service.StopDiscoveryAsync();

        requests.Should().HaveCount(3);
        requests[2].Start.ArgumentList.Should().Equal("-P", DiscoveryServerPort.ToString(), "kill-server");
    }

    [Fact]
    public async Task PairAsync_ConnectsThePairedDeviceWhenAdbDoesNotAutoConnect()
    {
        results.Enqueue(new(0, "Enter pairing code: Successfully paired to 192.168.2.169:36811 [guid=adb-38020DLJG004ZG-6tWvZf]\n"));
        results.Enqueue(new(0, "List of devices attached\n\n"));
        results.Enqueue(new(0, "List of discovered mdns services\n"));
        results.Enqueue(new(0, "List of discovered mdns services\nadb-38020DLJG004ZG-6tWvZf\t_adb-tls-connect._tcp\t192.168.2.169:46167\n"));
        results.Enqueue(new(0, "connected to 192.168.2.169:46167"));
        results.Enqueue(new(0, "device"));

        var result = await CreateFreshDiscoveryService(TimeSpan.FromSeconds(5)).PairAsync("192.168.2.169:36811", "123456");

        result.State.Should().Be(AndroidWirelessOperationState.Connected);
        result.Message.Should().Contain("connected");
        requests[1].Start.ArgumentList.Should().Equal("devices");
        requests[4].Start.ArgumentList.Should().Equal("connect", "192.168.2.169:46167");
        requests.Where(r => r.Start.ArgumentList.Contains("connect")).Should().OnlyContain(r => !UsesDiscoveryServer(r.Start));
    }

    [Fact]
    public async Task PairAsync_DoesNotDuplicateAConnectionAdbAlreadyMade()
    {
        results.Enqueue(new(0, "Successfully paired to 192.168.2.169:36811 [guid=adb-device]"));
        results.Enqueue(new(0, "List of devices attached\nadb-device._adb-tls-connect._tcp\tdevice\n"));

        var result = await CreateFreshDiscoveryService(TimeSpan.FromSeconds(5)).PairAsync("192.168.2.169:36811", "123456");

        result.State.Should().Be(AndroidWirelessOperationState.Connected);
        requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task PairAsync_ReportsPairedWhenTheDebuggingServiceNeverAppears()
    {
        results.Enqueue(new(0, "Successfully paired to 192.168.2.169:36811 [guid=adb-device]"));
        results.Enqueue(new(0, "List of devices attached\n"));
        results.Enqueue(new(0, "List of discovered mdns services\n"));
        results.Enqueue(new(0, "List of discovered mdns services\n"));

        var result = await CreateFreshDiscoveryService(TimeSpan.FromTicks(1)).PairAsync("192.168.2.169:36811", "123456");

        result.State.Should().Be(AndroidWirelessOperationState.Paired);
        result.Message.Should().Contain("does not guarantee");
        requests.Should().HaveCount(4);
    }

    private AndroidWirelessPairingService CreateReachabilityService(bool sherpaCanReach) =>
        new(logger.Object, () => "/sdk/platform-tools/adb", (start, input, ct) =>
        {
            requests.Add((start, input));
            return Task.FromResult(results.Dequeue());
        }, canReach: (_, _) => Task.FromResult(sherpaCanReach));

    [Fact]
    public async Task PairAsync_ExplainsAnAdbServerBlockedFromTheLocalNetwork()
    {
        results.Enqueue(new(1, "Enter pairing code: ", "error: protocol fault (couldn't read status message): Undefined error: 0"));

        var result = await CreateReachabilityService(sherpaCanReach: true).PairAsync("192.168.2.169:45545", "123456");

        result.State.Should().Be(AndroidWirelessOperationState.Failed);
        result.LocalNetworkBlocked.Should().Be(OperatingSystem.IsMacOS());
        if (OperatingSystem.IsMacOS())
            result.Message.Should().Contain("Local Network").And.Contain("Restart ADB").And.NotContain("protocol fault");
    }

    [Fact]
    public async Task PairAsync_DoesNotBlameMacOSWhenTheDeviceIsUnreachable()
    {
        results.Enqueue(new(1, "", "error: protocol fault (couldn't read status message): Undefined error: 0"));

        var result = await CreateReachabilityService(sherpaCanReach: false).PairAsync("192.168.2.169:45545", "123456");

        result.LocalNetworkBlocked.Should().BeFalse();
        result.Message.Should().Contain("Pairing failed");
    }

    [Fact]
    public async Task ConnectAsync_ExplainsNoRouteToHostAsALocalNetworkBlock()
    {
        results.Enqueue(new(1, "failed to connect to '192.168.2.169:46167': No route to host"));

        var result = await CreateService().ConnectAsync("192.168.2.169:46167");

        result.Success.Should().BeFalse();
        result.LocalNetworkBlocked.Should().Be(OperatingSystem.IsMacOS());
    }

    [Fact]
    public async Task RestartAdbServerAsync_RestartsTheMainServer()
    {
        results.Enqueue(new(0, ""));
        results.Enqueue(new(0, "* daemon started successfully"));

        (await CreateService().RestartAdbServerAsync()).Should().BeTrue();

        requests.Select(r => string.Join(' ', r.Start.ArgumentList)).Should().Equal("kill-server", "start-server");
        requests.Should().OnlyContain(r => !r.Start.Environment.ContainsKey("ADB_USB"));
    }

    [Fact]
    public async Task RunProcessAsync_WritesCodeToStdinAndCapturesPromptWithoutNewline()
    {
        if (OperatingSystem.IsWindows())
            return;
        var start = CreateShell("read code; printf 'Enter pairing code: Successfully paired to device:1234 [%s]' \"$code\"");
        var result = await RunProcessAsync(start, "123456", CancellationToken.None);
        result.ExitCode.Should().Be(0);
        result.Output.Should().Be("Enter pairing code: Successfully paired to device:1234 [123456]");
        start.ArgumentList.Should().NotContain("123456");
    }

    [Fact]
    public async Task RunProcessAsync_CancellationTerminatesOnlyItsOwnedProcess()
    {
        if (OperatingSystem.IsWindows())
            return;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var action = () => RunProcessAsync(CreateShell("sleep 30"), null, cts.Token);
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    private static ProcessStartInfo CreateShell(string command)
    {
        var start = new ProcessStartInfo("/bin/sh")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(command);
        return start;
    }
}
