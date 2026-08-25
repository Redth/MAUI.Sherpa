using FluentAssertions;

namespace MauiSherpa.Bundles.Tests.Toolchain;

public sealed class BundleToolchainInstallerTests
{
    [Fact]
    public async Task PrepareAsync_CancelledToken_DoesNotRunProcesses()
    {
        using var workspace = new ToolchainTestWorkspace();
        var runner = new RecordingBundleProcessRunner((request, _) =>
            throw new InvalidOperationException($"Unexpected process {request.FileName}"));
        var installer = new BundleToolchainInstaller(
            runner,
            null,
            CreateHost(workspace));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = () => installer.PrepareAsync(
            new BundleToolchainRequirements { DotnetSdkVersion = "10.0.400" },
            BundlePlatform.Android,
            new HashSet<string>(StringComparer.Ordinal),
            dryRun: true,
            cancellationToken: cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        runner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task PrepareAsync_DryRun_VerifiesAlreadyInstalledDependenciesWithoutInstallers()
    {
        using var workspace = new ToolchainTestWorkspace();
        CreateSdkManager(workspace);
        var runner = new RecordingBundleProcessRunner((request, _) => request switch
        {
            { FileName: "dotnet", Arguments: ["--list-sdks"] } =>
                new BundleProcessResult(0, "10.0.400 [/usr/local/share/dotnet/sdk]", string.Empty),
            { FileName: "dotnet", Arguments: ["workload", "list"] } =>
                new BundleProcessResult(0, """
                    Installed Workload Id      Manifest Version
                    maui                     10.0.400/10.0.400
                    """, string.Empty),
            { FileName: "java", Arguments: ["-version"] } =>
                new BundleProcessResult(0, string.Empty, """openjdk version "21.0.3" 2024-04-16"""),
            var processRequest when processRequest.FileName.EndsWith("sdkmanager", StringComparison.Ordinal) && processRequest.Arguments.SequenceEqual(["--list_installed"]) =>
                new BundleProcessResult(0, """
                    Installed packages:
                      Path                              | Version | Description | Location
                      platforms;android-36              | 3       | Android SDK Platform 36 | platforms/android-36
                    """, string.Empty),
            _ => throw new InvalidOperationException($"Unexpected process {request.FileName} {string.Join(' ', request.Arguments)}")
        });
        var installer = new BundleToolchainInstaller(
            runner,
            null,
            CreateHost(workspace));

        var result = await installer.PrepareAsync(
            new BundleToolchainRequirements
            {
                DotnetSdkVersion = "10.0.400",
                Workloads = ["maui"],
                AndroidSdkPackages = ["platforms;android-36"],
                JdkVersion = "21.0.3"
            },
            BundlePlatform.Android,
            new HashSet<string>(StringComparer.Ordinal),
            dryRun: true);

        result.Diagnostics.Should().ContainInOrder(
            "Verified .NET SDK 10.0.400.",
            "Verified .NET workload maui.",
            "Verified Android SDK package platforms;android-36.",
            "Verified JDK 21.0.3.");
        runner.Requests.Select(request => string.Join(' ', request.Arguments))
            .Should().BeEquivalentTo(
                [
                    "--list-sdks",
                    "workload list",
                    "--list_installed",
                    "-version"
                ],
                options => options.WithoutStrictOrdering());
    }

    [Fact]
    public async Task PrepareAsync_InstallsOnlyMissingDependencies()
    {
        using var workspace = new ToolchainTestWorkspace();
        CreateSdkManager(workspace);
        var dotnetUpPath = workspace.CreateFile(Path.Combine(
            "home",
            ".dotnetup",
            OperatingSystem.IsWindows() ? "dotnetup.exe" : "dotnetup"));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(dotnetUpPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var runner = new RecordingBundleProcessRunner((request, _) => request switch
        {
            { FileName: "dotnet", Arguments: ["--list-sdks"] } =>
                new BundleProcessResult(0, "10.0.302 [/usr/local/share/dotnet/sdk]", string.Empty),
            { FileName: "dotnet", Arguments: ["workload", "list"] } =>
                new BundleProcessResult(0, """
                    Installed Workload Id      Manifest Version
                    android                   10.0.400/10.0.400
                    """, string.Empty),
            { FileName: "dotnet", Arguments: ["workload", "install", "maui", "--version", "10.0.400", "--skip-manifest-update"] } =>
                new BundleProcessResult(0, "installed workload", string.Empty),
            { FileName: "java", Arguments: ["-version"] } =>
                new BundleProcessResult(0, string.Empty, """openjdk version "21.0.3" 2024-04-16"""),
            { FileName: var fileName, Arguments: ["sdk", "install", "10.0.400", "--set-default-install", "--no-progress"] }
                when fileName == dotnetUpPath => new BundleProcessResult(0, "installed sdk", string.Empty),
            var processRequest when processRequest.FileName.EndsWith("sdkmanager", StringComparison.Ordinal) && processRequest.Arguments.SequenceEqual(["--list_installed"]) =>
                new BundleProcessResult(0, """
                    Installed packages:
                      Path                              | Version | Description | Location
                      build-tools;36.0.0                | 36.0.0  | Android SDK Build-Tools 36 | build-tools/36.0.0
                    """, string.Empty),
            var processRequest when processRequest.FileName.EndsWith("sdkmanager", StringComparison.Ordinal) && processRequest.Arguments.SequenceEqual(["--install", "platforms;android-36"]) =>
                new BundleProcessResult(0, "installed package", string.Empty),
            _ => throw new InvalidOperationException($"Unexpected process {request.FileName} {string.Join(' ', request.Arguments)}")
        });
        var installer = new BundleToolchainInstaller(
            runner,
            null,
            CreateHost(workspace));

        var result = await installer.PrepareAsync(
            new BundleToolchainRequirements
            {
                DotnetSdkVersion = "10.0.400",
                WorkloadSetVersion = "10.0.400",
                Workloads = ["maui"],
                AndroidSdkPackages = ["build-tools;36.0.0", "platforms;android-36"],
                JdkVersion = "21.0.3"
            },
            BundlePlatform.Android,
            new HashSet<string>(StringComparer.Ordinal),
            dryRun: false);

        result.Diagnostics.Should().ContainInOrder(
            "Installed .NET SDK 10.0.400.",
            "Prepared .NET workload maui.",
            "Verified Android SDK package build-tools;36.0.0.",
            "Prepared Android SDK package platforms;android-36.",
            "Verified JDK 21.0.3.");
    }

    [Fact]
    public async Task PrepareAsync_UsesVersionPlistToMatchXcodeBundle()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        using var workspace = new ToolchainTestWorkspace();
        var xcodeDeveloperPath = workspace.CreateDirectory(Path.Combine("Applications", "Xcode.app", "Contents", "Developer"));
        workspace.CreateFile(
            Path.Combine("Applications", "Xcode.app", "Contents", "version.plist"),
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <plist version="1.0">
              <dict>
                <key>CFBundleShortVersionString</key>
                <string>26.2.1</string>
              </dict>
            </plist>
            """);

        var runner = new RecordingBundleProcessRunner((request, _) =>
            throw new InvalidOperationException($"Unexpected process {request.FileName}"));
        var installer = new BundleToolchainInstaller(
            runner,
            null,
            CreateHost(workspace, applicationsRoot: Path.Combine(workspace.RootPath, "Applications")));

        var result = await installer.PrepareAsync(
            new BundleToolchainRequirements { XcodeVersion = "26.2" },
            BundlePlatform.MacCatalyst,
            new HashSet<string>(StringComparer.Ordinal),
            dryRun: true);

        result.Environment["DEVELOPER_DIR"].Should().Be(xcodeDeveloperPath);
        result.Diagnostics.Should().ContainSingle("Selected Xcode 26.2 through DEVELOPER_DIR.");
        runner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task PrepareAsync_DoesNotAcceptJdkVersionSubstring()
    {
        using var workspace = new ToolchainTestWorkspace();
        var runner = new RecordingBundleProcessRunner((request, _) => request switch
        {
            { FileName: "java", Arguments: ["-version"] } =>
                new BundleProcessResult(0, string.Empty, "java version \"1.8.0_171\""),
            _ => throw new InvalidOperationException(
                $"Unexpected process {request.FileName} {string.Join(' ', request.Arguments)}")
        });
        var installer = new BundleToolchainInstaller(
            runner,
            null,
            CreateHost(workspace));

        var act = () => installer.PrepareAsync(
            new BundleToolchainRequirements { JdkVersion = "17" },
            BundlePlatform.Android,
            new HashSet<string>(StringComparer.Ordinal),
            dryRun: true);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*JDK 17 is required*");
    }

    private static BundleToolchainHost CreateHost(ToolchainTestWorkspace workspace, string? applicationsRoot = null) => new()
    {
        GetEnvironmentVariable = name => name switch
        {
            "ANDROID_SDK_ROOT" => Path.Combine(workspace.RootPath, "android-sdk"),
            "ANDROID_HOME" => null,
            _ => Environment.GetEnvironmentVariable(name)
        },
        FileExists = File.Exists,
        GetFolderPath = folder => folder == Environment.SpecialFolder.UserProfile
            ? Path.Combine(workspace.RootPath, "home")
            : Environment.GetFolderPath(folder),
        EnumerateDirectories = (path, pattern, option) =>
        {
            var root = path == "/Applications" && applicationsRoot is not null ? applicationsRoot : path;
            return Directory.Exists(root)
                ? Directory.EnumerateDirectories(root, pattern, option)
                : Enumerable.Empty<string>();
        },
        ReadAllText = File.ReadAllText
    };

    private static string CreateSdkManager(ToolchainTestWorkspace workspace)
    {
        var fileName = OperatingSystem.IsWindows() ? "sdkmanager.bat" : "sdkmanager";
        var path = workspace.CreateFile(Path.Combine("android-sdk", "cmdline-tools", "latest", "bin", fileName));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }
}
