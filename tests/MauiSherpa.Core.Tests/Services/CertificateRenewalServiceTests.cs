using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using Moq;

namespace MauiSherpa.Core.Tests.Services;

public class CertificateRenewalServiceTests : IDisposable
{
    static readonly DateTime Now = new(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);

    readonly Mock<IAppleConnectService> _apple = new();
    readonly Mock<ILoggingService> _logger = new();
    readonly CertificateRenewalService _sut;
    readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"sherpa-renewal-{Guid.NewGuid():N}");

    public CertificateRenewalServiceTests()
    {
        _sut = new CertificateRenewalService(_apple.Object, _logger.Object);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }

    // ------------------------------------------------------------------
    // Finding affected profiles and replacement certificates
    // ------------------------------------------------------------------

    [Fact]
    public void FindProfilesUsingCertificate_ReturnsOnlyProfilesThatIncludeIt()
    {
        var profiles = new[]
        {
            Profile("p1", "Beta", ["old", "other"]),
            Profile("p2", "Alpha", ["old"]),
            Profile("p3", "Unrelated", ["other"]),
            Profile("p4", "No certificates", null),
        };

        var result = _sut.FindProfilesUsingCertificate(profiles, "old");

        result.Select(profile => profile.Id).Should().Equal("p2", "p1");
    }

    [Fact]
    public void FindReplacementCandidates_ReturnsNewerValidCertificatesOfTheSameTypeNewestFirst()
    {
        var old = Certificate("old", "DISTRIBUTION", Now.AddDays(10));
        var certificates = new[]
        {
            old,
            Certificate("newer", "DISTRIBUTION", Now.AddDays(300)),
            Certificate("newest", "DISTRIBUTION", Now.AddDays(365)),
            Certificate("other-type", "DEVELOPMENT", Now.AddDays(365)),
            Certificate("expired", "DISTRIBUTION", Now.AddDays(-1)),
            Certificate("older", "DISTRIBUTION", Now.AddDays(5)),
            Certificate("local", "DISTRIBUTION", Now.AddDays(400)) with { IsLocalOnly = true },
        };

        var result = _sut.FindReplacementCandidates(certificates, old, Now);

        result.Select(certificate => certificate.Id).Should().Equal("newest", "newer");
    }

    [Fact]
    public void FindReplacementCandidates_AllowsReplacingAnExpiredCertificate()
    {
        var old = Certificate("old", "DEVELOPMENT", Now.AddDays(-30));
        var replacement = Certificate("new", "DEVELOPMENT", Now.AddDays(365));

        _sut.FindReplacementCandidates([old, replacement], old, Now)
            .Should().ContainSingle().Which.Id.Should().Be("new");
    }

    // ------------------------------------------------------------------
    // Building the recreate request
    // ------------------------------------------------------------------

    [Fact]
    public void BuildReplacementRequest_SwapsTheCertificateAndKeepsEverythingElse()
    {
        var details = Details(
            Profile("p1", "App Dev", ["keep-1", "old", "keep-2"], type: "IOS_APP_DEVELOPMENT"),
            bundleIdResourceId: "bundle-resource",
            devices: [Device("d1"), Device("d2")]);

        var plan = CertificateRenewalService.BuildReplacementRequest(details, "old", "new");

        plan.Request.Name.Should().Be("App Dev");
        plan.Request.ProfileType.Should().Be("IOS_APP_DEVELOPMENT");
        plan.Request.BundleIdResourceId.Should().Be("bundle-resource");
        plan.Request.CertificateIds.Should().Equal("keep-1", "new", "keep-2");
        plan.Request.DeviceIds.Should().Equal("d1", "d2");
        plan.DroppedDevices.Should().BeEmpty();
    }

    [Fact]
    public void BuildReplacementRequest_DoesNotDuplicateANewCertificateAlreadyOnTheProfile()
    {
        var details = Details(Profile("p1", "App", ["old", "new"]));

        var plan = CertificateRenewalService.BuildReplacementRequest(details, "old", "new");

        plan.Request.CertificateIds.Should().Equal("new");
    }

    [Fact]
    public void BuildReplacementRequest_SendsNoDevicesForProfilesWithoutDevices()
    {
        var details = Details(Profile("p1", "Store", ["old"], type: "IOS_APP_STORE"));

        var plan = CertificateRenewalService.BuildReplacementRequest(details, "old", "new");

        plan.Request.DeviceIds.Should().BeNull();
    }

    [Fact]
    public void BuildReplacementRequest_LeavesOutDisabledDevicesAndReportsThem()
    {
        var disabled = Device("d2", status: "DISABLED");
        var details = Details(Profile("p1", "Dev", ["old"]), devices: [Device("d1"), disabled]);

        var plan = CertificateRenewalService.BuildReplacementRequest(details, "old", "new");

        plan.Request.DeviceIds.Should().Equal("d1");
        plan.DroppedDevices.Should().ContainSingle().Which.Should().Be(disabled);
    }

    [Fact]
    public void BuildReplacementRequest_RefusesWhenEveryDeviceIsDisabled()
    {
        var details = Details(Profile("p1", "Dev", ["old"]), devices: [Device("d1", status: "DISABLED")]);

        var act = () => CertificateRenewalService.BuildReplacementRequest(details, "old", "new");

        act.Should().Throw<InvalidOperationException>().WithMessage("*disabled*");
    }

    [Fact]
    public void BuildReplacementRequest_RefusesWhenTheProfileNoLongerUsesTheOldCertificate()
    {
        var details = Details(Profile("p1", "Dev", ["something-else"]));

        var act = () => CertificateRenewalService.BuildReplacementRequest(details, "old", "new");

        act.Should().Throw<InvalidOperationException>().WithMessage("*no longer uses*");
    }

    // ------------------------------------------------------------------
    // Re-pointing a profile end to end
    // ------------------------------------------------------------------

    [Fact]
    public async Task RepointProfileAsync_RegeneratesInstallsAndRemovesTheOldInstalledCopy()
    {
        var original = Profile("p1", "App Dev", ["old", "other"], uuid: "OLD-UUID", bundleId: "com.example.app");
        var details = Details(original, "bundle-resource", [Device("d1")]);
        var regenerated = Profile("p2", "App Dev", null, uuid: "NEW-UUID", bundleId: "");
        Directory.CreateDirectory(_tempDirectory);
        var oldCopy = Path.Combine(_tempDirectory, "OLD-UUID.mobileprovision");
        await File.WriteAllTextAsync(oldCopy, "old");

        AppleProfileCreateRequest? sentRequest = null;
        _apple.Setup(service => service.GetProfileDetailsAsync("p1")).ReturnsAsync(details);
        _apple.Setup(service => service.RegenerateProfileAsync("p1", It.IsAny<AppleProfileCreateRequest>()))
            .Callback<string, AppleProfileCreateRequest>((_, request) => sentRequest = request)
            .ReturnsAsync(regenerated);
        _apple.Setup(service => service.InstallProfileAsync("p2")).ReturnsAsync("/profiles/NEW-UUID.mobileprovision");
        _apple.Setup(service => service.FindInstalledProfilePathsAsync("OLD-UUID")).ReturnsAsync([oldCopy]);

        var result = await _sut.RepointProfileAsync(original, "old", "new", removeOldInstalledCopies: true);

        sentRequest.Should().NotBeNull();
        sentRequest!.CertificateIds.Should().Equal("new", "other");
        sentRequest.DeviceIds.Should().Equal("d1");
        sentRequest.BundleIdResourceId.Should().Be("bundle-resource");
        result.RegeneratedProfile.Id.Should().Be("p2");
        result.RegeneratedProfile.BundleId.Should().Be("com.example.app");
        result.RegeneratedProfile.CertificateIds.Should().Equal("new", "other");
        result.Installed.Should().BeTrue();
        result.InstalledPath.Should().Be("/profiles/NEW-UUID.mobileprovision");
        result.RemovedInstalledPaths.Should().Equal(oldCopy);
        File.Exists(oldCopy).Should().BeFalse();
    }

    [Fact]
    public async Task RepointProfileAsync_KeepsOldInstalledCopiesWhenNotAsked()
    {
        var original = Profile("p1", "App", ["old"], uuid: "OLD-UUID");
        _apple.Setup(service => service.GetProfileDetailsAsync("p1")).ReturnsAsync(Details(original));
        _apple.Setup(service => service.RegenerateProfileAsync("p1", It.IsAny<AppleProfileCreateRequest>()))
            .ReturnsAsync(Profile("p2", "App", null, uuid: "NEW-UUID"));
        _apple.Setup(service => service.InstallProfileAsync("p2")).ReturnsAsync("/profiles/NEW-UUID.mobileprovision");

        var result = await _sut.RepointProfileAsync(original, "old", "new", removeOldInstalledCopies: false);

        result.RemovedInstalledPaths.Should().BeEmpty();
        _apple.Verify(service => service.FindInstalledProfilePathsAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RepointProfileAsync_ReportsInstallFailureWithoutRemovingTheOldCopy()
    {
        var original = Profile("p1", "App", ["old"], uuid: "OLD-UUID");
        _apple.Setup(service => service.GetProfileDetailsAsync("p1")).ReturnsAsync(Details(original));
        _apple.Setup(service => service.RegenerateProfileAsync("p1", It.IsAny<AppleProfileCreateRequest>()))
            .ReturnsAsync(Profile("p2", "App", null, uuid: "NEW-UUID"));
        _apple.Setup(service => service.InstallProfileAsync("p2")).ThrowsAsync(new IOException("disk full"));

        var result = await _sut.RepointProfileAsync(original, "old", "new", removeOldInstalledCopies: true);

        result.Installed.Should().BeFalse();
        result.InstallError.Should().Be("disk full");
        result.RegeneratedProfile.Id.Should().Be("p2");
        _apple.Verify(service => service.FindInstalledProfilePathsAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RepointProfileAsync_DoesNotTouchAppleWhenTheRequestCannotBeBuilt()
    {
        var original = Profile("p1", "App", ["old"]);
        _apple.Setup(service => service.GetProfileDetailsAsync("p1"))
            .ReturnsAsync(Details(original with { CertificateIds = ["someone-else"] }));

        var act = () => _sut.RepointProfileAsync(original, "old", "new", removeOldInstalledCopies: true);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _apple.Verify(service => service.RegenerateProfileAsync(It.IsAny<string>(), It.IsAny<AppleProfileCreateRequest>()), Times.Never);
        _apple.Verify(service => service.DeleteProfileAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RepointProfileAsync_PropagatesRegenerationFailuresWithoutInstalling()
    {
        var original = Profile("p1", "App", ["old"]);
        _apple.Setup(service => service.GetProfileDetailsAsync("p1")).ReturnsAsync(Details(original));
        _apple.Setup(service => service.RegenerateProfileAsync("p1", It.IsAny<AppleProfileCreateRequest>()))
            .ThrowsAsync(new InvalidOperationException("Apple rejected the request"));

        var act = () => _sut.RepointProfileAsync(original, "old", "new", removeOldInstalledCopies: true);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Apple rejected the request");
        _apple.Verify(service => service.InstallProfileAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RepointProfileAsync_HonoursCancellationBeforeRegenerating()
    {
        var original = Profile("p1", "App", ["old"]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => _sut.RepointProfileAsync(original, "old", "new", true, cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _apple.Verify(service => service.RegenerateProfileAsync(It.IsAny<string>(), It.IsAny<AppleProfileCreateRequest>()), Times.Never);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    static AppleProfile Profile(
        string id,
        string name,
        IReadOnlyList<string>? certificateIds,
        string type = "IOS_APP_DEVELOPMENT",
        string uuid = "UUID",
        string? bundleId = "com.example.app") =>
        new(id, name, type, "IOS", "ACTIVE", Now.AddDays(200), bundleId, uuid, certificateIds);

    static AppleCertificate Certificate(string id, string type, DateTime expires) =>
        new(id, $"Cert {id}", type, "IOS", expires, $"SERIAL-{id}");

    static AppleDevice Device(string id, string status = "ENABLED") =>
        new(id, $"UDID-{id}", $"Device {id}", "IOS", "IPHONE", status, null);

    static AppleProfileDetails Details(
        AppleProfile profile,
        string bundleIdResourceId = "bundle-resource",
        IReadOnlyList<AppleDevice>? devices = null) =>
        new(profile, bundleIdResourceId, profile.CertificateIds ?? [], devices ?? []);
}
