using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using Moq;

namespace MauiSherpa.Core.Tests.Services;

public class OpenJdkServiceTests : IDisposable
{
    private readonly string root = Path.Combine(AppContext.BaseDirectory, "test-artifacts", $"openjdk-{Guid.NewGuid():N}");
    private readonly Mock<IOpenJdkSettingsService> settings = new();
    private readonly Mock<ILoggingService> logger = new();
    private readonly CatalogHandler handler = new();
    private readonly HttpClient http;
    private readonly OpenJdkService service;

    public OpenJdkServiceTests()
    {
        Directory.CreateDirectory(root);
        http = new HttpClient(handler);
        service = new OpenJdkService(http, settings.Object, logger.Object, Path.Combine(root, "installed"));
    }

    private static string Platform => OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
    private static string Architecture => RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "aarch64" : "x64";
    private static string Url(string version, string? platform = null, string? architecture = null) =>
        $"https://aka.ms/download-jdk/microsoft-jdk-{version}-{platform ?? Platform}-{architecture ?? Architecture}.{((platform ?? Platform) == "windows" ? "zip" : "tar.gz")}";
    private static string Catalog(params string[] versions) => string.Join("\n", versions.Select(v => $"<a href=\"{Url(v)}\">Download</a>"));

    [Fact]
    public void Catalog_SelectsNewestBuildPerMajorAndExactPlatform()
    {
        var catalog = $"""
            <a href="{Url("21.0.9.1", "macos", "aarch64")}">old</a>
            <a href="{Url("21.0.10.1", "macos", "aarch64")}">current</a>
            <a href="{Url("21.0.10.2", "macos", "aarch64")}">newer build</a>
            <a href="{Url("21.0.10.2", "macos", "aarch64")}.sha256sum.txt">checksum</a>
            <a href="{Url("25.0.1", "macos", "aarch64")}">25</a>
            <a href="{Url("17.0.20", "macos", "aarch64")}">17</a>
            <a href="{Url("11.0.30", "macos", "aarch64")}">legacy</a>
            <a href="{Url("11.0.30", "macos", "x64")}">unsupported arch</a>
            <a href="{Url("21.0.99", "windows", "aarch64")}">wrong OS</a>
            https://untrusted.example/microsoft-jdk-21.0.99-macos-aarch64.tar.gz
            """;
        var releases = OpenJdkService.ParseReleases(catalog, "macos", "aarch64");
        releases.Select(r => r.Version).Should().Equal("25.0.1", "21.0.10.2", "17.0.20");
        releases.Should().OnlyContain(r => r.DownloadUrl.Host == "aka.ms");
    }

    [Fact]
    public void Catalog_RejectsChangedOrUnsupportedCatalogRatherThanInventingVersions()
    {
        var parse = () => OpenJdkService.ParseReleases(Catalog("21.0.10"), "unsupported", "x64");
        parse.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Install_DoesNotOfferVersionsBelow17()
    {
        handler.Catalog = Catalog("11.0.30", "17.0.20", "21.0.10");
        (await service.GetReleasesAsync()).Select(r => r.MajorVersion).Should().Equal(21, 17);
        await service.Invoking(s => s.InstallAsync(11)).Should().ThrowAsync<InvalidOperationException>().WithMessage("*not available*");
        handler.ArchiveRequests.Should().Be(0);
    }

    [Fact]
    public async Task Catalog_CachesAndExplicitRefreshFetchesNewPatch()
    {
        handler.Catalog = Catalog("21.0.9");
        (await service.GetReleasesAsync()).Single().Version.Should().Be("21.0.9");
        handler.Catalog = Catalog("21.0.10");
        (await service.GetReleasesAsync()).Single().Version.Should().Be("21.0.9");
        (await service.GetReleasesAsync(true)).Single().Version.Should().Be("21.0.10");
        handler.CatalogRequests.Should().Be(2);
    }

    [Fact]
    public async Task Checksum_RejectsCorruptDownloadAndMalformedHash()
    {
        var archive = Path.Combine(root, "archive");
        await File.WriteAllTextAsync(archive, "download");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("download")));
        await OpenJdkService.VerifyChecksumAsync(archive, $"{hash}  archive");
        await service.Invoking(_ => OpenJdkService.VerifyChecksumAsync(archive, new string('0', 64)))
            .Should().ThrowAsync<InvalidDataException>().WithMessage("*verification failed*");
        await service.Invoking(_ => OpenJdkService.VerifyChecksumAsync(archive, "not a checksum"))
            .Should().ThrowAsync<InvalidDataException>().WithMessage("*invalid*");
    }

    [Fact]
    public void Inspect_RequiresJavaAndCompilerAndReadsVendor()
    {
        var home = CreateHome(Path.Combine(root, "home"), "21.0.10");
        OpenJdkService.Inspect(home).IsMicrosoft.Should().BeTrue();
        File.Delete(Path.Combine(home, "bin", OperatingSystem.IsWindows() ? "javac.exe" : "javac"));
        var inspect = () => OpenJdkService.Inspect(home);
        inspect.Should().Throw<InvalidOperationException>().WithMessage("*bin/javac*");
    }

    [Fact]
    public async Task Updates_StayWithinMajorAndOnlyOfferMicrosoftBuilds()
    {
        var managed = CreateHome(Path.Combine(root, "installed", "microsoft-jdk-21.0.9.1"), "21.0.9.1");
        var external = CreateHome(Path.Combine(root, "external"), "17.0.1");
        File.WriteAllText(Path.Combine(external, "release"), "JAVA_VERSION=\"17.0.1\"\nIMPLEMENTOR=\"Another vendor\"\n");
        settings.Setup(s => s.GetEffectiveJdkPathAsync()).ReturnsAsync(external);
        handler.Catalog = Catalog("25.0.1", "21.0.10.1", "17.0.20");
        await service.CheckForUpdatesAsync();
        service.AvailableUpdates.Should().ContainSingle();
        var update = service.AvailableUpdates.Single();
        update.Installed.Path.Should().Be(managed);
        update.Installed.IsActive.Should().BeFalse();
        update.Available.Version.Should().Be("21.0.10.1");
        settings.Verify(s => s.SetCustomJdkPathAsync(It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void ExternalMicrosoftUpdates_IncludeFourthJavaVersionComponentButStayWithinMajor()
    {
        var installed = new OpenJdkInstallation(root, "21.0.10", true, false, true);
        OpenJdkService.IsUpdateAvailable(installed, new(21, "21.0.10.1", new Uri(Url("21.0.10.1")))).Should().BeTrue();
        OpenJdkService.IsUpdateAvailable(installed with { Version = "21.0.10.1" }, new(21, "21.0.10.1", new Uri(Url("21.0.10.1")))).Should().BeFalse();
        OpenJdkService.IsUpdateAvailable(installed, new(21, "21.0.11.1", new Uri(Url("21.0.11.1")))).Should().BeTrue();
        OpenJdkService.IsUpdateAvailable(installed, new(25, "25.0.1", new Uri(Url("25.0.1")))).Should().BeFalse();
    }

    [Fact]
    public async Task Updates_ReportOfflineErrorAndRecoverOnNextCheck()
    {
        CreateHome(Path.Combine(root, "installed", "microsoft-jdk-21.0.9"), "21.0.9");
        handler.FailCatalog = true;
        await service.CheckForUpdatesAsync();
        service.UpdateCheckError.Should().NotBeNull();
        handler.FailCatalog = false;
        handler.Catalog = Catalog("21.0.10");
        await service.CheckForUpdatesAsync();
        service.UpdateCheckError.Should().BeNull();
        service.AvailableUpdates.Should().ContainSingle();
    }

    [Fact]
    public async Task Install_ChecksumFailureLeavesNoInstallationAndDoesNotChangeSelection()
    {
        handler.Catalog = Catalog("21.0.10.1");
        handler.Archive = Encoding.UTF8.GetBytes("corrupt archive");
        handler.Checksum = new string('0', 64);
        await service.Invoking(s => s.InstallAsync(21)).Should().ThrowAsync<InvalidDataException>();
        Directory.EnumerateFileSystemEntries(Path.Combine(root, "installed")).Should().BeEmpty();
        settings.Verify(s => s.SetCustomJdkPathAsync(It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task Install_RejectsExistingDestinationContainingDifferentJavaVersion()
    {
        handler.Catalog = Catalog("21.0.10.1");
        CreateHome(Path.Combine(root, "installed", "microsoft-jdk-21.0.10.1"), "17.0.1");
        await service.Invoking(s => s.InstallAsync(21)).Should().ThrowAsync<InvalidOperationException>().WithMessage("*does not match*");
        handler.ArchiveRequests.Should().Be(0);
    }

    [Fact]
    public async Task Install_RefreshesPatchVerifiesToolsAndPublishesSideBySide()
    {
        if (OperatingSystem.IsWindows()) return; // Fixture tools are POSIX scripts, not Windows executables.
        handler.Catalog = Catalog("21.0.9");
        await service.GetReleasesAsync();
        handler.Catalog = Catalog("21.0.10.1");
        var previous = CreateHome(Path.Combine(root, "installed", "microsoft-jdk-21.0.9"), "21.0.9");
        CreateHome(Path.Combine(root, "archive-source", "jdk", "Contents", "Home"), "21.0.10.1");
        var archive = Path.Combine(root, "test.tar.gz");
        await using (var output = File.Create(archive))
        await using (var gzip = new GZipStream(output, CompressionMode.Compress))
            await TarFile.CreateFromDirectoryAsync(Path.Combine(root, "archive-source"), gzip, false);
        handler.Archive = await File.ReadAllBytesAsync(archive);
        handler.Checksum = Convert.ToHexString(SHA256.HashData(handler.Archive));
        var installed = await service.InstallAsync(21);
        installed.Should().StartWith(Path.Combine(root, "installed", "microsoft-jdk-21.0.10.1"));
        OpenJdkService.Inspect(installed).Version.Should().Be("21.0.10.1");
        File.Exists(Path.Combine(previous, "release")).Should().BeTrue();
        Directory.EnumerateDirectories(Path.Combine(root, "installed"), ".install-*").Should().BeEmpty();
        handler.ArchiveRequests.Should().Be(1);
        (await service.InstallAsync(21)).Should().Be(installed);
        handler.ArchiveRequests.Should().Be(1);
    }

    [Fact]
    public async Task Install_CancellationRemovesStagingAndPreservesExistingBuild()
    {
        handler.Catalog = Catalog("21.0.10");
        var previous = CreateHome(Path.Combine(root, "installed", "microsoft-jdk-21.0.9"), "21.0.9");
        handler.CancelArchive = true;
        await service.Invoking(s => s.InstallAsync(21)).Should().ThrowAsync<OperationCanceledException>();
        Directory.EnumerateDirectories(Path.Combine(root, "installed"), ".install-*").Should().BeEmpty();
        File.Exists(Path.Combine(previous, "release")).Should().BeTrue();
    }

    [Fact]
    public async Task Install_ReportsDownloadProgressAndCancelsImmediatelyMidStream()
    {
        handler.Catalog = Catalog("21.0.10.1");
        using var cts = new CancellationTokenSource();
        var chunkReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.CustomArchiveContent = () => new StallingStreamContent(
            chunkSize: 64 * 1024,
            totalLength: 256 * 1024,
            onFirstChunkRead: () => chunkReceived.TrySetResult());

        var updates = new List<OpenJdkInstallProgress>();
        var progress = new ImmediateProgress<OpenJdkInstallProgress>(u =>
        {
            lock (updates) updates.Add(u);
        });

        var installTask = service.InstallAsync(21, progress, cts.Token);
        await chunkReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();

        await FluentActions.Awaiting(() => installTask.WaitAsync(TimeSpan.FromSeconds(5)))
            .Should().ThrowAsync<OperationCanceledException>();

        updates.Should().Contain(u => u.BytesDownloaded > 0 && u.TotalBytes == 256 * 1024 && u.Percent > 0);
        Directory.EnumerateDirectories(Path.Combine(root, "installed"), ".install-*").Should().BeEmpty();
    }

    [Fact]
    public async Task Uninstall_RemovesOnlyInactiveManagedJdks()
    {
        handler.Catalog = Catalog("21.0.10");
        var managed = CreateHome(Path.Combine(root, "installed", "microsoft-jdk-21.0.9", "Contents", "Home"), "21.0.9");
        var active = CreateHome(Path.Combine(root, "installed", "microsoft-jdk-17.0.1"), "17.0.1");
        var external = CreateHome(Path.Combine(root, "external"), "21.0.9");
        settings.Setup(s => s.GetEffectiveJdkPathAsync()).ReturnsAsync(active);

        await service.Invoking(s => s.UninstallAsync(active)).Should().ThrowAsync<InvalidOperationException>().WithMessage("*active*");
        await service.Invoking(s => s.UninstallAsync(external)).Should().ThrowAsync<InvalidOperationException>().WithMessage("*installed*");
        await service.UninstallAsync(managed);

        Directory.Exists(Path.Combine(root, "installed", "microsoft-jdk-21.0.9")).Should().BeFalse();
        Directory.Exists(active).Should().BeTrue();
        Directory.Exists(external).Should().BeTrue();
    }

    private static string CreateHome(string path, string version)
    {
        Directory.CreateDirectory(Path.Combine(path, "bin"));
        File.WriteAllText(Path.Combine(path, "release"), $"JAVA_VERSION=\"{version}\"\nIMPLEMENTOR=\"Microsoft\"\n");
        foreach (var name in new[] { "java", "javac" })
        {
            var executable = Path.Combine(path, "bin", name + (OperatingSystem.IsWindows() ? ".exe" : ""));
            File.WriteAllText(executable, $"#!/bin/sh\nprintf '{name} {version}\\n'\n");
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return path;
    }

    public void Dispose()
    {
        service.Dispose();
        http.Dispose();
        Directory.Delete(root, true);
    }

    private sealed class CatalogHandler : HttpMessageHandler
    {
        public string Catalog { get; set; } = "";
        public byte[] Archive { get; set; } = [];
        public Func<HttpContent>? CustomArchiveContent { get; set; }
        public string Checksum { get; set; } = "";
        public bool FailCatalog { get; set; }
        public bool CancelArchive { get; set; }
        public int CatalogRequests { get; private set; }
        public int ArchiveRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!;
            if (url.Host == "learn.microsoft.com")
            {
                CatalogRequests++;
                if (FailCatalog) throw new HttpRequestException("Offline");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Catalog) });
            }
            if (url.AbsolutePath.EndsWith(".sha256sum.txt"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Checksum) });
            ArchiveRequests++;
            if (CancelArchive) throw new OperationCanceledException();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = CustomArchiveContent?.Invoke() ?? new ByteArrayContent(Archive) });
        }
    }

    private sealed class ImmediateProgress<T>(Action<T> onReport) : IProgress<T>
    {
        public void Report(T value) => onReport(value);
    }

    private sealed class StallingStreamContent(int chunkSize, long totalLength, Action onFirstChunkRead) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new NotSupportedException();

        protected override bool TryComputeLength(out long length)
        {
            length = totalLength;
            return true;
        }

        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new StallingReadStream(chunkSize, totalLength, onFirstChunkRead));

        private sealed class StallingReadStream(int chunkSize, long totalLength, Action onFirstChunkRead) : Stream
        {
            private bool sentFirstChunk;
            private readonly TaskCompletionSource disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => totalLength;
            public override long Position { get; set; }

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                if (!sentFirstChunk)
                {
                    sentFirstChunk = true;
                    var count = Math.Min(buffer.Length, chunkSize);
                    buffer.Span[..count].Fill(0x42);
                    Position += count;
                    onFirstChunkRead();
                    return count;
                }

                await disposed.Task.WaitAsync(cancellationToken);
                return 0;
            }

            protected override void Dispose(bool disposing)
            {
                disposed.TrySetResult();
                base.Dispose(disposing);
            }

            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
