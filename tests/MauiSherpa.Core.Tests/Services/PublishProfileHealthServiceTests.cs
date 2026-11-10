using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests.Android;
using MauiSherpa.Core.Requests.Apple;
using MauiSherpa.Core.Services;
using Moq;
using Shiny.Mediator;

namespace MauiSherpa.Core.Tests.Services;

public class PublishProfileHealthServiceTests
{
    static readonly DateTime Now = new(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);
    const string IdentityId = "apple-1";

    readonly Mock<IMediator> _mediator = new();
    readonly Mock<IAppleIdentityStateService> _identityState = new();
    readonly Mock<IAppleIdentityService> _appleIdentities = new();
    readonly Mock<IGoogleIdentityService> _googleIdentities = new();
    readonly Mock<IManagedSecretsService> _managedSecrets = new();
    readonly Mock<ICloudSecretsService> _cloudSecrets = new();
    readonly Mock<IPublishProfileService> _publishProfiles = new();
    readonly Mock<ILoggingService> _logger = new();
    DateTime _clock = Now;
    readonly PublishProfileHealthService _sut;

    public PublishProfileHealthServiceTests()
    {
        _sut = new PublishProfileHealthService(
            _mediator.Object,
            _identityState.Object,
            _appleIdentities.Object,
            _googleIdentities.Object,
            _managedSecrets.Object,
            _cloudSecrets.Object,
            _publishProfiles.Object,
            _logger.Object,
            () => _clock);
    }

    // ------------------------------------------------------------------
    // Healthy
    // ------------------------------------------------------------------

    [Fact]
    public void Evaluate_ReportsNothingForAHealthyProfile()
    {
        var profile = Profile(
            apple: [AppleConfig(certificates: [Selection(DistCert)], profiles: [ProfileSelection(StoreProfile)])],
            android: [new PublishProfileAndroidConfig("Android", "ks-1", new())],
            secrets: [new PublishProfileSecretMapping("API_KEY", ["API_KEY"])]) with
        {
            AppleIdentities = [new PublishProfileAppleIdentity("ASC", IdentityId, new())],
            GoogleIdentities = [new PublishProfileGoogleIdentity("Play", "google-1", new())],
        };

        var report = _sut.Evaluate(profile, Snapshot());

        report.IsHealthy.Should().BeTrue();
        report.Issues.Should().BeEmpty();
        report.UncheckedSources.Should().BeEmpty();
        report.Summary.Should().BeEmpty();
        report.Severity.Should().BeNull();
    }

    // ------------------------------------------------------------------
    // Certificates
    // ------------------------------------------------------------------

    [Fact]
    public void Evaluate_ReportsMissingCertificate()
    {
        var profile = Profile(apple: [AppleConfig(certificates: [new PublishProfileCertificateSelection("DEADBEEF", "Gone Cert")])]);

        var report = _sut.Evaluate(profile, Snapshot());

        var issue = report.Issues.Should().ContainSingle().Subject;
        issue.Kind.Should().Be(PublishProfileHealthIssueKind.MissingCertificate);
        issue.Label.Should().Contain("Gone Cert");
        issue.ConfigKind.Should().Be(PublishProfileConfigKind.Apple);
        issue.ConfigIndex.Should().Be(0);
        issue.ConfigLabel.Should().Be("iOS");
        issue.CanFix.Should().BeFalse();
        issue.Severity.Should().Be(PublishProfileHealthSeverity.Error);
    }

    [Fact]
    public void Evaluate_OffersASameNameCertificateForAMissingOne()
    {
        var renewed = Certificate("c9", "Apple Distribution: Team", "DISTRIBUTION", Now.AddDays(365), "NEWSERIAL");
        var profile = Profile(apple: [AppleConfig(certificates: [new PublishProfileCertificateSelection("REVOKED", "Apple Distribution: Team")])]);

        var report = _sut.Evaluate(profile, Snapshot(certificates: [renewed]));

        var issue = report.Issues.Should().ContainSingle().Subject;
        issue.Kind.Should().Be(PublishProfileHealthIssueKind.MissingCertificate);
        issue.CanFix.Should().BeTrue();
        issue.ReplacementReference.Should().Be("NEWSERIAL");
    }

    [Fact]
    public void Evaluate_MatchesSerialsIgnoringFormatting()
    {
        var profile = Profile(apple: [AppleConfig(certificates: [Selection(DistCert with { SerialNumber = "00ab:cd:12" })])]);
        var snapshot = Snapshot(certificates: [DistCert with { SerialNumber = "ABCD12" }]);

        _sut.Evaluate(profile, snapshot).IsHealthy.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_ReportsMissingInstallerCertificate()
    {
        var profile = Profile(apple: [AppleConfig() with { InstallerCertSerialNumber = "INSTALLER" }]);

        var issue = _sut.Evaluate(profile, Snapshot()).Issues.Should().ContainSingle().Subject;

        issue.Kind.Should().Be(PublishProfileHealthIssueKind.MissingCertificate);
        issue.Label.Should().StartWith("Installer certificate");
    }

    [Fact]
    public void Evaluate_ReportsExpiredCertificate()
    {
        var expired = Certificate("c2", "Old Dist", "DISTRIBUTION", Now.AddDays(-1), "EXPIRED1");
        var profile = Profile(apple: [AppleConfig(certificates: [Selection(expired)])]);

        var issue = _sut.Evaluate(profile, Snapshot(certificates: [expired])).Issues.Should().ContainSingle().Subject;

        issue.Kind.Should().Be(PublishProfileHealthIssueKind.ExpiredCertificate);
        issue.CanFix.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_OffersTheNewerCertificateForAnExpiredOne()
    {
        var expired = Certificate("c2", "Old Dist", "DISTRIBUTION", Now.AddDays(-1), "EXPIRED1");
        var profile = Profile(apple: [AppleConfig(certificates: [Selection(expired)])]);

        var issue = _sut.Evaluate(profile, Snapshot(certificates: [expired, DistCert])).Issues.Should().ContainSingle().Subject;

        issue.Kind.Should().Be(PublishProfileHealthIssueKind.ExpiredCertificate);
        issue.ReplacementReference.Should().Be(DistCert.SerialNumber);
        issue.ReplacementName.Should().Be(DistCert.Name);
    }

    [Fact]
    public void Evaluate_ReportsCertificateReplacedWhenANewerOneOfTheSameTypeExists()
    {
        var older = Certificate("c3", "Dist 2025", "DISTRIBUTION", Now.AddDays(20), "OLDER1");
        var profile = Profile(apple: [AppleConfig(certificates: [Selection(older)])]);
        var otherType = Certificate("c4", "Dev", "DEVELOPMENT", Now.AddDays(900), "DEV1");

        var issue = _sut.Evaluate(profile, Snapshot(certificates: [older, DistCert, otherType]))
            .Issues.Should().ContainSingle().Subject;

        issue.Kind.Should().Be(PublishProfileHealthIssueKind.CertificateReplaced);
        issue.ReplacementReference.Should().Be(DistCert.SerialNumber);
        issue.Severity.Should().Be(PublishProfileHealthSeverity.Warning);
    }

    [Fact]
    public void Evaluate_DoesNotFlagAnOlderCertificateWhenTheNewerOneIsAlsoSelected()
    {
        var older = Certificate("c3", "Dist 2025", "DISTRIBUTION", Now.AddDays(20), "OLDER1");
        var profile = Profile(apple: [AppleConfig(certificates: [Selection(older), Selection(DistCert)])]);

        _sut.Evaluate(profile, Snapshot(certificates: [older, DistCert])).IsHealthy.Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // Provisioning profiles
    // ------------------------------------------------------------------

    [Fact]
    public void Evaluate_ReportsMissingProvisioningProfile()
    {
        var profile = Profile(apple: [AppleConfig(profiles: [new PublishProfileProvisioningProfileSelection("gone", "U-GONE", "Gone Profile")])]);

        var issue = _sut.Evaluate(profile, Snapshot()).Issues.Should().ContainSingle().Subject;

        issue.Kind.Should().Be(PublishProfileHealthIssueKind.MissingProvisioningProfile);
        issue.Label.Should().Contain("Gone Profile");
        issue.CanFix.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_ReportsProvisioningProfileUpdatedWhenItWasRecreatedWithTheSameName()
    {
        // What Feature 1 leaves behind: the old ID is gone, a new profile has the same name.
        var regenerated = StoreProfile with { Id = "p-new", Uuid = "U-NEW" };
        var profile = Profile(apple: [AppleConfig(profiles: [ProfileSelection(StoreProfile)])]);

        var issue = _sut.Evaluate(profile, Snapshot(profiles: [regenerated])).Issues.Should().ContainSingle().Subject;

        issue.Kind.Should().Be(PublishProfileHealthIssueKind.ProvisioningProfileUpdated);
        issue.Reference.Should().Be(StoreProfile.Id);
        issue.ReplacementReference.Should().Be("p-new");
        issue.ReplacementUuid.Should().Be("U-NEW");
        issue.CanFix.Should().BeTrue();
        issue.Severity.Should().Be(PublishProfileHealthSeverity.Warning);
    }

    [Fact]
    public void Evaluate_ReportsProvisioningProfileUpdatedWhenTheUuidChanged()
    {
        var profile = Profile(apple: [AppleConfig(profiles: [ProfileSelection(StoreProfile) with { Uuid = "U-OLD" }])]);

        var issue = _sut.Evaluate(profile, Snapshot()).Issues.Should().ContainSingle().Subject;

        issue.Kind.Should().Be(PublishProfileHealthIssueKind.ProvisioningProfileUpdated);
        issue.ReplacementReference.Should().Be(StoreProfile.Id);
        issue.ReplacementUuid.Should().Be(StoreProfile.Uuid);
    }

    [Fact]
    public void Evaluate_ReportsExpiredProvisioningProfile()
    {
        var expired = StoreProfile with { ExpirationDate = Now.AddDays(-2) };
        var profile = Profile(apple: [AppleConfig(profiles: [ProfileSelection(expired)])]);

        var issue = _sut.Evaluate(profile, Snapshot(profiles: [expired])).Issues.Should().ContainSingle().Subject;

        issue.Kind.Should().Be(PublishProfileHealthIssueKind.ExpiredProvisioningProfile);
    }

    [Fact]
    public void Evaluate_TreatsInvalidProfilesAsExpired()
    {
        var invalid = StoreProfile with { State = "INVALID" };
        var profile = Profile(apple: [AppleConfig(profiles: [ProfileSelection(invalid)])]);

        _sut.Evaluate(profile, Snapshot(profiles: [invalid])).Issues.Should().ContainSingle()
            .Which.Kind.Should().Be(PublishProfileHealthIssueKind.ExpiredProvisioningProfile);
    }

    [Fact]
    public void Evaluate_ChecksLegacyProfileIdFields()
    {
        var legacy = AppleConfig() with { ProfileId = "legacy-gone", ProfileUuid = "U" };

        _sut.Evaluate(Profile(apple: [legacy]), Snapshot()).Issues.Should().ContainSingle()
            .Which.Kind.Should().Be(PublishProfileHealthIssueKind.MissingProvisioningProfile);
    }

    // ------------------------------------------------------------------
    // Keystores, secrets, identities
    // ------------------------------------------------------------------

    [Fact]
    public void Evaluate_ReportsMissingKeystore()
    {
        var profile = Profile(android: [new PublishProfileAndroidConfig("Play Store", "ks-gone", new())]);

        var issue = _sut.Evaluate(profile, Snapshot()).Issues.Should().ContainSingle().Subject;

        issue.Kind.Should().Be(PublishProfileHealthIssueKind.MissingKeystore);
        issue.ConfigKind.Should().Be(PublishProfileConfigKind.Android);
        issue.ConfigLabel.Should().Be("Play Store");
        issue.Reference.Should().Be("ks-gone");
    }

    [Fact]
    public void Evaluate_ReportsEachMissingSecret()
    {
        var profile = Profile(secrets:
        [
            new PublishProfileSecretMapping("API_KEY", ["API_KEY"]),
            new PublishProfileSecretMapping("GONE_1", ["A"]),
            new PublishProfileSecretMapping("GONE_2", ["B"]),
        ]);

        var report = _sut.Evaluate(profile, Snapshot());

        report.Issues.Should().HaveCount(2).And.OnlyContain(issue => issue.Kind == PublishProfileHealthIssueKind.MissingSecret);
        report.Issues.Select(issue => issue.Reference).Should().Equal("GONE_1", "GONE_2");
        report.Summary.Should().Be("2 missing secrets");
    }

    [Fact]
    public void Evaluate_ReportsMissingNotarizationSecretsButNotManualValues()
    {
        var apple = AppleConfig() with
        {
            IncludeNotarization = true,
            NotarizationAppleIdSecretKey = "NOTARY_ID",
            NotarizationPasswordSecretKey = "NOTARY_PASSWORD",
            NotarizationPasswordManualValue = "typed-in",
            NotarizationTeamIdSecretKey = "API_KEY",
        };

        var issue = _sut.Evaluate(Profile(apple: [apple]), Snapshot()).Issues.Should().ContainSingle().Subject;

        issue.Kind.Should().Be(PublishProfileHealthIssueKind.MissingSecret);
        issue.Reference.Should().Be("NOTARY_ID");
        issue.ConfigKind.Should().Be(PublishProfileConfigKind.Apple);
    }

    [Fact]
    public void Evaluate_ReportsMissingAppleAndGoogleIdentities()
    {
        var profile = Profile() with
        {
            AppleIdentities = [new PublishProfileAppleIdentity("ASC", "apple-gone", new())],
            GoogleIdentities = [new PublishProfileGoogleIdentity("Play", "google-gone", new())],
        };

        var report = _sut.Evaluate(profile, Snapshot());

        report.Issues.Should().HaveCount(2).And.OnlyContain(issue => issue.Kind == PublishProfileHealthIssueKind.MissingIdentity);
        report.Issues.Select(issue => issue.ConfigKind).Should()
            .Equal(PublishProfileConfigKind.AppleIdentity, PublishProfileConfigKind.GoogleIdentity);
        report.Summary.Should().Be("2 missing identities");
    }

    [Fact]
    public void Evaluate_ReportsMissingAppleConfigIdentityAndSkipsItsCertificates()
    {
        var apple = AppleConfig(certificates: [new PublishProfileCertificateSelection("X", "X Cert")]) with { IdentityId = "apple-gone" };

        var issue = _sut.Evaluate(Profile(apple: [apple]), Snapshot()).Issues.Should().ContainSingle().Subject;

        issue.Kind.Should().Be(PublishProfileHealthIssueKind.MissingIdentity);
        issue.ConfigKind.Should().Be(PublishProfileConfigKind.Apple);
    }

    // ------------------------------------------------------------------
    // Sources that can't be checked
    // ------------------------------------------------------------------

    [Fact]
    public void Evaluate_ReportsUnavailableSourcesInsteadOfIssues()
    {
        var profile = Profile(
            apple: [AppleConfig(certificates: [Selection(DistCert)], profiles: [ProfileSelection(StoreProfile)])],
            android: [new PublishProfileAndroidConfig("Android", "ks-1", new())],
            secrets: [new PublishProfileSecretMapping("API_KEY", ["API_KEY"])]);
        var snapshot = Snapshot() with
        {
            Certificates = null,
            ProvisioningProfiles = null,
            Keystores = null,
            ManagedSecretKeys = null,
            UnavailableSources = new Dictionary<string, string>
            {
                [PublishProfileHealthService.CertificatesSource] = "Apple certificates couldn't be checked: offline",
            },
        };

        var report = _sut.Evaluate(profile, snapshot);

        report.IsHealthy.Should().BeTrue();
        report.UncheckedSources.Should().Contain("Apple certificates couldn't be checked: offline");
        report.UncheckedSources.Should().HaveCount(4);
    }

    [Fact]
    public void Evaluate_DoesNotCheckCertificatesOfAnotherAppleIdentity()
    {
        var apple = AppleConfig(certificates: [new PublishProfileCertificateSelection("X", "X Cert")]) with { IdentityId = "apple-2" };
        var snapshot = Snapshot() with { AppleIdentityIds = new HashSet<string> { IdentityId, "apple-2" } };

        var report = _sut.Evaluate(Profile(apple: [apple]), snapshot);

        report.IsHealthy.Should().BeTrue();
        report.UncheckedSources.Should().ContainSingle().Which.Should().Contain("another Apple identity");
    }

    [Fact]
    public void Evaluate_TreatsAnEmptySecretListAsUnchecked()
    {
        var profile = Profile(secrets: [new PublishProfileSecretMapping("API_KEY", ["API_KEY"])]);

        var report = _sut.Evaluate(profile, Snapshot() with { ManagedSecretKeys = new HashSet<string>() });

        report.IsHealthy.Should().BeTrue();
        report.UncheckedSources.Should().ContainSingle();
    }

    [Fact]
    public async Task LoadSnapshotAsync_RecordsAFailingSourceAndKeepsTheRest()
    {
        SetUpSources();
        _mediator
            .Setup(mediator => mediator.Request(It.IsAny<GetCertificatesRequest>(), It.IsAny<CancellationToken>(), It.IsAny<Action<IMediatorContext>?>()))
            .ThrowsAsync(new HttpRequestException("offline"));

        var snapshot = await _sut.LoadSnapshotAsync();

        snapshot.Certificates.Should().BeNull();
        snapshot.UnavailableSources.Should().ContainKey(PublishProfileHealthService.CertificatesSource)
            .WhoseValue.Should().Contain("offline");
        snapshot.ProvisioningProfiles.Should().ContainSingle();
        snapshot.Keystores.Should().ContainSingle();
        snapshot.ManagedSecretKeys.Should().Contain("API_KEY");
        snapshot.AppleIdentityIds.Should().Contain(IdentityId);
        snapshot.GoogleIdentityIds.Should().Contain("google-1");
    }

    [Fact]
    public async Task LoadSnapshotAsync_SkipsAppleDataWithoutASelectedIdentity()
    {
        SetUpSources();
        _identityState.SetupGet(state => state.SelectedIdentity).Returns((AppleIdentity?)null);

        var snapshot = await _sut.LoadSnapshotAsync();

        snapshot.Certificates.Should().BeNull();
        snapshot.ProvisioningProfiles.Should().BeNull();
        snapshot.UnavailableSources.Should().ContainKey(PublishProfileHealthService.AppleSource);
        _mediator.Verify(mediator => mediator.Request(It.IsAny<GetCertificatesRequest>(), It.IsAny<CancellationToken>(), It.IsAny<Action<IMediatorContext>?>()), Times.Never);
    }

    [Fact]
    public async Task LoadSnapshotAsync_SkipsSecretsWithoutAProvider()
    {
        SetUpSources();
        _cloudSecrets.SetupGet(service => service.ActiveProvider).Returns((CloudSecretsProviderConfig?)null);

        var snapshot = await _sut.LoadSnapshotAsync();

        snapshot.ManagedSecretKeys.Should().BeNull();
        snapshot.UnavailableSources.Should().ContainKey(PublishProfileHealthService.SecretsSource);
        _managedSecrets.Verify(service => service.ListAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EvaluateAsync_ReusesTheSnapshotUntilInvalidated()
    {
        SetUpSources();
        var profiles = new[] { Profile(secrets: [new PublishProfileSecretMapping("API_KEY", ["API_KEY"])]) };

        await _sut.EvaluateAsync(profiles);
        await _sut.EvaluateAsync(profiles);
        _managedSecrets.Verify(service => service.ListAsync(It.IsAny<CancellationToken>()), Times.Once);

        _sut.Invalidate();
        var reports = await _sut.EvaluateAsync(profiles);

        _managedSecrets.Verify(service => service.ListAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        reports.Should().ContainKey("pp-1").WhoseValue.IsHealthy.Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // Summary
    // ------------------------------------------------------------------

    [Fact]
    public void Summary_GroupsIssuesByKindInAStableOrder()
    {
        var regenerated = StoreProfile with { Id = "p-new", Uuid = "U-NEW" };
        var profile = Profile(
            apple: [AppleConfig(profiles: [ProfileSelection(StoreProfile)])],
            secrets: [new PublishProfileSecretMapping("GONE_1", ["A"]), new PublishProfileSecretMapping("GONE_2", ["B"])]);

        var report = _sut.Evaluate(profile, Snapshot(profiles: [regenerated]));

        report.Summary.Should().Be("2 missing secrets · Provisioning profile updated");
        report.Severity.Should().Be(PublishProfileHealthSeverity.Error);
        report.HasFixes.Should().BeTrue();
    }

    [Theory]
    [InlineData(PublishProfileHealthIssueKind.MissingSecret, 1, "1 missing secret")]
    [InlineData(PublishProfileHealthIssueKind.MissingCertificate, 1, "Missing certificate")]
    [InlineData(PublishProfileHealthIssueKind.ExpiredCertificate, 2, "2 expired certificates")]
    [InlineData(PublishProfileHealthIssueKind.CertificateReplaced, 1, "Newer certificate available")]
    [InlineData(PublishProfileHealthIssueKind.MissingProvisioningProfile, 3, "3 missing provisioning profiles")]
    [InlineData(PublishProfileHealthIssueKind.ExpiredProvisioningProfile, 1, "Expired provisioning profile")]
    [InlineData(PublishProfileHealthIssueKind.ProvisioningProfileUpdated, 2, "2 provisioning profiles updated")]
    [InlineData(PublishProfileHealthIssueKind.MissingKeystore, 1, "Missing keystore")]
    [InlineData(PublishProfileHealthIssueKind.MissingIdentity, 1, "Missing identity")]
    public void Describe_UsesReadableLabels(PublishProfileHealthIssueKind kind, int count, string expected)
    {
        PublishProfileHealthText.Describe(kind, count).Should().Be(expected);
    }

    // ------------------------------------------------------------------
    // Fixes
    // ------------------------------------------------------------------

    [Fact]
    public void ApplyFixes_RepointsCertificatesAndProfiles()
    {
        var older = Certificate("c3", "Dist 2025", "DISTRIBUTION", Now.AddDays(20), "OLDER1");
        var regenerated = StoreProfile with { Id = "p-new", Uuid = "U-NEW" };
        var apple = AppleConfig(certificates: [Selection(older)], profiles: [ProfileSelection(StoreProfile)]) with
        {
            CertificateSerialNumber = older.SerialNumber,
            ProfileId = StoreProfile.Id,
            ProfileUuid = StoreProfile.Uuid,
        };
        var profile = Profile(apple: [apple]);
        var report = _sut.Evaluate(profile, Snapshot(certificates: [older, DistCert], profiles: [regenerated]));
        _clock = Now.AddHours(1);

        var fixedProfile = _sut.ApplyFixes(profile, report.Issues);

        fixedProfile.Should().NotBeSameAs(profile);
        fixedProfile.UpdatedAt.Should().Be(Now.AddHours(1));
        var config = fixedProfile.AppleConfigs.Single();
        config.SigningCertificates.Should().ContainSingle().Which.Should().Be(
            new PublishProfileCertificateSelection(DistCert.SerialNumber, DistCert.Name));
        config.CertificateSerialNumber.Should().Be(DistCert.SerialNumber);
        config.ProvisioningProfiles.Should().ContainSingle().Which.Should().Be(
            new PublishProfileProvisioningProfileSelection("p-new", "U-NEW", StoreProfile.Name));
        config.ProfileId.Should().Be("p-new");
        config.ProfileUuid.Should().Be("U-NEW");
        config.Label.Should().Be(apple.Label);
        config.KeyMappings.Should().BeSameAs(apple.KeyMappings);

        // The original is untouched.
        profile.AppleConfigs.Single().SigningCertificates.Single().SerialNumber.Should().Be(older.SerialNumber);

        _sut.Evaluate(fixedProfile, Snapshot(certificates: [older, DistCert], profiles: [regenerated]))
            .IsHealthy.Should().BeTrue();
    }

    [Fact]
    public void ApplyFixes_ReturnsTheSameProfileWhenNothingIsFixable()
    {
        var profile = Profile(secrets: [new PublishProfileSecretMapping("GONE", ["A"])]);
        var report = _sut.Evaluate(profile, Snapshot());

        _sut.ApplyFixes(profile, report.Issues).Should().BeSameAs(profile);
    }

    [Fact]
    public void ApplyFixes_RepointsTheInstallerCertificate()
    {
        var oldInstaller = Certificate("i1", "Installer", "DEVELOPER_ID_INSTALLER", Now.AddDays(-5), "INST-OLD") with { IsLocalOnly = true };
        var newInstaller = Certificate("i2", "Installer", "DEVELOPER_ID_INSTALLER", Now.AddDays(800), "INST-NEW") with { IsLocalOnly = true };
        var profile = Profile(apple: [AppleConfig() with { InstallerCertSerialNumber = oldInstaller.SerialNumber }]);
        var report = _sut.Evaluate(profile, Snapshot(certificates: [oldInstaller, newInstaller]));

        var fixedProfile = _sut.ApplyFixes(profile, report.Issues);

        fixedProfile.AppleConfigs.Single().InstallerCertSerialNumber.Should().Be("INST-NEW");
    }

    [Fact]
    public async Task ApplyFixesAsync_SavesOnlyWhenSomethingChanged()
    {
        var regenerated = StoreProfile with { Id = "p-new", Uuid = "U-NEW" };
        var profile = Profile(apple: [AppleConfig(profiles: [ProfileSelection(StoreProfile)])]);
        var report = _sut.Evaluate(profile, Snapshot(profiles: [regenerated]));

        var saved = await _sut.ApplyFixesAsync(profile, report.Issues);
        var unchanged = await _sut.ApplyFixesAsync(profile, []);

        saved.Should().NotBeNull();
        unchanged.Should().BeNull();
        _publishProfiles.Verify(service => service.SaveProfileAsync(saved!), Times.Once);
        _publishProfiles.Verify(service => service.SaveProfileAsync(It.IsAny<PublishProfile>()), Times.Once);
    }

    [Fact]
    public void Repoint_ReplacesRenewedCertificateAndRegeneratedProfilesAcrossConfigs()
    {
        var oldCert = Certificate("c-old", "Dist", "DISTRIBUTION", Now.AddDays(5), "AA:BB");
        var regenerated = StoreProfile with { Id = "p-new", Uuid = "U-NEW" };
        var profile = Profile(apple:
        [
            AppleConfig(certificates: [Selection(oldCert)], profiles: [ProfileSelection(StoreProfile)]),
            AppleConfig(certificates: [new PublishProfileCertificateSelection("UNRELATED", "Other")]) with { Label = "macOS" },
        ]);

        var result = _sut.Repoint(
            profile,
            new Dictionary<string, AppleCertificate> { ["AABB"] = DistCert },
            new Dictionary<string, AppleProfile> { [StoreProfile.Id] = regenerated });

        result.AppleConfigs[0].SigningCertificates.Single().SerialNumber.Should().Be(DistCert.SerialNumber);
        result.AppleConfigs[0].ProvisioningProfiles.Single().Id.Should().Be("p-new");
        result.AppleConfigs[1].Should().BeSameAs(profile.AppleConfigs[1]);
    }

    [Fact]
    public void Repoint_ReturnsTheSameProfileWhenNothingMatches()
    {
        var profile = Profile(apple: [AppleConfig(certificates: [Selection(DistCert)])]);

        var result = _sut.Repoint(
            profile,
            new Dictionary<string, AppleCertificate> { ["SOMETHINGELSE"] = DistCert },
            new Dictionary<string, AppleProfile>());

        result.Should().BeSameAs(profile);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    static readonly AppleCertificate DistCert =
        Certificate("c1", "Apple Distribution: Team", "DISTRIBUTION", Now.AddDays(300), "D15751");

    static readonly AppleProfile StoreProfile = new(
        "p1", "App Store Profile", "IOS_APP_STORE", "IOS", "ACTIVE", Now.AddDays(300), "com.example.app", "U-1", ["c1"]);

    static AppleCertificate Certificate(string id, string name, string type, DateTime expires, string serial) =>
        new(id, name, type, "IOS", expires, serial);

    static PublishProfileCertificateSelection Selection(AppleCertificate certificate) =>
        new(certificate.SerialNumber, certificate.Name);

    static PublishProfileProvisioningProfileSelection ProfileSelection(AppleProfile profile) =>
        new(profile.Id, profile.Uuid, profile.Name);

    static PublishProfileAppleConfig AppleConfig(
        IEnumerable<PublishProfileCertificateSelection>? certificates = null,
        IEnumerable<PublishProfileProvisioningProfileSelection>? profiles = null) =>
        new("iOS", IdentityId, ApplePlatformType.iOS, AppleDistributionType.AppStore,
            null, null, null, null, false, null, null, null, null, null, null,
            new Dictionary<string, List<string>>())
        {
            SigningCertificates = certificates?.ToList() ?? new(),
            ProvisioningProfiles = profiles?.ToList() ?? new(),
        };

    static PublishProfile Profile(
        IEnumerable<PublishProfileAppleConfig>? apple = null,
        IEnumerable<PublishProfileAndroidConfig>? android = null,
        IEnumerable<PublishProfileSecretMapping>? secrets = null) =>
        new("pp-1", "Release", null, null, null, null,
            apple?.ToList() ?? new(),
            android?.ToList() ?? new(),
            secrets?.ToList() ?? new(),
            Now.AddDays(-10),
            Now.AddDays(-10));

    static PublishProfileHealthSnapshot Snapshot(
        IReadOnlyList<AppleCertificate>? certificates = null,
        IReadOnlyList<AppleProfile>? profiles = null) => new(
            IdentityId,
            "Team",
            certificates ?? [DistCert],
            profiles ?? [StoreProfile],
            [new AndroidKeystore("ks-1", "upload", "/tmp/upload.keystore", "PKCS12", Now)],
            new HashSet<string> { "API_KEY" },
            new HashSet<string> { IdentityId },
            new HashSet<string> { "google-1" },
            new Dictionary<string, string>(),
            Now);

    void SetUpSources()
    {
        _identityState.SetupGet(state => state.SelectedIdentity)
            .Returns(new AppleIdentity(IdentityId, "Team", "KEY", "ISSUER", null, null));
        _cloudSecrets.SetupGet(service => service.ActiveProvider)
            .Returns(new CloudSecretsProviderConfig("local", "Local", CloudSecretsProviderType.Local, new()));

        _mediator
            .Setup(mediator => mediator.Request(It.IsAny<GetCertificatesRequest>(), It.IsAny<CancellationToken>(), It.IsAny<Action<IMediatorContext>?>()))
            .ReturnsAsync((Mock.Of<IMediatorContext>(), (IReadOnlyList<AppleCertificate>)[DistCert]));
        _mediator
            .Setup(mediator => mediator.Request(It.IsAny<GetProfilesRequest>(), It.IsAny<CancellationToken>(), It.IsAny<Action<IMediatorContext>?>()))
            .ReturnsAsync((Mock.Of<IMediatorContext>(), (IReadOnlyList<AppleProfile>)[StoreProfile]));
        _mediator
            .Setup(mediator => mediator.Request(It.IsAny<GetKeystoresRequest>(), It.IsAny<CancellationToken>(), It.IsAny<Action<IMediatorContext>?>()))
            .ReturnsAsync((Mock.Of<IMediatorContext>(), (IReadOnlyList<AndroidKeystore>)[new AndroidKeystore("ks-1", "upload", "/tmp/k", "PKCS12", Now)]));

        _managedSecrets.Setup(service => service.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ManagedSecret("API_KEY", ManagedSecretType.String, null, null, Now, Now)]);
        _appleIdentities.Setup(service => service.GetIdentitiesAsync())
            .ReturnsAsync([new AppleIdentity(IdentityId, "Team", "KEY", "ISSUER", null, null)]);
        _googleIdentities.Setup(service => service.GetIdentitiesAsync())
            .ReturnsAsync([new GoogleIdentity("google-1", "Play", "project", "svc@example.com", null, null)]);
    }
}
