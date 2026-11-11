using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using Moq;

namespace MauiSherpa.Core.Tests.Services;

public class AppleDevicePairingServiceTests
{
    [Fact]
    public void Discovery_ParsesV6ApplePlatformsAndSeparatesPairingFromConnection()
    {
        using var response = Envelope(new { devices = new[]
        {
            Device("phone", "paired", platform: "iOS"),
            Device("tv", "unpaired", platform: "tvOS"),
            Device("watch", "paired", platform: "watchOS"),
            Device("vision", "paired", platform: "visionOS")
        }});

        var devices = PhysicalDeviceService.ParseDevices(response.RootElement);

        devices.Select(d => d.Platform).Should().Equal("iOS", "tvOS", "watchOS", "visionOS");
        devices[0].IsPaired.Should().BeTrue();
        devices[0].IsConnected.Should().BeFalse();
        devices[0].DeveloperModeStatus.Should().Be("enabled");
        devices[0].OsVersion.Should().Be("27.2");
        devices[1].PairingState.Should().Be("unpaired");
    }

    [Fact]
    public void Discovery_ParsesLegacySchemaAndFallsBackToTunnelState()
    {
        using var response = Envelope(new { devices = new[] { new
        {
            identifier = "legacy",
            hardwareProperties = new { udid = "hardware-udid", platform = "tvOS", marketingName = "Apple TV", deviceType = "AppleTV" },
            deviceProperties = new { name = "Living room", osVersionNumber = "26.0", developerModeStatus = "disabled" },
            connectionProperties = new { pairingState = "paired", tunnelState = "connected", transportType = "localNetwork" }
        }}});

        var device = PhysicalDeviceService.ParseDevices(response.RootElement).Single();

        device.Platform.Should().Be("tvOS");
        device.Udid.Should().Be("hardware-udid");
        device.IsPaired.Should().BeTrue();
        device.IsConnected.Should().BeTrue();
        device.DeveloperModeStatus.Should().Be("disabled");
    }

    [Theory]
    [InlineData("fidelity")]
    [InlineData("rootVisibility")]
    [InlineData("stateVisibility")]
    [InlineData("transport")]
    [InlineData("platform")]
    [InlineData("host")]
    public void Discovery_ExcludesSimulatorsAndMacHost(string marker)
    {
        var device = Device("not-physical", "paired");
        switch (marker)
        {
            case "fidelity": device["properties"]!["hardware"]!["fidelity"] = "simulated"; break;
            case "rootVisibility": device["visibilityClass"] = "simulators"; break;
            case "stateVisibility": device["properties"]!["state"]!["visibilityClass"] = "simulators"; break;
            case "transport": device["properties"]!["connection"]!["transportType"] = "sameMachine"; break;
            case "platform": device["properties"]!["hardware"]!["osPlatform"] = "iOS Simulator"; break;
            case "host": device["properties"]!["hardware"]!["osPlatform"] = "macOS"; break;
        }
        using var response = Envelope(new { devices = new[] { device } });
        PhysicalDeviceService.ParseDevices(response.RootElement).Should().BeEmpty();
    }

    [Fact]
    public void Discovery_HandlesStringValueAndDeveloperModeEnum()
    {
        var raw = Device("physical", "paired");
        raw["properties"]!["connection"]!["state"] = new JsonObject { ["stringValue"] = "connected" };
        raw["properties"]!["state"]!["developerModeStatus"] = new JsonObject { ["disabled"] = new JsonObject() };
        using var response = Envelope(new { devices = new[] { raw } });
        var device = PhysicalDeviceService.ParseDevices(response.RootElement).Single();
        device.IsConnected.Should().BeTrue();
        device.DeveloperModeStatus.Should().Be("disabled");
    }

    [Theory]
    [InlineData("fidelity")]
    [InlineData("reality")]
    [InlineData("transportType")]
    public void Discovery_ExcludesLegacySimulatorsWithoutModernProperties(string marker)
    {
        var raw = new JsonObject
        {
            ["identifier"] = "simulator",
            ["hardwareProperties"] = new JsonObject { ["platform"] = "iOS" },
            ["connectionProperties"] = new JsonObject { ["pairingState"] = "paired" }
        };
        if (marker == "transportType")
            raw["connectionProperties"]![marker] = "sameMachine";
        else
            raw["hardwareProperties"]![marker] = "simulated";
        using var response = Envelope(new { devices = new[] { raw } });

        PhysicalDeviceService.ParseDevices(response.RootElement).Should().BeEmpty();
    }

    [Theory]
    [InlineData("wired", "USB", true, false)]
    [InlineData("usb", "USB", true, false)]
    [InlineData("localNetwork", "Wi-Fi", false, true)]
    [InlineData("wifi", "Wi-Fi", false, true)]
    [InlineData("wireless", "Wi-Fi", false, true)]
    [InlineData("unknown", "Unknown connection", false, false)]
    [InlineData("futureTransport", "Unknown connection", false, false)]
    public void Discovery_UsesFriendlyTransportLabelsWithoutGuessingUnknownConnections(
        string transport, string label, bool isWired, bool isWireless)
    {
        using var response = Envelope(new { devices = new[] { Device("physical", "paired", transport: transport) } });
        var device = PhysicalDeviceService.ParseDevices(response.RootElement).Single();

        device.TransportLabel.Should().Be(label);
        device.IsWired.Should().Be(isWired);
        device.IsWireless.Should().Be(isWireless);
        device.IsConnected.Should().BeFalse();
    }

    [Fact]
    public void Discovery_MixedListKeepsOnlyPhysicalDevicesAndTheirLiveTransport()
    {
        var simulator = Device("simulator", "paired");
        simulator["properties"]!["hardware"]!["fidelity"] = "simulated";
        simulator["properties"]!["connection"]!["transportType"] = "sameMachine";
        var usb = Device("usb", "paired");
        usb["properties"]!["connection"]!["state"] = "connected";
        var wifi = Device("wifi", "paired", transport: "localNetwork");
        wifi["properties"]!["connection"]!["state"] = "connected";
        var offline = Device("offline", "paired", transport: "localNetwork");
        using var response = Envelope(new { devices = new[] { simulator, usb, wifi, offline } });

        var devices = PhysicalDeviceService.ParseDevices(response.RootElement);

        devices.Select(d => d.Identifier).Should().Equal("usb", "wifi", "offline");
        devices.Where(d => d.IsConnected).Select(d => d.TransportLabel).Should().Equal("USB", "Wi-Fi");
        devices.Last().IsPaired.Should().BeTrue();
        devices.Last().IsConnected.Should().BeFalse();
    }

    [Fact]
    public void Discovery_RejectsMissingStableIdentifier()
    {
        var raw = Device("physical", "paired");
        raw.Remove("identifier");
        using var response = Envelope(new { devices = new[] { raw } });
        var parse = () => PhysicalDeviceService.ParseDevices(response.RootElement);
        parse.Should().Throw<InvalidOperationException>().WithMessage("*stable identifier*");
    }

    [Fact]
    public void Discovery_RejectsMissingDeviceArrayRatherThanReportingNoDevices()
    {
        using var response = Envelope(new { devices = new { } });
        var parse = () => PhysicalDeviceService.ParseDevices(response.RootElement);
        parse.Should().Throw<InvalidOperationException>().WithMessage("*device list*");
    }

    [Theory]
    [InlineData("""{"info":{"outcome":"success"},"result":{}}""", 1)]
    [InlineData("""{"info":{"outcome":"failure"},"result":{}}""", 0)]
    [InlineData("""{"info":{"outcome":"success"}}""", 0)]
    [InlineData("""{"info":{"outcome":"success"},"result":{},"error":{}}""", 0)]
    [InlineData("""{"result":{}}""", 0)]
    [InlineData("""[]""", 0)]
    [InlineData("""not-json""", 0)]
    public void NativeResponse_RequiresSuccessfulExitOutcomeAndResult(string json, int exitCode)
    {
        var parse = () => AppleDevicePairingRunner.ParseDeviceCtlResponse(json, exitCode);
        parse.Should().Throw<AppleDevicePairingException>();
    }

    [Theory]
    [InlineData("123456")]
    [InlineData("123-456")]
    [InlineData("AB12 CD34")]
    public void WirelessProtocol_PreservesAppleCodeFormatting(string code)
    {
        var parsed = AppleDevicePairingRunner.ParseWirelessEvent(JsonSerializer.Serialize(new { kind = "code", code }));
        parsed.Code.Should().Be(code);
    }

    [Theory]
    [InlineData("""{"kind":"code"}""")]
    [InlineData("""{"kind":"code","code":123456}""")]
    [InlineData("""{"kind":"code","code":""}""")]
    [InlineData("""{"kind":"code","code":"123\n456"}""")]
    [InlineData("""{"kind":"code","code":"123\u202e456"}""")]
    [InlineData("""{"kind":"futureEvent","code":"SECRET"}""")]
    [InlineData("""[]""")]
    [InlineData("""not-json SECRET""")]
    public void WirelessProtocol_RejectsUnsupportedEventsWithoutEchoingPayload(string json)
    {
        var parse = () => AppleDevicePairingRunner.ParseWirelessEvent(json);
        parse.Should().Throw<AppleDevicePairingException>().Which.Message.Should().NotContain("SECRET");
    }

    [Fact]
    public void WirelessProtocol_BoundsCodeAndEventLength()
    {
        var oversizedCode = () => AppleDevicePairingRunner.ParseWirelessEvent(
            JsonSerializer.Serialize(new { kind = "code", code = new string('1', 129) }));
        var oversizedLine = () => AppleDevicePairingRunner.ParseWirelessEvent(new string('1', 4097));
        oversizedCode.Should().Throw<AppleDevicePairingException>();
        oversizedLine.Should().Throw<AppleDevicePairingException>();
    }

    [Fact]
    public void Progress_ToStringDoesNotExposeCodeOrRequestingDevice()
    {
        var progress = new ApplePairingProgress
        {
            Stage = ApplePairingStage.Code, PairingCode = "PRIVATE-CODE", RequestingDevice = "PRIVATE-DEVICE"
        };
        progress.ToString().Should().NotContain("PRIVATE");
    }

    [Fact]
    public async Task Usb_AlreadyPairedOnlyChecksLiveConnection()
    {
        var runner = new FakeRunner();
        var result = await Service(runner).PairUsbAsync("old");
        result.IsLiveVerified.Should().BeTrue();
        runner.PairCalls.Should().Be(0);
        runner.LiveTargets.Should().Equal("old");
        runner.UnpairCalls.Should().Be(0);
    }

    [Fact]
    public async Task Usb_RechecksDetailsBeforePairingInsteadOfTrustingListState()
    {
        var runner = new FakeRunner { State = "unpaired", DetailsState = "paired" };
        var result = await Service(runner).PairUsbAsync("old");
        result.IsLiveVerified.Should().BeTrue();
        runner.PairCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("pairingInProgress")]
    [InlineData("futureState")]
    public async Task Usb_RejectsUnknownAndAlreadyActivePairing(string state)
    {
        var runner = new FakeRunner { State = state };
        var result = await Service(runner).PairUsbAsync("old");
        result.State.Should().Be(ApplePairingState.Failed);
        runner.PairCalls.Should().Be(0);
    }

    [Fact]
    public async Task Usb_RejectsUnpairedNetworkDevice()
    {
        var runner = new FakeRunner { State = "unpaired", Transport = "localNetwork" };
        var result = await Service(runner).PairUsbAsync("old");
        result.State.Should().Be(ApplePairingState.Failed);
        runner.PairCalls.Should().Be(0);
    }

    [Fact]
    public async Task Usb_PairsExactWiredIdentifierThenVerifies()
    {
        var runner = new FakeRunner { State = "unpaired" };
        var result = await Service(runner).PairUsbAsync("old");
        result.IsLiveVerified.Should().BeTrue();
        runner.PairCalls.Should().Be(1);
        runner.PairTargets.Should().Equal("old");
        runner.PairTimeout.Should().Be(120);
        runner.LiveTargets.Should().Equal("old");
    }

    [Fact]
    public async Task Usb_DoesNotSelectByNameOrRevokeTrust()
    {
        var runner = new FakeRunner { State = "unpaired" };
        var result = await Service(runner).PairUsbAsync("Test device");
        result.State.Should().Be(ApplePairingState.Failed);
        runner.PairCalls.Should().Be(0);
    }

    [Fact]
    public async Task Usb_NativeSuccessSurvivesAReadOnlyVerificationFailure()
    {
        var runner = new FakeRunner { State = "unpaired", FailDiscoveryAfterPair = true };
        var result = await Service(runner).PairUsbAsync("old");
        result.State.Should().Be(ApplePairingState.CompletedUnverified);
        runner.PairCalls.Should().Be(1);
        result.IsLiveVerified.Should().BeFalse();
    }

    [Fact]
    public async Task Usb_NativeSuccessWithoutSavedPairingIsNotLiveVerification()
    {
        var runner = new FakeRunner { State = "unpaired", DetailsState = "unpaired" };
        var result = await Service(runner).PairUsbAsync("old");
        result.State.Should().Be(ApplePairingState.CompletedUnverified);
        runner.PairCalls.Should().Be(1);
        runner.LiveTargets.Should().BeEmpty();
    }

    [Fact]
    public async Task Usb_RejectsMismatchedDetailsIdentifier()
    {
        var runner = new FakeRunner { State = "unpaired", DetailsIdentifier = "different-device" };
        var result = await Service(runner).PairUsbAsync("old");
        result.State.Should().Be(ApplePairingState.Failed);
        runner.PairCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Verify_SavedPairingIsNotProofOfLiveAccess(bool malformedProbe)
    {
        var runner = new FakeRunner { MalformedProbe = malformedProbe, FailProbe = !malformedProbe };
        var result = await Service(runner).VerifyAsync("old");
        result.State.Should().Be(ApplePairingState.Paired);
        result.IsLiveVerified.Should().BeFalse();
        runner.PairCalls.Should().Be(0);
    }

    [Fact]
    public async Task Wireless_VerifiesUniqueNewStableIdentifierEvenWithDuplicateNames()
    {
        var runner = new FakeRunner();
        var result = await Service(runner).PairWirelessAsync(new InlineProgress());
        result.IsLiveVerified.Should().BeTrue();
        result.Device!.Identifier.Should().Be("new-0");
        runner.LiveTargets.Should().Equal("new-0");
        runner.PairCalls.Should().Be(0);
        runner.WirelessTimeout.Should().Be(180);
    }

    [Fact]
    public async Task Wireless_DoesNotGuessWhenMultipleDevicesAppear()
    {
        var runner = new FakeRunner { NewDevices = 2 };
        var result = await Service(runner).PairWirelessAsync(new InlineProgress());
        result.State.Should().Be(ApplePairingState.CompletedUnverified);
        runner.LiveTargets.Should().BeEmpty();
    }

    [Fact]
    public async Task Wireless_NativeSuccessWithNoNewRecordIsNotLiveVerification()
    {
        var runner = new FakeRunner { NewDevices = 0 };
        var result = await Service(runner).PairWirelessAsync(new InlineProgress());
        result.State.Should().Be(ApplePairingState.CompletedUnverified);
        result.Device.Should().BeNull();
        runner.LiveTargets.Should().BeEmpty();
    }

    [Fact]
    public async Task Wireless_NativeSuccessSurvivesAReadOnlyVerificationFailure()
    {
        var runner = new FakeRunner { FailDiscoveryAfterWireless = true };
        var result = await Service(runner).PairWirelessAsync(new InlineProgress());
        result.State.Should().Be(ApplePairingState.CompletedUnverified);
        result.IsLiveVerified.Should().BeFalse();
    }

    [Fact]
    public async Task Wireless_CancellationWaitsForRunnerCleanupAndReleasesOperationLock()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new FakeRunner
        {
            OnWireless = async ct =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.Infinite, ct);
            }
        };
        var service = Service(runner);
        using var cancel = new CancellationTokenSource();
        var operation = service.PairWirelessAsync(new InlineProgress(), cancel.Token);
        await entered.Task;
        var duplicate = await service.VerifyAsync("old");
        duplicate.State.Should().Be(ApplePairingState.Failed);
        duplicate.Message.Should().Contain("Another Apple");
        cancel.Cancel();
        var cancelled = async () => await operation;
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        runner.ActiveWirelessSessions.Should().Be(0);
        (await service.VerifyAsync("old")).IsLiveVerified.Should().BeTrue();
    }

    [Fact]
    public async Task Wireless_PrivateApiFailureDoesNotFallBackOrClaimSuccess()
    {
        var runner = new FakeRunner { OnWireless = _ => throw new AppleDevicePairingException("Incompatible private API.") };
        var result = await Service(runner).PairWirelessAsync(new InlineProgress());
        result.State.Should().Be(ApplePairingState.Failed);
        runner.PairCalls.Should().Be(0);
        runner.ActiveWirelessSessions.Should().Be(0);
    }

    [Fact]
    public async Task UnsupportedPlatformDoesNotLaunchAppleTools()
    {
        var runner = new FakeRunner();
        var platform = new Mock<IPlatformService>();
        var service = new AppleDevicePairingService(platform.Object, Mock.Of<ILoggingService>(), runner);
        (await service.PairUsbAsync("old")).State.Should().Be(ApplePairingState.Failed);
        (await service.UnpairAsync("old")).State.Should().Be(ApplePairingState.Failed);
        runner.ListCalls.Should().Be(0);
        service.IsWirelessSupported.Should().BeFalse();
    }

    [Fact]
    public async Task Unpair_TargetsExactStableIdentifierAndConfirmsSavedState()
    {
        var runner = new FakeRunner();
        var result = await Service(runner).UnpairAsync("old");
        result.State.Should().Be(ApplePairingState.Unpaired);
        runner.UnpairTargets.Should().Equal("old");
        runner.UnpairTimeout.Should().Be(60);
        runner.PairCalls.Should().Be(0);
        runner.LiveTargets.Should().BeEmpty();
    }

    [Fact]
    public async Task Unpair_RefusesNamesAndAlreadyUnpairedDevices()
    {
        var runner = new FakeRunner { State = "unpaired" };
        (await Service(runner).UnpairAsync("Test device")).State.Should().Be(ApplePairingState.Failed);
        (await Service(runner).UnpairAsync("old")).State.Should().Be(ApplePairingState.Failed);
        runner.UnpairCalls.Should().Be(0);
    }

    [Fact]
    public async Task Unpair_RechecksDetailsInsteadOfTrustingListState()
    {
        var runner = new FakeRunner { DetailsState = "unpaired" };
        (await Service(runner).UnpairAsync("old")).State.Should().Be(ApplePairingState.Failed);
        runner.UnpairCalls.Should().Be(0);
    }

    [Fact]
    public async Task Unpair_DoesNotClaimSuccessWhenAppleStillReportsPaired()
    {
        var runner = new FakeRunner { IgnoreUnpair = true };
        var result = await Service(runner).UnpairAsync("old");
        result.State.Should().Be(ApplePairingState.Failed);
        runner.UnpairCalls.Should().Be(1);
    }

    [Fact]
    public async Task Unpair_IsSerializedWithPairing()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new FakeRunner
        {
            OnWireless = async ct => { entered.SetResult(); await Task.Delay(Timeout.Infinite, ct); }
        };
        var service = Service(runner);
        using var cancel = new CancellationTokenSource();
        var wireless = service.PairWirelessAsync(new InlineProgress(), cancel.Token);
        await entered.Task;
        (await service.UnpairAsync("old")).Message.Should().Contain("Another Apple");
        runner.UnpairCalls.Should().Be(0);
        cancel.Cancel();
        await FluentActions.Awaiting(() => wireless).Should().ThrowAsync<OperationCanceledException>();
    }

    private static AppleDevicePairingService Service(FakeRunner runner)
    {
        var platform = new Mock<IPlatformService>();
        platform.SetupGet(p => p.IsMacOS).Returns(true);
        return new(platform.Object, Mock.Of<ILoggingService>(), runner);
    }

    private static JsonDocument Envelope(object result) =>
        JsonDocument.Parse(JsonSerializer.Serialize(new { info = new { outcome = "success" }, result }));

    private static JsonObject Device(string identifier, string pairing, string platform = "iOS", string transport = "wired") =>
        JsonSerializer.SerializeToNode(new
        {
            identifier,
            properties = new
            {
                hardware = new { udid = identifier + "-udid", osPlatform = platform, marketingName = "Test model", fidelity = "physical", deviceType = "iPhone" },
                state = new { name = "Test device", developerModeStatus = new { enabled = new { } } },
                software = new { osVersionNumber = "27.2" },
                connection = new { pairingState = pairing, state = "disconnected", tunnelState = "disconnected", transportType = transport }
            }
        })!.AsObject();

    private sealed class InlineProgress : IProgress<ApplePairingProgress>
    {
        public void Report(ApplePairingProgress value) { }
    }

    private sealed class FakeRunner : IAppleDevicePairingRunner
    {
        public bool IsWirelessAvailable => true;
        public string State { get; set; } = "paired";
        public string? DetailsState { get; init; }
        public string? DetailsIdentifier { get; init; }
        public string Transport { get; init; } = "wired";
        public bool MalformedProbe { get; init; }
        public bool FailProbe { get; init; }
        public bool FailDiscoveryAfterPair { get; init; }
        public bool FailDiscoveryAfterWireless { get; init; }
        public int NewDevices { get; init; } = 1;
        public int PairCalls { get; private set; }
        public int PairTimeout { get; private set; }
        public int UnpairCalls => UnpairTargets.Count;
        public int UnpairTimeout { get; private set; }
        public bool IgnoreUnpair { get; init; }
        public List<string> UnpairTargets { get; } = [];
        public int WirelessTimeout { get; private set; }
        public int ListCalls { get; private set; }
        public int ActiveWirelessSessions { get; private set; }
        public List<string> PairTargets { get; } = [];
        public List<string> LiveTargets { get; } = [];
        public Func<CancellationToken, Task>? OnWireless { get; init; }
        private bool wirelessCompleted;

        public Task<JsonDocument> RunDeviceCtlAsync(string[] arguments, int timeoutSeconds, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (arguments[0] == "list")
            {
                if (PairCalls > 0 && FailDiscoveryAfterPair)
                    throw new AppleDevicePairingException("Discovery failed after native success.");
                if (wirelessCompleted && FailDiscoveryAfterWireless)
                    throw new AppleDevicePairingException("Discovery failed after native success.");
                ListCalls++;
                var devices = new List<JsonObject> { Device("old", State, transport: Transport) };
                if (wirelessCompleted)
                    devices.AddRange(Enumerable.Range(0, NewDevices).Select(i => Device($"new-{i}", "paired", transport: "localNetwork")));
                return Task.FromResult(Envelope(new { devices }));
            }
            var identifier = arguments[Array.IndexOf(arguments, "--device") + 1];
            if (arguments[0] == "manage")
            {
                if (arguments[1] == "unpair")
                {
                    UnpairTargets.Add(identifier);
                    UnpairTimeout = timeoutSeconds;
                    if (!IgnoreUnpair) State = "unpaired";
                    return Task.FromResult(Envelope(new { }));
                }
                arguments[1].Should().Be("pair");
                PairCalls++;
                PairTimeout = timeoutSeconds;
                PairTargets.Add(identifier);
                State = "paired";
                return Task.FromResult(Envelope(new { }));
            }
            if (arguments[2] == "details")
                return Task.FromResult(Envelope(Device(DetailsIdentifier ?? identifier, DetailsState ?? State, transport: Transport)));
            arguments[2].Should().Be("lockState");
            LiveTargets.Add(identifier);
            if (FailProbe)
                throw new AppleDevicePairingException("Live connection unavailable.");
            return Task.FromResult(Envelope(MalformedProbe ? new { } :
                (object)new { passcodeRequired = false, unlockedSinceBoot = true }));
        }

        public async Task RunWirelessAsync(IProgress<ApplePairingProgress> progress, int timeoutSeconds, CancellationToken ct)
        {
            ActiveWirelessSessions++;
            WirelessTimeout = timeoutSeconds;
            try
            {
                if (OnWireless != null) await OnWireless(ct);
                progress.Report(new() { Stage = ApplePairingStage.Verifying });
                wirelessCompleted = true;
            }
            finally { ActiveWirelessSessions--; }
        }
    }
}
