using FluentAssertions;

namespace MauiSherpa.Bundles.Tests.Signing;

public sealed class BundleSigningSessionTests
{
    [Fact]
    public async Task PrepareAsync_NonApplePlatform_DoesNothing()
    {
        using var workspace = new SigningTestWorkspace();
        var certPath = workspace.CreateFile("cert.p12", "cert-bytes");
        var bundle = SigningTestData.CreateBundle(new()
        {
            ["cert"] = SigningTestData.CertificateAsset()
        });
        var configuration = SigningTestData.CreateConfiguration("cert");
        var runner = new RecordingBundleProcessRunner((_) =>
            throw new InvalidOperationException("Android should not run any signing processes."));

        await using var session = await BundleSigningSession.PrepareAsync(
            bundle,
            configuration,
            BundlePlatform.Android,
            workspace.RootPath,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["certPath"] = certPath,
                ["CertPassword"] = "s3cret"
            },
            new HashSet<string>(StringComparer.Ordinal),
            dryRun: false,
            runner,
            workspace.CreateHost(),
            progress: null,
            CancellationToken.None);

        runner.Requests.Should().BeEmpty();
        session.CodesignKeychain.Should().BeNull();
        session.Variables.Should().BeEmpty();
    }

    [Fact]
    public async Task PrepareAsync_HappyPath_ImportsCertificateAndInstallsProfileUsingArgumentLists()
    {
        using var workspace = new SigningTestWorkspace();
        var certPath = workspace.CreateFile("cert.p12", "cert-bytes");
        var profilePath = workspace.CreateFile("profile.mobileprovision", "profile-bytes");
        var bundle = SigningTestData.CreateBundle(new()
        {
            ["cert"] = SigningTestData.CertificateAsset(),
            ["profile"] = SigningTestData.ProfileAsset()
        });
        var configuration = SigningTestData.CreateConfiguration("cert", "profile");
        var runner = new RecordingBundleProcessRunner(request =>
        {
            if (request.Arguments is ["create-keychain", ..])
                File.WriteAllText(request.Arguments[^1], "keychain-bytes");
            return new BundleProcessResult(0, string.Empty, string.Empty);
        });

        await using var session = await BundleSigningSession.PrepareAsync(
            bundle,
            configuration,
            BundlePlatform.MacCatalyst,
            workspace.RootPath,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["certPath"] = certPath,
                ["CertPassword"] = "s3cret",
                ["profilePath"] = profilePath
            },
            new HashSet<string>(StringComparer.Ordinal),
            dryRun: false,
            runner,
            workspace.CreateHost(),
            progress: null,
            CancellationToken.None);

        runner.Requests.Should().HaveCount(5);
        runner.Requests.Should().OnlyContain(r => r.FileName == "security");
        runner.Requests[0].Arguments.Should().StartWith(["create-keychain", "-p"]);
        runner.Requests[1].Arguments.Should().StartWith(["set-keychain-settings"]);
        runner.Requests[2].Arguments.Should().StartWith(["unlock-keychain", "-p"]);
        runner.Requests[3].Arguments.Should().Equal(
            "import", certPath, "-k", session.CodesignKeychain, "-P", "s3cret", "-T", "/usr/bin/codesign", "-T", "/usr/bin/security");
        runner.Requests[4].Arguments.Should().StartWith(["set-key-partition-list"]);

        // Every request must carry the p12 password and the random keychain password as secrets so
        // the process runner's redactor scrubs both from any logged output.
        runner.Requests.Should().OnlyContain(r => r.SecretValues.Contains("s3cret"));
        var keychainPassword = runner.Requests[0].Arguments[2];
        runner.Requests.Should().OnlyContain(r => r.SecretValues.Contains(keychainPassword));

        session.CodesignKeychain.Should().NotBeNullOrEmpty();
        session.Variables.Should().Contain("CodesignKeychain", session.CodesignKeychain!);

        var installedProfile = Directory.GetFiles(workspace.ProfilesDirectory).Should().ContainSingle().Subject;
        Path.GetExtension(installedProfile).Should().Be(".mobileprovision");
        File.ReadAllText(installedProfile).Should().Be("profile-bytes");
    }

    [Fact]
    public async Task PrepareAsync_DryRun_ValidatesWithoutRunningProcessesOrWritingFiles()
    {
        using var workspace = new SigningTestWorkspace();
        var certPath = workspace.CreateFile("cert.p12", "cert-bytes");
        var profilePath = workspace.CreateFile("profile.mobileprovision", "profile-bytes");
        var bundle = SigningTestData.CreateBundle(new()
        {
            ["cert"] = SigningTestData.CertificateAsset(),
            ["profile"] = SigningTestData.ProfileAsset()
        });
        var configuration = SigningTestData.CreateConfiguration("cert", "profile");
        var runner = new RecordingBundleProcessRunner((_) =>
            throw new InvalidOperationException("Dry run must not execute any process."));

        await using var session = await BundleSigningSession.PrepareAsync(
            bundle,
            configuration,
            BundlePlatform.Ios,
            workspace.RootPath,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["certPath"] = certPath,
                ["CertPassword"] = "s3cret",
                ["profilePath"] = profilePath
            },
            new HashSet<string>(StringComparer.Ordinal),
            dryRun: true,
            runner,
            // A host that would fail if IsMacOS() were ever consulted during a dry run.
            new BundleSigningHost
            {
                IsMacOS = () => throw new InvalidOperationException("Dry run must not probe the host OS."),
                GetProvisioningProfilesDirectory = () => workspace.ProfilesDirectory
            },
            progress: null,
            CancellationToken.None);

        runner.Requests.Should().BeEmpty();
        session.CodesignKeychain.Should().BeNull();
        session.Variables.Should().BeEmpty();
        session.Diagnostics.Should().Contain(d => d.Contains("Would import certificate"));
        session.Diagnostics.Should().Contain(d => d.Contains("Would install provisioning profile"));
        Directory.GetFiles(workspace.ProfilesDirectory).Should().BeEmpty();
    }

    [Fact]
    public async Task PrepareAsync_MissingPasswordVariable_ThrowsValidationExceptionWithoutRunningProcesses()
    {
        using var workspace = new SigningTestWorkspace();
        var certPath = workspace.CreateFile("cert.p12", "cert-bytes");
        var bundle = SigningTestData.CreateBundle(new()
        {
            ["cert"] = SigningTestData.CertificateAsset(passwordVariable: null)
        });
        var configuration = SigningTestData.CreateConfiguration("cert");
        var runner = new RecordingBundleProcessRunner((_) =>
            throw new InvalidOperationException("Validation failures must not run any process."));

        var act = () => BundleSigningSession.PrepareAsync(
            bundle,
            configuration,
            BundlePlatform.MacCatalyst,
            workspace.RootPath,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["certPath"] = certPath },
            new HashSet<string>(StringComparer.Ordinal),
            dryRun: false,
            runner,
            workspace.CreateHost(),
            progress: null,
            CancellationToken.None);

        (await act.Should().ThrowAsync<BundleValidationException>())
            .Which.Errors.Should().Contain(e => e.Contains("PasswordVariable"));
        runner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task PrepareAsync_PasswordVariableDoesNotResolve_ThrowsValidationException()
    {
        using var workspace = new SigningTestWorkspace();
        var certPath = workspace.CreateFile("cert.p12", "cert-bytes");
        var bundle = SigningTestData.CreateBundle(new()
        {
            ["cert"] = SigningTestData.CertificateAsset()
        });
        var configuration = SigningTestData.CreateConfiguration("cert");
        var runner = new RecordingBundleProcessRunner((_) =>
            throw new InvalidOperationException("Validation failures must not run any process."));

        var act = () => BundleSigningSession.PrepareAsync(
            bundle,
            configuration,
            BundlePlatform.MacCatalyst,
            workspace.RootPath,
            // CertPassword is intentionally absent from the resolved variable set.
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["certPath"] = certPath },
            new HashSet<string>(StringComparer.Ordinal),
            dryRun: false,
            runner,
            workspace.CreateHost(),
            progress: null,
            CancellationToken.None);

        (await act.Should().ThrowAsync<BundleValidationException>())
            .Which.Errors.Should().Contain(e => e.Contains("CertPassword"));
        runner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task DisposeAsync_DeletesKeychainAndCreatedProfilesButLeavesPreexistingFilesAlone()
    {
        using var workspace = new SigningTestWorkspace();
        var certPath = workspace.CreateFile("cert.p12", "cert-bytes");
        var profilePath = workspace.CreateFile("profile.mobileprovision", "profile-bytes");
        var preexisting = Path.Combine(workspace.ProfilesDirectory, "keep-me.mobileprovision");
        File.WriteAllText(preexisting, "do-not-delete");
        var bundle = SigningTestData.CreateBundle(new()
        {
            ["cert"] = SigningTestData.CertificateAsset(),
            ["profile"] = SigningTestData.ProfileAsset()
        });
        var configuration = SigningTestData.CreateConfiguration("cert", "profile");
        var runner = new RecordingBundleProcessRunner(request =>
        {
            if (request.Arguments is ["create-keychain", ..])
                File.WriteAllText(request.Arguments[^1], "keychain-bytes");
            return new BundleProcessResult(0, string.Empty, string.Empty);
        });

        var session = await BundleSigningSession.PrepareAsync(
            bundle,
            configuration,
            BundlePlatform.MacOS,
            workspace.RootPath,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["certPath"] = certPath,
                ["CertPassword"] = "s3cret",
                ["profilePath"] = profilePath
            },
            new HashSet<string>(StringComparer.Ordinal),
            dryRun: false,
            runner,
            workspace.CreateHost(),
            progress: null,
            CancellationToken.None);

        var keychainPath = session.CodesignKeychain!;
        var installedProfile = Directory.GetFiles(workspace.ProfilesDirectory)
            .Single(path => !string.Equals(path, preexisting, StringComparison.Ordinal));
        File.Exists(keychainPath).Should().BeTrue();

        await session.DisposeAsync();

        File.Exists(keychainPath).Should().BeFalse();
        File.Exists(installedProfile).Should().BeFalse();
        File.Exists(preexisting).Should().BeTrue("cleanup must only remove files this session created");

        // Disposal must be idempotent and safe to call more than once (mirrors `await using`).
        await session.DisposeAsync();
    }

    [Fact]
    public async Task PrepareAsync_ProcessFailure_ThrowsAndBestEffortCleansUpPartialState()
    {
        using var workspace = new SigningTestWorkspace();
        var certPath = workspace.CreateFile("cert.p12", "cert-bytes");
        var bundle = SigningTestData.CreateBundle(new()
        {
            ["cert"] = SigningTestData.CertificateAsset()
        });
        var configuration = SigningTestData.CreateConfiguration("cert");
        string? keychainPath = null;
        var runner = new RecordingBundleProcessRunner(request =>
        {
            if (request.Arguments is ["create-keychain", ..])
            {
                keychainPath = request.Arguments[^1];
                File.WriteAllText(keychainPath, "keychain-bytes");
                return new BundleProcessResult(0, string.Empty, string.Empty);
            }
            if (request.Arguments is ["import", ..])
                return new BundleProcessResult(1, string.Empty, "security: SecKeychainItemImport: MAC verification failed.");
            return new BundleProcessResult(0, string.Empty, string.Empty);
        });

        var act = () => BundleSigningSession.PrepareAsync(
            bundle,
            configuration,
            BundlePlatform.MacCatalyst,
            workspace.RootPath,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["certPath"] = certPath,
                ["CertPassword"] = "s3cret"
            },
            new HashSet<string>(StringComparer.Ordinal),
            dryRun: false,
            runner,
            workspace.CreateHost(),
            progress: null,
            CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("import certificate asset 'cert'");

        keychainPath.Should().NotBeNull();
        File.Exists(keychainPath!).Should().BeFalse("a failed import must trigger best-effort cleanup of the partially created keychain");
    }

    [Fact]
    public async Task PrepareAsync_NonMacOSHost_ThrowsPlatformNotSupportedExceptionWithoutRunningProcesses()
    {
        using var workspace = new SigningTestWorkspace();
        var certPath = workspace.CreateFile("cert.p12", "cert-bytes");
        var bundle = SigningTestData.CreateBundle(new()
        {
            ["cert"] = SigningTestData.CertificateAsset()
        });
        var configuration = SigningTestData.CreateConfiguration("cert");
        var runner = new RecordingBundleProcessRunner((_) =>
            throw new InvalidOperationException("Must not run any process when the host is not macOS."));

        var act = () => BundleSigningSession.PrepareAsync(
            bundle,
            configuration,
            BundlePlatform.MacCatalyst,
            workspace.RootPath,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["certPath"] = certPath,
                ["CertPassword"] = "s3cret"
            },
            new HashSet<string>(StringComparer.Ordinal),
            dryRun: false,
            runner,
            workspace.CreateHost(isMacOS: false),
            progress: null,
            CancellationToken.None);

        await act.Should().ThrowAsync<PlatformNotSupportedException>();
        runner.Requests.Should().BeEmpty();
    }
}
