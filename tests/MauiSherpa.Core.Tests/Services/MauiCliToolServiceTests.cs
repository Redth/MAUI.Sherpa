using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Profiling;
using MauiSherpa.Core.Services;
using MauiSherpa.Workloads.NuGet;
using Moq;
using NuGet.Versioning;

namespace MauiSherpa.Core.Tests.Services;

public class MauiCliToolServiceTests
{
    [Fact]
    public async Task GetStatusAsync_ReportsAvailableWhenProfileCommandsExist()
    {
        var process = new QueueProcessExecutionService(
            Completed("""{"version":"1.2.3","runtime":"10.0","os":"macOS"}"""),
            Completed("startup help"),
            Completed("manual help"));
        var service = CreateService(process, () => "/fake/maui");

        var status = await service.GetStatusAsync();

        status.State.Should().Be(MauiCliToolState.Available);
        status.Version.Should().Be("1.2.3");
        process.Requests.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetStatusAsync_ReportsMissingWithoutExecutable()
    {
        var process = new QueueProcessExecutionService();
        var service = CreateService(process, () => null);

        var status = await service.GetStatusAsync();

        status.State.Should().Be(MauiCliToolState.Missing);
        process.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDevicesAsync_ReturnsOnlySupportedRunningTargets()
    {
        var devicesJson = """
            [
              {"name":"Pixel","identifier":"android-1","platforms":["android"],"is_emulator":true,"is_running":true},
              {"name":"Stopped","identifier":"android-2","platforms":["android"],"is_emulator":true,"is_running":false},
              {"name":"iPhone","identifier":"ios-1","platforms":["ios"],"is_emulator":true,"is_running":true},
              {"name":"Physical iPhone","identifier":"ios-2","platforms":["ios"],"is_emulator":false,"is_running":true},
              {"name":"Desktop","identifier":"mac-1","platforms":["maccatalyst"],"is_emulator":false,"is_running":true}
            ]
            """;
        var process = new QueueProcessExecutionService(
            Completed("""{"version":"1.2.3"}"""),
            Completed("startup help"),
            Completed("manual help"),
            Completed(devicesJson));
        var service = CreateService(process, () => "/fake/maui");

        var devices = await service.GetDevicesAsync();

        devices.Select(x => x.Identifier).Should().Equal("android-1", "ios-1");
    }

    [Fact]
    public async Task InstallAsync_FallsBackToPrereleaseWhenVersionIsUnknown()
    {
        var process = new QueueProcessExecutionService(Completed("installed"));
        var service = CreateService(process, () => null);

        var result = await service.InstallAsync();

        result.Success.Should().BeTrue();
        process.Requests.Should().ContainSingle()
            .Which.Arguments.Should().Equal(
                "tool",
                "install",
                "--global",
                "Microsoft.Maui.Cli",
                "--prerelease");
    }

    [Fact]
    public async Task UpdateAsync_PinsTheResolvedVersionWhenKnown()
    {
        var process = new QueueProcessExecutionService(Completed("updated"));
        var service = CreateService(process, () => "/fake/maui");

        var result = await service.UpdateAsync("0.1.0-preview.12.26368.2");

        result.Success.Should().BeTrue();
        process.Requests.Should().ContainSingle()
            .Which.Arguments.Should().Equal(
                "tool",
                "update",
                "--global",
                "Microsoft.Maui.Cli",
                "--version",
                "0.1.0-preview.12.26368.2");
    }

    [Fact]
    public async Task GetUpdateInfoAsync_ReportsUpdateWhenNewerPrereleaseExists()
    {
        var service = CreateService(
            new QueueProcessExecutionService(),
            () => "/fake/maui",
            new StubNuGetClient("0.1.0-preview.12.26358.3", "0.1.0-preview.12.26368.2"));

        var info = await service.GetUpdateInfoAsync(
            new MauiCliToolStatus(
                MauiCliToolState.Available,
                "/fake/maui",
                "0.1.0-preview.12.26358.3+370e95b72f9b"));

        info.UpdateAvailable.Should().BeTrue();
        info.InstalledVersion.Should().Be("0.1.0-preview.12.26358.3");
        info.LatestVersion.Should().Be("0.1.0-preview.12.26368.2");
    }

    [Fact]
    public async Task GetUpdateInfoAsync_ReportsUpToDateOnLatestVersion()
    {
        var service = CreateService(
            new QueueProcessExecutionService(),
            () => "/fake/maui",
            new StubNuGetClient("0.1.0-preview.11.26317.2", "0.1.0-preview.12.26358.3"));

        var info = await service.GetUpdateInfoAsync(
            new MauiCliToolStatus(MauiCliToolState.Available, "/fake/maui", "0.1.0-preview.12.26358.3"));

        info.UpdateAvailable.Should().BeFalse();
        info.LatestVersion.Should().Be("0.1.0-preview.12.26358.3");
    }

    [Fact]
    public async Task GetUpdateInfoAsync_SkipsFeedLookupWhenToolIsMissing()
    {
        var nuget = new StubNuGetClient("1.0.0");
        var service = CreateService(new QueueProcessExecutionService(), () => null, nuget);

        var info = await service.GetUpdateInfoAsync(new MauiCliToolStatus(MauiCliToolState.Missing));

        info.UpdateAvailable.Should().BeFalse();
        info.LatestVersion.Should().BeNull();
        nuget.QueryCount.Should().Be(0);
    }

    [Fact]
    public async Task GetUpdateInfoAsync_ReturnsMessageWhenFeedLookupFails()
    {
        var service = CreateService(
            new QueueProcessExecutionService(),
            () => "/fake/maui",
            new StubNuGetClient(new InvalidOperationException("feed offline")));

        var info = await service.GetUpdateInfoAsync(
            new MauiCliToolStatus(MauiCliToolState.Available, "/fake/maui", "0.1.0-preview.12.26358.3"));

        info.UpdateAvailable.Should().BeFalse();
        info.InstalledVersion.Should().Be("0.1.0-preview.12.26358.3");
        info.Message.Should().Contain("feed offline");
    }

    private static MauiCliToolService CreateService(
        IProcessExecutionService process,
        Func<string?> resolver)
    {
        return new MauiCliToolService(
            process,
            Mock.Of<ILoggingService>(),
            resolver);
    }

    private static MauiCliToolService CreateService(
        IProcessExecutionService process,
        Func<string?> resolver,
        INuGetClient nugetClient)
    {
        return new MauiCliToolService(
            process,
            Mock.Of<ILoggingService>(),
            resolver,
            () => nugetClient);
    }

    private sealed class StubNuGetClient : INuGetClient
    {
        private readonly string[] _versions;
        private readonly Exception? _failure;

        public StubNuGetClient(params string[] versions) => _versions = versions;

        public StubNuGetClient(Exception failure)
        {
            _versions = [];
            _failure = failure;
        }

        public int QueryCount { get; private set; }

        public Task<IReadOnlyList<NuGetVersion>> GetPackageVersionsAsync(
            string packageId,
            bool includePrerelease = false,
            CancellationToken cancellationToken = default)
        {
            QueryCount++;
            if (_failure is not null)
                return Task.FromException<IReadOnlyList<NuGetVersion>>(_failure);

            IReadOnlyList<NuGetVersion> parsed = _versions
                .Select(NuGetVersion.Parse)
                .Where(x => includePrerelease || !x.IsPrerelease)
                .ToArray();
            return Task.FromResult(parsed);
        }

        public Task<string> DownloadPackageAsync(
            string packageId,
            NuGetVersion version,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string?> GetPackageFileContentAsync(
            string packageId,
            NuGetVersion version,
            string filePath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static ProcessResult Completed(string output) =>
        new(0, output, string.Empty, TimeSpan.Zero, ProcessState.Completed);

    private sealed class QueueProcessExecutionService(params ProcessResult[] results)
        : IProcessExecutionService
    {
        private readonly Queue<ProcessResult> _results = new(results);

        public List<ProcessRequest> Requests { get; } = [];
        public ProcessState CurrentState { get; private set; } = ProcessState.Pending;
        public int? ProcessId => null;
        public event EventHandler<ProcessOutputEventArgs>? OutputReceived;
        public event EventHandler<ProcessStateChangedEventArgs>? StateChanged;

        public Task<ProcessResult> ExecuteAsync(
            ProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var result = _results.Dequeue();
            CurrentState = result.FinalState;
            return Task.FromResult(result);
        }

        public Task<bool> SendInputAsync(
            string data,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public void Cancel()
        {
        }

        public void Kill()
        {
        }

        public string GetFullOutput() => string.Empty;
    }
}
