using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using Moq;

namespace MauiSherpa.Core.Tests.Services;

public class ProfileDeviceServiceTests : IDisposable
{
    static readonly DateTime Now = new(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);

    readonly Mock<IAppleConnectService> _apple = new();
    readonly Mock<ILoggingService> _logger = new();
    readonly ProfileDeviceService _sut;
    readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"sherpa-profile-device-{Guid.NewGuid():N}");

    public ProfileDeviceServiceTests()
    {
        _sut = new ProfileDeviceService(_apple.Object, _logger.Object);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }

    // ------------------------------------------------------------------
    // Finding profiles a device can join
    // ------------------------------------------------------------------

    [Fact]
    public void FindProfilesForDevice_ReturnsActiveIosDevelopmentAndAdHocProfilesForAnIPhone()
    {
        var profiles = new[]
        {
            Profile("p1", "Beta Ad Hoc", type: "IOS_APP_ADHOC"),
            Profile("p2", "Alpha Dev", type: "IOS_APP_DEVELOPMENT"),
            Profile("p3", "Store", type: "IOS_APP_STORE"),
            Profile("p4", "Mac Dev", type: "MAC_APP_DEVELOPMENT"),
            Profile("p5", "TV Dev", type: "TVOS_APP_DEVELOPMENT"),
            Profile("p6", "Expired Dev", type: "IOS_APP_DEVELOPMENT", state: "INVALID"),
        };

        var result = _sut.FindProfilesForDevice(profiles, Device("d1"));

        result.Select(profile => profile.Id).Should().Equal("p2", "p1");
    }

    [Fact]
    public void FindProfilesForDevice_ReturnsMacAndCatalystDevelopmentProfilesForAMac()
    {
        var profiles = new[]
        {
            Profile("p1", "iOS Dev", type: "IOS_APP_DEVELOPMENT"),
            Profile("p2", "Mac Dev", type: "MAC_APP_DEVELOPMENT"),
            Profile("p3", "Catalyst Dev", type: "MAC_CATALYST_APP_DEVELOPMENT"),
            Profile("p4", "Developer ID", type: "MAC_APP_DIRECT"),
        };

        var result = _sut.FindProfilesForDevice(profiles, Device("d1", platform: "MAC_OS", deviceClass: "MAC"));

        result.Select(profile => profile.Id).Should().Equal("p3", "p2");
    }

    [Fact]
    public void FindProfilesForDevice_ReturnsTvosProfilesForAnAppleTv()
    {
        var profiles = new[]
        {
            Profile("p1", "iOS Dev", type: "IOS_APP_DEVELOPMENT"),
            Profile("p2", "TV Dev", type: "TVOS_APP_DEVELOPMENT"),
            Profile("p3", "TV Ad Hoc", type: "TVOS_APP_ADHOC"),
        };

        var result = _sut.FindProfilesForDevice(profiles, Device("d1", deviceClass: "APPLE_TV"));

        result.Select(profile => profile.Id).Should().Equal("p3", "p2");
    }

    // ------------------------------------------------------------------
    // Building the recreate request
    // ------------------------------------------------------------------

    [Fact]
    public void BuildAddDeviceRequest_AddsTheDeviceAndKeepsEverythingElse()
    {
        var profile = Profile("p1", "App Dev", certificateIds: ["c1", "c2"]);
        var details = Details(profile, "bundle-resource", [Device("d1"), Device("d2")]);

        var plan = ProfileDeviceService.BuildAddDeviceRequest(details, Device("new"));

        plan.Should().NotBeNull();
        plan!.Request.Name.Should().Be("App Dev");
        plan.Request.ProfileType.Should().Be("IOS_APP_DEVELOPMENT");
        plan.Request.BundleIdResourceId.Should().Be("bundle-resource");
        plan.Request.CertificateIds.Should().Equal("c1", "c2");
        plan.Request.DeviceIds.Should().Equal("d1", "d2", "new");
        plan.DroppedDevices.Should().BeEmpty();
    }

    [Fact]
    public void BuildAddDeviceRequest_ReturnsNullWhenTheProfileAlreadyIncludesTheDevice()
    {
        var details = Details(Profile("p1", "App Dev"), devices: [Device("d1"), Device("new")]);

        ProfileDeviceService.BuildAddDeviceRequest(details, Device("new")).Should().BeNull();
    }

    [Fact]
    public void BuildAddDeviceRequest_LeavesOutDisabledDevicesAndReportsThem()
    {
        var details = Details(Profile("p1", "App Dev"), devices: [Device("d1"), Device("off", "DISABLED")]);

        var plan = ProfileDeviceService.BuildAddDeviceRequest(details, Device("new"));

        plan!.Request.DeviceIds.Should().Equal("d1", "new");
        plan.DroppedDevices.Select(device => device.Id).Should().Equal("off");
    }

    [Fact]
    public void BuildAddDeviceRequest_WorksForAProfileWhoseDevicesAreAllDisabled()
    {
        var details = Details(Profile("p1", "App Dev"), devices: [Device("off", "DISABLED")]);

        var plan = ProfileDeviceService.BuildAddDeviceRequest(details, Device("new"));

        plan!.Request.DeviceIds.Should().Equal("new");
    }

    [Fact]
    public void BuildAddDeviceRequest_RefusesADisabledDevice()
    {
        var details = Details(Profile("p1", "App Dev"), devices: [Device("d1")]);

        var act = () => ProfileDeviceService.BuildAddDeviceRequest(details, Device("new", "DISABLED"));

        act.Should().Throw<InvalidOperationException>();
    }

    // ------------------------------------------------------------------
    // Adding a device end to end
    // ------------------------------------------------------------------

    [Fact]
    public async Task AddDeviceToProfileAsync_RegeneratesInstallsAndRemovesTheOldInstalledCopy()
    {
        var original = Profile("p1", "App Dev", ["c1"], uuid: "OLD-UUID", bundleId: "com.example.app");
        var regenerated = Profile("p2", "App Dev", null, uuid: "NEW-UUID", bundleId: "");
        Directory.CreateDirectory(_tempDirectory);
        var oldCopy = Path.Combine(_tempDirectory, "OLD-UUID.mobileprovision");
        await File.WriteAllTextAsync(oldCopy, "old");

        AppleProfileCreateRequest? sentRequest = null;
        _apple.Setup(service => service.GetProfileDetailsAsync("p1"))
            .ReturnsAsync(Details(original, "bundle-resource", [Device("d1")]));
        _apple.Setup(service => service.RegenerateProfileAsync("p1", It.IsAny<AppleProfileCreateRequest>()))
            .Callback<string, AppleProfileCreateRequest>((_, request) => sentRequest = request)
            .ReturnsAsync(regenerated);
        _apple.Setup(service => service.InstallProfileAsync("p2")).ReturnsAsync("/profiles/NEW-UUID.mobileprovision");
        _apple.Setup(service => service.FindInstalledProfilePathsAsync("OLD-UUID")).ReturnsAsync([oldCopy]);

        var result = await _sut.AddDeviceToProfileAsync(original, Device("new"), removeOldInstalledCopies: true);

        sentRequest!.DeviceIds.Should().Equal("d1", "new");
        sentRequest.CertificateIds.Should().Equal("c1");
        result.AlreadyIncluded.Should().BeFalse();
        result.RegeneratedProfile!.Id.Should().Be("p2");
        result.RegeneratedProfile.BundleId.Should().Be("com.example.app");
        result.RegeneratedProfile.CertificateIds.Should().Equal("c1");
        result.Installed.Should().BeTrue();
        result.RemovedInstalledPaths.Should().Equal(oldCopy);
        File.Exists(oldCopy).Should().BeFalse();
    }

    [Fact]
    public async Task AddDeviceToProfileAsync_LeavesAProfileThatAlreadyIncludesTheDeviceAlone()
    {
        var original = Profile("p1", "App Dev");
        _apple.Setup(service => service.GetProfileDetailsAsync("p1"))
            .ReturnsAsync(Details(original, devices: [Device("new")]));

        var result = await _sut.AddDeviceToProfileAsync(original, Device("new"), removeOldInstalledCopies: true);

        result.AlreadyIncluded.Should().BeTrue();
        _apple.Verify(service => service.RegenerateProfileAsync(It.IsAny<string>(), It.IsAny<AppleProfileCreateRequest>()), Times.Never);
        _apple.Verify(service => service.InstallProfileAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AddDeviceToProfileAsync_ReportsInstallFailureWithoutRemovingTheOldCopy()
    {
        var original = Profile("p1", "App Dev", uuid: "OLD-UUID");
        _apple.Setup(service => service.GetProfileDetailsAsync("p1")).ReturnsAsync(Details(original, devices: [Device("d1")]));
        _apple.Setup(service => service.RegenerateProfileAsync("p1", It.IsAny<AppleProfileCreateRequest>()))
            .ReturnsAsync(Profile("p2", "App Dev", uuid: "NEW-UUID"));
        _apple.Setup(service => service.InstallProfileAsync("p2")).ThrowsAsync(new IOException("disk full"));

        var result = await _sut.AddDeviceToProfileAsync(original, Device("new"), removeOldInstalledCopies: true);

        result.Installed.Should().BeFalse();
        result.InstallError.Should().Be("disk full");
        _apple.Verify(service => service.FindInstalledProfilePathsAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AddDeviceToProfileAsync_HonoursCancellationBeforeRegenerating()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => _sut.AddDeviceToProfileAsync(Profile("p1", "App Dev"), Device("new"), true, cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _apple.Verify(service => service.RegenerateProfileAsync(It.IsAny<string>(), It.IsAny<AppleProfileCreateRequest>()), Times.Never);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    static AppleProfile Profile(
        string id,
        string name,
        IReadOnlyList<string>? certificateIds = null,
        string type = "IOS_APP_DEVELOPMENT",
        string uuid = "UUID",
        string? bundleId = "com.example.app",
        string state = "ACTIVE") =>
        new(id, name, type, "IOS", state, Now.AddDays(200), bundleId, uuid, certificateIds ?? ["c1"]);

    static AppleDevice Device(string id, string status = "ENABLED", string platform = "IOS", string deviceClass = "IPHONE") =>
        new(id, $"UDID-{id}", $"Device {id}", platform, deviceClass, status, null);

    static AppleProfileDetails Details(
        AppleProfile profile,
        string bundleIdResourceId = "bundle-resource",
        IReadOnlyList<AppleDevice>? devices = null) =>
        new(profile, bundleIdResourceId, profile.CertificateIds ?? [], devices ?? []);
}
