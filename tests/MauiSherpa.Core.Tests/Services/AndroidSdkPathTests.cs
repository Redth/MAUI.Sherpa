using System.Diagnostics;
using System.Text;
using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using Moq;

namespace MauiSherpa.Core.Tests.Services;

public sealed class AndroidSdkPathTests : IDisposable
{
    private readonly string root = Path.Combine(AppContext.BaseDirectory, $"path-tests-{Guid.NewGuid():N}");

    public AndroidSdkPathTests() => Directory.CreateDirectory(root);

    [Fact]
    public void SharedLocation_RejectsHomeAndOtherUsersButAcceptsSharedLocations()
    {
        AndroidSdkPathConfiguration.IsSharedLocation(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "sdk")).Should().BeFalse();
        if (OperatingSystem.IsWindows())
        {
            var drive = Path.GetPathRoot(root)!;
            AndroidSdkPathConfiguration.IsSharedLocation(Path.Combine(drive, "Users", "other", "Android", "Sdk")).Should().BeFalse();
            AndroidSdkPathConfiguration.IsSharedLocation(Path.Combine(drive, "Program Files", "Android", "Sdk")).Should().BeTrue();
        }
        else
        {
            foreach (var path in new[] { "/Users/other/sdk", "/home/other/sdk", "/root/sdk", "/var/home/other/sdk", "/run/user/1000/sdk" })
                AndroidSdkPathConfiguration.IsSharedLocation(path).Should().BeFalse();
            AndroidSdkPathConfiguration.IsSharedLocation("/opt/android-sdk").Should().BeTrue();
            AndroidSdkPathConfiguration.IsSharedLocation("/Users-shared/android-sdk").Should().BeTrue();
        }
    }

    [Fact]
    public void DirectoryResolution_ResolvesLinksInParentDirectories()
    {
        if (OperatingSystem.IsWindows()) return;
        var link = Path.Combine(root, "linked-home");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Directory.CreateSymbolicLink(link, home);
        var sdkPath = Path.Combine(link, "Android", "Sdk");
        AndroidSdkPathConfiguration.ResolveDirectory(sdkPath).Should().Be(Path.Combine(home, "Android", "Sdk"));
        AndroidSdkPathConfiguration.IsSharedLocation(sdkPath).Should().BeFalse();
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void ProfileUpdate_PreservesUnrelatedContentAndIsIdempotent(string newline)
    {
        var prefix = $"# existing settings{newline}export CUSTOM=value{newline}";
        var block = AndroidSdkPathConfiguration.BuildBlock(["/opt/android-sdk/platform-tools"], AndroidPathShell.Zsh, newline);
        var updated = AndroidSdkPathConfiguration.ReplaceBlock(prefix, block);
        updated.Should().StartWith(prefix);
        AndroidSdkPathConfiguration.ReplaceBlock(updated, block).Should().Be(updated);
        var suffix = $"# following settings{newline}export OTHER=value{newline}";
        var replacement = AndroidSdkPathConfiguration.BuildBlock(["/opt/another-sdk/platform-tools"], AndroidPathShell.Zsh, newline);
        AndroidSdkPathConfiguration.ReplaceBlock(updated + suffix, replacement).Should().Be(prefix + replacement + suffix);
    }

    [Theory]
    [InlineData("# >>> MAUI Sherpa Android SDK PATH >>>\nno end")]
    [InlineData("# >>> MAUI Sherpa Android SDK PATH >>> extra\n# <<< MAUI Sherpa Android SDK PATH <<<\n")]
    [InlineData("# >>> MAUI Sherpa Android SDK PATH >>>\necho # <<< MAUI Sherpa Android SDK PATH <<<\n")]
    [InlineData("# <<< MAUI Sherpa Android SDK PATH <<<\n# >>> MAUI Sherpa Android SDK PATH >>>\n")]
    public void ProfileUpdate_RejectsMalformedMarkers(string original)
    {
        var block = AndroidSdkPathConfiguration.BuildBlock(["/opt/sdk/bin"], AndroidPathShell.Sh, "\n");
        var update = () => AndroidSdkPathConfiguration.ReplaceBlock(original, block);
        update.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void WindowsUpdate_PreservesUnrelatedEntriesAndAvoidsDuplicateSdkEntries()
    {
        var updated = AndroidSdkPathConfiguration.UpdateWindowsPath(@"%SystemRoot%\System32;C:\Other;""c:\android sdk\platform-tools\"";C:\Last",
            [@"C:\Android SDK\platform-tools", @"C:\Android SDK\emulator"]);
        updated.Should().Be(@"C:\Android SDK\platform-tools;C:\Android SDK\emulator;%SystemRoot%\System32;C:\Other;C:\Last");
        AndroidSdkPathConfiguration.UpdateWindowsPath("", [@"C:\Android SDK\platform-tools"])
            .Should().Be(@"C:\Android SDK\platform-tools");
        AndroidSdkPathConfiguration.UpdateWindowsPath(updated, [@"C:\Android SDK\platform-tools", @"C:\Android SDK\emulator"])
            .Should().Be(updated);
    }

    [Fact]
    public void DecodeProfile_PreservesBomAndRejectsInvalidOrNonUtf8Text()
    {
        var bytes = Encoding.UTF8.Preamble.ToArray().Concat(Encoding.UTF8.GetBytes("# profile\n")).ToArray();
        AndroidSdkPathConfiguration.DecodeProfile(bytes).Should().Be(("# profile\n", true));
        var invalid = () => AndroidSdkPathConfiguration.DecodeProfile([0xff, 0xfe]);
        invalid.Should().Throw<DecoderFallbackException>();
        var utf16 = () => AndroidSdkPathConfiguration.DecodeProfile(Encoding.Unicode.GetBytes("# profile"));
        utf16.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(AndroidPathShell.Sh, "/bin/sh")]
    [InlineData(AndroidPathShell.Bash, "/bin/bash")]
    [InlineData(AndroidPathShell.Zsh, "/bin/zsh")]
    public async Task ShellBlock_PreservesQuotedPathsAndExistingEntriesWithoutDuplicates(AndroidPathShell shell, string executable)
    {
        if (OperatingSystem.IsWindows() || !File.Exists(executable)) return;
        var sdk = "/opt/Android SDK's $tools\\dir";
        var block = AndroidSdkPathConfiguration.BuildBlock([sdk, "/opt/sdk/emulator"], shell, "\n");
        using var process = new Process
        {
            StartInfo = new(executable) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false }
        };
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add(block + block + "printf '%s' \"$PATH\"");
        process.StartInfo.Environment["PATH"] = $"/usr/bin:{sdk}:/bin:/other";
        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        process.ExitCode.Should().Be(0, error);
        output.Should().Be($"{sdk}:/opt/sdk/emulator:/usr/bin:/bin:/other");
    }

    [Fact]
    public void ToolDiscovery_PrefersLatestCommandToolsAndStableBuildTools()
    {
        CreateTool("cmdline-tools/12.0/bin", "sdkmanager");
        var latest = CreateTool("cmdline-tools/latest/bin", "sdkmanager");
        var avd = CreateTool("cmdline-tools/latest/bin", "avdmanager");
        var stable = CreateTool("build-tools/35.0.0", "aapt2");
        CreateTool("build-tools/36.0.0-rc1", "aapt2");
        var tools = AndroidSdkPathService.DiscoverTools(root, true);
        tools.Single(t => t.Name == "sdkmanager").Path.Should().Be(latest);
        tools.Single(t => t.Name == "avdmanager").Path.Should().Be(avd);
        tools.Single(t => t.Name == "aapt2").Path.Should().Be(stable);
        tools.Single(t => t.Name == "adb").Path.Should().BeNull();
        AndroidSdkPathService.DiscoverTools(root, false).Should().HaveCount(5);
    }

    [Fact]
    public async Task MachinePlanForUserSdk_IsRejectedBeforeShellOrElevationRuns()
    {
        var sdk = new Mock<IAndroidSdkService>();
        var userSdk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Android", "Sdk");
        sdk.SetupGet(s => s.SdkPath).Returns(userSdk);
        var processes = new Mock<IProcessExecutionService>(MockBehavior.Strict);
        var service = new AndroidSdkPathService(sdk.Object, processes.Object, Mock.Of<ILoggingService>());
        service.CanConfigureMachinePath.Should().BeFalse();
        await service.Invoking(s => s.CheckAsync(new(s.DefaultShell, AndroidPathScope.Machine)))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*outside user-local*");
        var plan = new AndroidPathPlan(userSdk, new(service.DefaultShell, AndroidPathScope.Machine), "/etc/profile",
            [], [], "", "", "", false, false);
        await service.Invoking(s => s.ApplyAsync(plan)).Should().ThrowAsync<InvalidOperationException>().WithMessage("*outside user-local*");
        processes.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ApplyUserProfile_PreservesPermissionsAndBackupInResolvedZdotdir()
    {
        if (OperatingSystem.IsWindows() || !File.Exists("/bin/zsh")) return;
        var profile = Path.Combine(root, ".zshrc");
        var original = "# unrelated configuration\nexport CUSTOM=value\n";
        await File.WriteAllTextAsync(profile, original);
        File.SetUnixFileMode(profile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var service = CreateZshService();
        var plan = await service.CheckAsync(new(AndroidPathShell.Zsh, AndroidPathScope.User));
        plan.Target.Should().Be(profile);
        await service.ApplyAsync(plan);
        (await File.ReadAllTextAsync(profile)).Should().Be(plan.UpdatedContent);
        File.GetUnixFileMode(profile).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var backup = Directory.GetFiles(root, ".zshrc.sherpa-backup-*").Should().ContainSingle().Subject;
        (await File.ReadAllTextAsync(backup)).Should().Be(original);
    }

    [Fact]
    public async Task ApplyUserProfile_RejectsStalePreviewWithoutOverwritingExternalEdit()
    {
        if (OperatingSystem.IsWindows() || !File.Exists("/bin/zsh")) return;
        var service = CreateZshService();
        var plan = await service.CheckAsync(new(AndroidPathShell.Zsh, AndroidPathScope.User));
        var profile = Path.Combine(root, ".zshrc");
        await File.WriteAllTextAsync(profile, "# external edit\n");
        await service.Invoking(s => s.ApplyAsync(plan)).Should().ThrowAsync<InvalidOperationException>().WithMessage("*changed since this preview*");
        (await File.ReadAllTextAsync(profile)).Should().Be("# external edit\n");
        Directory.GetFiles(root, ".zshrc.sherpa-backup-*").Should().BeEmpty();
    }

    private AndroidSdkPathService CreateZshService()
    {
        CreateTool("platform-tools", "adb");
        var sdk = new Mock<IAndroidSdkService>();
        sdk.SetupGet(s => s.SdkPath).Returns(root);
        var processes = new Mock<IProcessExecutionService>();
        processes.Setup(p => p.ExecuteAsync(It.IsAny<ProcessRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessResult(0, $"__MAUI_SHERPA_PATH__/usr/bin:/bin\n__MAUI_SHERPA_PROFILE__{root}\n",
                "", TimeSpan.Zero, ProcessState.Completed));
        return new(sdk.Object, processes.Object, Mock.Of<ILoggingService>());
    }

    private string CreateTool(string directory, string name)
    {
        var path = Path.Combine(root, directory.Replace('/', Path.DirectorySeparatorChar), name + (OperatingSystem.IsWindows() ? ".exe" : ""));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    public void Dispose() => Directory.Delete(root, true);
}
