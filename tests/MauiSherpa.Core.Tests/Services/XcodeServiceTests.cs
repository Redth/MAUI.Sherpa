using System.Net;
using System.Net.Http.Headers;
using System.Collections.Concurrent;
using System.Text;
using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using Moq;
using Moq.Protected;

namespace MauiSherpa.Core.Tests.Services;

public class XcodeServiceTests
{
    [Fact]
    public void FindBundledUnxipExecutable_WithRuntimeSpecificBundle_ReturnsBundledPath()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var bundledPath = CreateBundledExecutable(tempDir, "runtimes", "osx-arm64", "native", "unxip");

            var resolved = XcodeService.FindBundledUnxipExecutable(tempDir, "osx-arm64");

            resolved.Should().Be(bundledPath);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void FindBundledUnxipExecutable_WithMacCatalystRuntime_FallsBackToBundledOsxPath()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var bundledPath = CreateBundledExecutable(tempDir, "runtimes", "osx-arm64", "native", "unxip");

            var resolved = XcodeService.FindBundledUnxipExecutable(tempDir, "maccatalyst-arm64");

            resolved.Should().Be(bundledPath);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void CreateArchiveExtractionCommand_WithSystemPreference_UsesSystemXip()
    {
        var command = XcodeService.CreateArchiveExtractionCommand(
            "/tmp/Xcode_16.0.xip",
            XcodeArchiveExtractorOptions.SystemXip,
            null);

        command.FileName.Should().Be("xip");
        command.Arguments.Should().Equal("--expand", "/tmp/Xcode_16.0.xip");
        command.Extractor.Should().Be(XcodeArchiveExtractor.SystemXip);
        command.FellBackToSystemXip.Should().BeFalse();
    }

    [Fact]
    public void CreateArchiveExtractionCommand_WithUnxipPreferenceAndExecutable_UsesUnxip()
    {
        var command = XcodeService.CreateArchiveExtractionCommand(
            "/tmp/Xcode_16.0.xip",
            XcodeArchiveExtractorOptions.Unxip,
            "/opt/homebrew/bin/unxip");

        command.FileName.Should().Be("/opt/homebrew/bin/unxip");
        command.Arguments.Should().Equal("/tmp/Xcode_16.0.xip");
        command.Extractor.Should().Be(XcodeArchiveExtractor.Unxip);
        command.FellBackToSystemXip.Should().BeFalse();
    }

    [Fact]
    public void CreateArchiveExtractionCommand_WithUnxipPreferenceButNoExecutable_FallsBackToSystemXip()
    {
        var command = XcodeService.CreateArchiveExtractionCommand(
            "/tmp/Xcode_16.0.xip",
            XcodeArchiveExtractorOptions.Unxip,
            null);

        command.FileName.Should().Be("xip");
        command.Arguments.Should().Equal("--expand", "/tmp/Xcode_16.0.xip");
        command.Extractor.Should().Be(XcodeArchiveExtractor.SystemXip);
        command.FellBackToSystemXip.Should().BeTrue();
    }

    [Theory]
    [InlineData(XcodeArchiveExtractorOptions.SystemXip)]
    [InlineData(XcodeArchiveExtractorOptions.Unxip)]
    public void CreateArchiveExtractionCommand_WithAea_IgnoresXipPreference(string preference)
    {
        const string archivePath = "/archives/With spaces/Quoted \"Xcode\".AEA";
        var command = XcodeService.CreateArchiveExtractionCommand(archivePath, preference, "/usr/local/bin/unxip");

        command.FileName.Should().Be("/usr/bin/archiveutil");
        command.Arguments.Should().Equal("extractXcodeAEA", archivePath);
        command.Extractor.Should().Be(XcodeArchiveExtractor.ArchiveUtil);
        command.FellBackToSystemXip.Should().BeFalse();
    }

    [Fact]
    public void CreateArchiveExtractionCommand_WithUnknownFormat_RejectsArchive()
    {
        var act = () => XcodeService.CreateArchiveExtractionCommand("/archives/Xcode.zip", null, null);

        act.Should().Throw<NotSupportedException>();
    }

    [Theory]
    [InlineData("https://download.developer.apple.com/Developer_Tools/Xcode_27.3_beta_2/Xcode_27.3_beta_2.aea", "Xcode_27.3_beta_2.aea", ".aea")]
    [InlineData("https://developer.apple.com/services-account/download?path=/Developer_Tools/Xcode_27.3_beta_2/Xcode_27.3_beta_2.aea", "Xcode_27.3_beta_2.aea", ".aea")]
    [InlineData("https://developer.apple.com/services-account/download?other=1&path=%2FDeveloper_Tools%2FXcode%20Beta.AEA", "Xcode Beta.AEA", ".aea")]
    [InlineData("https://download.developer.apple.com/Developer_Tools/Xcode.xip?token=value", "Xcode.xip", ".xip")]
    [InlineData(null, null, null)]
    public void ArchiveMetadata_UsesUnderlyingDownloadPath(string? url, string? fileName, string? extension)
    {
        var release = CreateDownloadRelease(url);

        release.ArchiveFileName.Should().Be(fileName);
        release.ArchiveExtension.Should().Be(extension);
    }

    [Theory]
    [InlineData(true, 2, false, "Xcode 27.3 Beta 2")]
    [InlineData(true, null, false, "Xcode 27.3 Beta")]
    [InlineData(true, null, true, "Xcode 27.3 RC")]
    [InlineData(false, null, false, "Xcode 27.3")]
    public void ReleaseDisplayName_PreservesPrereleaseIdentity(bool isBeta, int? betaNumber, bool isRc, string expected)
    {
        var release = CreateDownloadRelease() with { IsBeta = isBeta, BetaNumber = betaNumber, IsReleaseCandidate = isRc };

        release.DisplayName.Should().Be(expected);
    }

    [Theory]
    [InlineData("27.2", false, null, false, false)]
    [InlineData("27.3", true, 1, false, false)]
    [InlineData("27.3.0", true, 1, false, false)]
    [InlineData("27.3", true, null, false, false)]
    [InlineData("27.3", true, 2, false, true)]
    [InlineData("27.3.0", true, 3, false, true)]
    [InlineData("27.3", true, null, true, true)]
    [InlineData("27.3", false, null, false, true)]
    [InlineData("27.3.1", true, 1, false, true)]
    [InlineData("27.4", true, 1, false, true)]
    [InlineData("28.0", true, 1, false, true)]
    public void GetAeaDownloadCandidate_RespectsBeta2Boundary(
        string version, bool isBeta, int? betaNumber, bool isRc, bool expected)
    {
        var release = CreateDownloadRelease() with
        {
            Version = version, IsBeta = isBeta, BetaNumber = betaNumber, IsReleaseCandidate = isRc
        };

        var candidate = XcodeService.GetAeaDownloadCandidate(release);

        if (expected)
            candidate.Should().Be("https://download.developer.apple.com/Developer_Tools/Xcode_27.3_beta_2/Xcode_27.3_beta_2.aea");
        else
            candidate.Should().BeNull();
    }

    [Theory]
    [InlineData("https://example.com/Developer_Tools/Xcode.xip")]
    [InlineData("http://download.developer.apple.com/Developer_Tools/Xcode.xip")]
    [InlineData("https://download.developer.apple.com/Developer_Tools/Xcode.aea")]
    [InlineData("https://download.developer.apple.com/Other/Xcode.xip")]
    public void GetAeaDownloadCandidate_DoesNotRewriteOtherDownloads(string url)
    {
        XcodeService.GetAeaDownloadCandidate(CreateDownloadRelease(url)).Should().BeNull();
    }

    [Theory]
    [InlineData(200, "application/octet-stream", "AEA1", true)]
    [InlineData(206, "application/octet-stream", "AEA1", true)]
    [InlineData(200, "application/vnd.apple.encrypted-archive", "AEA1", true)]
    [InlineData(200, "application/octet-stream", "not-an-archive", false)]
    [InlineData(200, "application/octet-stream", "AEA", false)]
    [InlineData(200, "text/html", "<html>Sign in</html>", false)]
    [InlineData(200, "application/json", "{\"resultCode\":1100}", false)]
    [InlineData(302, "application/octet-stream", "", false)]
    [InlineData(403, "application/octet-stream", "", false)]
    [InlineData(404, "application/octet-stream", "", false)]
    public async Task GetAvailableReleasesAsync_UsesAeaOnlyWhenAppleConfirmsAnArchive(
        int statusCode, string contentType, string body, bool expectAea)
    {
        var requests = new List<HttpMethod>();
        var authHandler = new Mock<HttpMessageHandler>();
        authHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((request, _) =>
            {
                requests.Add(request.Method);
                if (request.Method == HttpMethod.Get)
                {
                    request.Headers.Range!.Ranges.Should().ContainSingle()
                        .Which.Should().BeEquivalentTo(new RangeItemHeaderValue(0, 3));
                }
                var response = new HttpResponseMessage(request.Method == HttpMethod.Post ? HttpStatusCode.OK : (HttpStatusCode)statusCode);
                response.Content = new ByteArrayContent(Encoding.ASCII.GetBytes(body));
                response.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                if (statusCode == 206)
                    response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 3, 1000000);
                return Task.FromResult(response);
            });
        var auth = new Mock<IAppleDownloadAuthService>();
        auth.SetupGet(a => a.IsAuthenticated).Returns(true);
        auth.Setup(a => a.ValidateSessionAsync()).ReturnsAsync(true);
        auth.Setup(a => a.GetAllCookies()).Returns(new CookieCollection { new Cookie("ADCDownloadAuth", "test-cookie") });
        auth.Setup(a => a.CreateAuthenticatedHttpClient()).Returns(() => new HttpClient(authHandler.Object, disposeHandler: false));
        using var feedClient = CreateReleaseFeedClient();
        var service = CreateService(feedClient, auth.Object);

        var releases = await service.GetAvailableReleasesAsync();

        var release = releases.Should().ContainSingle().Subject;
        release.BetaNumber.Should().Be(2);
        release.ArchiveExtension.Should().Be(expectAea ? ".aea" : ".xip");
        requests.Should().Equal(HttpMethod.Get);
        if (statusCode == 206 && expectAea)
            release.FileSizeBytes.Should().Be(1000000);
    }

    [Fact]
    public async Task GetAvailableReleasesAsync_WithoutSignIn_DoesNotProbeApple()
    {
        var auth = new Mock<IAppleDownloadAuthService>(MockBehavior.Strict);
        auth.SetupGet(a => a.IsAuthenticated).Returns(false);
        using var client = CreateReleaseFeedClient();

        var releases = await CreateService(client, auth.Object).GetAvailableReleasesAsync();

        releases.Should().ContainSingle().Which.ArchiveExtension.Should().Be(".xip");
        auth.Verify(a => a.CreateAuthenticatedHttpClient(), Times.Never);
    }

    [Fact]
    public async Task GetAvailableReleasesAsync_WhenDownloadSessionIsNotValid_DoesNotProbeArchives()
    {
        var auth = new Mock<IAppleDownloadAuthService>(MockBehavior.Strict);
        auth.SetupGet(a => a.IsAuthenticated).Returns(true);
        auth.Setup(a => a.ValidateSessionAsync()).ReturnsAsync(false);
        using var client = CreateReleaseFeedClient();

        var releases = await CreateService(client, auth.Object).GetAvailableReleasesAsync();

        releases.Should().ContainSingle().Which.ArchiveExtension.Should().Be(".xip");
        auth.Verify(a => a.CreateAuthenticatedHttpClient(), Times.Never);
    }

    [Fact]
    public async Task GetAvailableReleasesAsync_WhenProbeFails_PreservesFeedRelease()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network unavailable"));
        var auth = new Mock<IAppleDownloadAuthService>();
        auth.SetupGet(a => a.IsAuthenticated).Returns(true);
        auth.Setup(a => a.ValidateSessionAsync()).ReturnsAsync(true);
        auth.Setup(a => a.GetAllCookies()).Returns(new CookieCollection { new Cookie("ADCDownloadAuth", "test-cookie") });
        auth.Setup(a => a.CreateAuthenticatedHttpClient()).Returns(() => new HttpClient(handler.Object, disposeHandler: false));
        using var client = CreateReleaseFeedClient();

        var releases = await CreateService(client, auth.Object).GetAvailableReleasesAsync();

        releases.Should().ContainSingle().Which.ArchiveExtension.Should().Be(".xip");
    }

    [Fact]
    public async Task RunProcessAsync_CancellationStopsExtraction()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var act = () => XcodeService.RunProcessAsync("/bin/sh", ["-c", "exec sleep 30"], ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RunProcessAsync_ReportsBothStreamsBeforeExtractionFinishes()
    {
        if (OperatingSystem.IsWindows())
            return;

        var lines = new ConcurrentQueue<string>();
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var progress = new Progress<string>(line =>
        {
            lines.Enqueue(line);
            if (lines.Count == 3)
                received.TrySetResult();
        });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = XcodeService.RunProcessAsync("/bin/sh",
            ["-c", "printf 'Extracting\\rVerifying\\n'; printf 'Checking archive\\n' >&2; exec sleep 30"],
            ct: cts.Token, progress: progress);
        try
        {
            await received.Task.WaitAsync(cts.Token);
            run.IsCompleted.Should().BeFalse();
            lines.Should().BeEquivalentTo(new[] { "Extracting", "Verifying", "Checking archive" });
        }
        finally
        {
            cts.Cancel();
            var act = async () => await run;
            await act.Should().ThrowAsync<OperationCanceledException>();
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public async Task InstallationScript_MovesBundleAndRunsFirstLaunchInOneInvocation(int firstLaunchExitCode)
    {
        if (OperatingSystem.IsWindows())
            return;

        var tempDir = CreateTempDirectory();
        try
        {
            var source = Path.Combine(tempDir, "Source \"Beta's\".app");
            var destination = Path.Combine(tempDir, "Installed \"Beta's\".app");
            var xcodebuild = Path.Combine(source, "Contents", "Developer", "usr", "bin", "xcodebuild");
            Directory.CreateDirectory(Path.GetDirectoryName(xcodebuild)!);
            await File.WriteAllTextAsync(xcodebuild,
                $"#!/bin/sh\nprintf '%s\\n' \"$1\"\nexit {firstLaunchExitCode}\n");
            File.SetUnixFileMode(xcodebuild, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var result = await XcodeService.RunProcessAsync("/bin/bash",
                ["-euo", "pipefail", "-c", XcodeService.CreateInstallationScript(source, destination)]);

            result.exitCode.Should().Be(0);
            Directory.Exists(source).Should().BeFalse();
            File.Exists(Path.Combine(destination, "Contents", "Developer", "usr", "bin", "xcodebuild")).Should().BeTrue();
            result.output.Should().Contain("-runFirstLaunch");
            if (firstLaunchExitCode != 0)
                result.output.Should().Contain("MAUI_SHERPA_XCODE_FIRST_LAUNCH_FAILED");
            else
                result.output.Should().NotContain("MAUI_SHERPA_XCODE_FIRST_LAUNCH_FAILED");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task InstallationScript_WhenMoveFails_PreservesSourceAndReturnsFailure()
    {
        if (OperatingSystem.IsWindows())
            return;

        var tempDir = CreateTempDirectory();
        try
        {
            var source = Path.Combine(tempDir, "Source.app");
            Directory.CreateDirectory(source);
            var destination = Path.Combine(tempDir, "missing-parent", "Xcode.app");

            var result = await XcodeService.RunProcessAsync("/bin/bash",
                ["-euo", "pipefail", "-c", XcodeService.CreateInstallationScript(source, destination)]);

            result.exitCode.Should().NotBe(0);
            Directory.Exists(source).Should().BeTrue();
            result.output.Should().NotContain("MAUI_SHERPA_XCODE_FIRST_LAUNCH_FAILED");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static XcodeRelease CreateDownloadRelease(
        string? url = "https://download.developer.apple.com/Developer_Tools/Xcode_27.3_beta_2/Xcode_27.3_beta_2.xip") =>
        new("27.3", "27C5035i", new DateTime(2026, 11, 16), true, null, url, null, null, [], [], 2);

    private static XcodeService CreateService(HttpClient client, IAppleDownloadAuthService auth) =>
        new(Mock.Of<ILoggingService>(), Mock.Of<IPlatformService>(), client, auth, Mock.Of<IEncryptedSettingsService>());

    private static HttpClient CreateReleaseFeedClient()
    {
        const string json = """
            [{
                "version": { "number": "27.3", "build": "27C5035i", "release": { "beta": 2 } },
                "date": { "year": 2026, "month": 11, "day": 16 },
                "links": { "download": { "url": "https://download.developer.apple.com/Developer_Tools/Xcode_27.3_beta_2/Xcode_27.3_beta_2.xip" } }
            }]
            """;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        return new HttpClient(handler.Object);
    }

    [Fact]
    public void GetManagedXcodeBundleName_Underscore_UsesVersionOnly()
    {
        var bundleName = XcodeService.GetManagedXcodeBundleName("26.3", XcodeBundleSeparatorOptions.Underscore);

        bundleName.Should().Be("Xcode_26.3.app");
    }

    [Fact]
    public void GetManagedXcodeBundleName_Hyphen_UsesVersionOnly()
    {
        var bundleName = XcodeService.GetManagedXcodeBundleName("26.3", XcodeBundleSeparatorOptions.Hyphen);

        bundleName.Should().Be("Xcode-26.3.app");
    }

    [Theory]
    [InlineData("_", "Xcode_26.3_Beta_2.app")]
    [InlineData("-", "Xcode-26.3-Beta-2.app")]
    public void GetManagedXcodeBundleName_SanitizesBetaVersions(string separator, string expected)
    {
        var bundleName = XcodeService.GetManagedXcodeBundleName("26.3 Beta 2", separator);

        bundleName.Should().Be(expected);
    }

    [Fact]
    public void GetManagedXcodeBundleNameWithBuild_AppendsBuildNumber()
    {
        var bundleName = XcodeService.GetManagedXcodeBundleNameWithBuild("26.3", "17A123", XcodeBundleSeparatorOptions.Underscore);

        bundleName.Should().Be("Xcode_26.3_17A123.app");
    }

    [Theory]
    [InlineData("_", "/Applications/Xcode_26.3.app")]
    [InlineData("-", "/Applications/Xcode-26.3.app")]
    public void ResolveManagedXcodeBundlePath_WhenNoCollision_UsesPlainVersionName(string separator, string expected)
    {
        var bundlePath = XcodeService.ResolveManagedXcodeBundlePath(
            "/Applications",
            "26.3",
            "17A123",
            [],
            separator);

        bundlePath.Should().Be(expected);
    }

    [Theory]
    [InlineData("_", "/Applications/Xcode_26.3.app", "/Applications/Xcode_26.3_17A123.app")]
    [InlineData("-", "/Applications/Xcode-26.3.app", "/Applications/Xcode-26.3-17A123.app")]
    public void ResolveManagedXcodeBundlePath_WhenPlainNameExists_AppendsBuildNumber(
        string separator, string existing, string expected)
    {
        var bundlePath = XcodeService.ResolveManagedXcodeBundlePath(
            "/Applications",
            "26.3",
            "17A123",
            [existing],
            separator);

        bundlePath.Should().Be(expected);
    }

    [Fact]
    public void ResolveManagedXcodeBundlePath_WhenBuildNumberedNameAlsoExists_AppendsNumericSuffix()
    {
        var bundlePath = XcodeService.ResolveManagedXcodeBundlePath(
            "/Applications",
            "26.3",
            "17A123",
            [
                "/Applications/Xcode_26.3.app",
                "/Applications/Xcode_26.3_17A123.app"
            ],
            XcodeBundleSeparatorOptions.Underscore);

        bundlePath.Should().Be("/Applications/Xcode_26.3_17A123_2.app");
    }

    [Fact]
    public void CreateSelectionPlan_WhenSelectedBundleIsCanonical_SelectsCanonicalWithoutRename()
    {
        var managedDefaultState = new XcodeManagedDefaultState(
            CanonicalAppPath: "/Applications/Xcode.app",
            Exists: true,
            IsSymlink: false,
            LinkTargetPath: null,
            Version: "26.3",
            BuildNumber: "17A123");

        var plan = XcodeService.CreateSelectionPlan(
            "/Applications/Xcode.app",
            managedDefaultState,
            ["/Applications/Xcode.app"],
            selectionAction: XcodeSelectionActionOptions.Rename,
            separator: XcodeBundleSeparatorOptions.Hyphen);

        plan.SelectedAppPath.Should().Be("/Applications/Xcode.app");
        plan.XcodeSelectPath.Should().Be("/Applications/Xcode.app");
        plan.DefaultMoveDestinationPath.Should().BeNull();
        plan.RemoveCanonicalSymlink.Should().BeFalse();
        plan.CreateCanonicalSymlink.Should().BeFalse();
    }

    [Fact]
    public void CreateSelectionPlan_WhenSelectingDifferentBundle_MovesCanonicalToXcodesStyleName()
    {
        var managedDefaultState = new XcodeManagedDefaultState(
            CanonicalAppPath: "/Applications/Xcode.app",
            Exists: true,
            IsSymlink: false,
            LinkTargetPath: null,
            Version: "26.3",
            BuildNumber: "17A123");

        var plan = XcodeService.CreateSelectionPlan(
            "/Applications/Xcode_26.4.app",
            managedDefaultState,
            [
                "/Applications/Xcode.app",
                "/Applications/Xcode_26.4.app"
            ],
            selectionAction: XcodeSelectionActionOptions.Rename,
            separator: XcodeBundleSeparatorOptions.Hyphen);

        plan.SelectedAppPath.Should().Be("/Applications/Xcode_26.4.app");
        plan.XcodeSelectPath.Should().Be("/Applications/Xcode.app");
        plan.DefaultMoveDestinationPath.Should().Be("/Applications/Xcode-26.3.app");
        plan.RemoveCanonicalSymlink.Should().BeFalse();
        plan.CreateCanonicalSymlink.Should().BeFalse();
    }

    [Fact]
    public void CreateSelectionPlan_WhenCanonicalMoveCollides_UsesUniqueDestinationPath()
    {
        var managedDefaultState = new XcodeManagedDefaultState(
            CanonicalAppPath: "/Applications/Xcode.app",
            Exists: true,
            IsSymlink: false,
            LinkTargetPath: null,
            Version: "26.3",
            BuildNumber: "17A123");

        var plan = XcodeService.CreateSelectionPlan(
            "/Applications/Xcode_26.4.app",
            managedDefaultState,
            [
                "/Applications/Xcode.app",
                "/Applications/Xcode_26.4.app",
                "/Applications/Xcode-26.3.app"
            ],
            selectionAction: XcodeSelectionActionOptions.Rename,
            separator: XcodeBundleSeparatorOptions.Hyphen);

        plan.DefaultMoveDestinationPath.Should().Be("/Applications/Xcode-26.3-17A123.app");
    }

    [Fact]
    public void CreateSelectionPlan_WhenSelectionActionIsNone_SelectsOriginalBundleWithoutRenaming()
    {
        var managedDefaultState = new XcodeManagedDefaultState(
            CanonicalAppPath: "/Applications/Xcode.app",
            Exists: true,
            IsSymlink: false,
            LinkTargetPath: null,
            Version: "26.3",
            BuildNumber: "17A123");

        var plan = XcodeService.CreateSelectionPlan(
            "/Applications/Xcode_26.4.app",
            managedDefaultState,
            [
                "/Applications/Xcode.app",
                "/Applications/Xcode_26.4.app"
            ],
            selectionAction: XcodeSelectionActionOptions.None);

        plan.SelectedAppPath.Should().Be("/Applications/Xcode_26.4.app");
        plan.XcodeSelectPath.Should().Be("/Applications/Xcode_26.4.app");
        plan.DefaultMoveDestinationPath.Should().BeNull();
        plan.RemoveCanonicalSymlink.Should().BeFalse();
        plan.CreateCanonicalSymlink.Should().BeFalse();
    }

    [Fact]
    public void CreateSelectionPlan_WhenSelectionActionIsNoneAndSymlinkRequested_CreatesCanonicalSymlink()
    {
        var managedDefaultState = new XcodeManagedDefaultState(
            CanonicalAppPath: "/Applications/Xcode.app",
            Exists: true,
            IsSymlink: false,
            LinkTargetPath: null,
            Version: "26.3",
            BuildNumber: "17A123");

        var plan = XcodeService.CreateSelectionPlan(
            "/Applications/Xcode_26.4.app",
            managedDefaultState,
            [
                "/Applications/Xcode.app",
                "/Applications/Xcode_26.4.app"
            ],
            selectionAction: XcodeSelectionActionOptions.None,
            createSymlinkOnSelect: true);

        plan.SelectedAppPath.Should().Be("/Applications/Xcode_26.4.app");
        plan.XcodeSelectPath.Should().Be("/Applications/Xcode_26.4.app");
        plan.DefaultMoveDestinationPath.Should().BeNull();
        plan.RemoveCanonicalSymlink.Should().BeFalse();
        plan.CreateCanonicalSymlink.Should().BeTrue();
    }

    [Fact]
    public void CreateSelectionPlan_WhenCanonicalPathIsSymlink_UsesLinkTargetAndRemovesSymlink()
    {
        var managedDefaultState = new XcodeManagedDefaultState(
            CanonicalAppPath: "/Applications/Xcode.app",
            Exists: true,
            IsSymlink: true,
            LinkTargetPath: "/Applications/Xcode_26.3_17A123.app",
            Version: null,
            BuildNumber: null);

        var plan = XcodeService.CreateSelectionPlan(
            "/Applications/Xcode.app",
            managedDefaultState,
            [
                "/Applications/Xcode.app",
                "/Applications/Xcode_26.3_17A123.app"
            ],
            selectionAction: XcodeSelectionActionOptions.Rename,
            separator: XcodeBundleSeparatorOptions.Underscore);

        plan.SelectedAppPath.Should().Be("/Applications/Xcode_26.3_17A123.app");
        plan.XcodeSelectPath.Should().Be("/Applications/Xcode.app");
        plan.DefaultMoveDestinationPath.Should().BeNull();
        plan.RemoveCanonicalSymlink.Should().BeTrue();
        plan.CreateCanonicalSymlink.Should().BeFalse();
    }

    [Fact]
    public void ComputeNormalizationPlan_WhenAllBundlesMatch_ReturnsEmptyPlan()
    {
        var plan = XcodeService.ComputeNormalizationPlan(
            [
                ("/Applications/Xcode_26.3.app", "26.3", "17A123"),
                ("/Applications/Xcode_26.2.app", "26.2", "17A500")
            ],
            XcodeBundleSeparatorOptions.Underscore,
            currentSymlinkTarget: null);

        plan.Renames.Should().BeEmpty();
        plan.SymlinkRetargetPath.Should().BeNull();
        plan.HasWork.Should().BeFalse();
    }

    [Fact]
    public void ComputeNormalizationPlan_SwitchUnderscoreToHyphen_RenamesBundles()
    {
        var plan = XcodeService.ComputeNormalizationPlan(
            [
                ("/Applications/Xcode_26.3_17A123.app", "26.3", "17A123"),
                ("/Applications/Xcode_26.2.app", "26.2", "17A500")
            ],
            XcodeBundleSeparatorOptions.Hyphen,
            currentSymlinkTarget: null);

        plan.Renames.Should().HaveCount(2);
        plan.Renames.Should().Contain(r =>
            r.FromPath == "/Applications/Xcode_26.3_17A123.app" &&
            r.ToPath == "/Applications/Xcode-26.3.app");
        plan.Renames.Should().Contain(r =>
            r.FromPath == "/Applications/Xcode_26.2.app" &&
            r.ToPath == "/Applications/Xcode-26.2.app");
    }

    [Fact]
    public void ComputeNormalizationPlan_WhenSymlinkTargetIsRenamed_SetsRetargetPath()
    {
        var plan = XcodeService.ComputeNormalizationPlan(
            [
                ("/Applications/Xcode_26.3_17A123.app", "26.3", "17A123")
            ],
            XcodeBundleSeparatorOptions.Underscore,
            currentSymlinkTarget: "/Applications/Xcode_26.3_17A123.app");

        plan.Renames.Should().ContainSingle()
            .Which.ToPath.Should().Be("/Applications/Xcode_26.3.app");
        plan.SymlinkRetargetPath.Should().Be("/Applications/Xcode_26.3.app");
    }

    [Fact]
    public void ComputeNormalizationPlan_SwappingSeparators_HandlesCollisionViaBuildNumber()
    {
        // Two installs of the same version exist, one in each naming style. After
        // switching to hyphen, both want the plain `Xcode-26.3.app` slot — one must
        // keep the build number suffix to disambiguate.
        var plan = XcodeService.ComputeNormalizationPlan(
            [
                ("/Applications/Xcode_26.3.app", "26.3", "17A123"),
                ("/Applications/Xcode_26.3_17A400.app", "26.3", "17A400")
            ],
            XcodeBundleSeparatorOptions.Hyphen,
            currentSymlinkTarget: null);

        plan.Renames.Should().HaveCount(2);
        plan.Renames.Select(r => r.ToPath).Should().BeEquivalentTo(
            new[] { "/Applications/Xcode-26.3.app", "/Applications/Xcode-26.3-17A400.app" });
    }

    private static string CreateTempDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "MauiSherpa-XcodeServiceTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    private static string CreateBundledExecutable(string baseDirectory, params string[] relativeSegments)
    {
        var path = Path.Combine(baseDirectory, Path.Combine(relativeSegments));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
        return path;
    }
}
