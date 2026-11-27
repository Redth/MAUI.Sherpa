using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public sealed class OpenJdkService : IOpenJdkService, IDisposable
{
    public const int MinimumMajorVersion = 17;
    public const int RecommendedMajorVersion = 21;
    private const string CatalogUrl = "https://learn.microsoft.com/en-us/java/openjdk/download";
    private readonly HttpClient http;
    private readonly IOpenJdkSettingsService settings;
    private readonly ILoggingService logger;
    private readonly string root;
    private readonly SemaphoreSlim catalogGate = new(1, 1);
    private readonly SemaphoreSlim installGate = new(1, 1);
    private readonly SemaphoreSlim updateGate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private IReadOnlyList<OpenJdkRelease>? releases;
    private DateTimeOffset catalogChecked;
    private int started;

    public OpenJdkService(HttpClient http, IOpenJdkSettingsService settings, ILoggingService logger)
        : this(http, settings, logger, Path.Combine(AppDataPath.GetAppDataDirectory(), "jdks")) { }

    public OpenJdkService(HttpClient http, IOpenJdkSettingsService settings, ILoggingService logger, string root)
    {
        this.http = http;
        this.settings = settings;
        this.logger = logger;
        this.root = root;
    }

    public IReadOnlyList<OpenJdkUpdate> AvailableUpdates { get; private set; } = [];
    public string? UpdateCheckError { get; private set; }
    public event Action? UpdatesChanged;

    public static IReadOnlyList<OpenJdkRelease> ParseReleases(string catalog, string platform, string architecture)
    {
        var extension = platform == "windows" ? "zip" : "tar.gz";
        var matches = Regex.Matches(catalog,
            @"https://aka\.ms/download-jdk/microsoft-jdk-(?<version>\d+(?:\.\d+){2,3})-(?<platform>linux|macos|windows)-(?<arch>x64|aarch64)\.(?<ext>tar\.gz|zip)(?![a-zA-Z0-9.])");
        var result = matches.Cast<Match>()
            .Where(m => m.Groups["platform"].Value == platform && m.Groups["arch"].Value == architecture &&
                m.Groups["ext"].Value == extension)
            .Select(m => new OpenJdkRelease(Version.Parse(m.Groups["version"].Value).Major,
                m.Groups["version"].Value, new Uri(m.Value)))
            .Where(r => r.MajorVersion >= MinimumMajorVersion)
            .GroupBy(r => r.MajorVersion)
            .Select(g => g.MaxBy(r => Version.Parse(r.Version))!)
            .OrderByDescending(r => r.MajorVersion).ToArray();
        if (result.Length == 0)
            throw new InvalidOperationException($"Microsoft has no published OpenJDK archive for {platform}/{architecture}, or its download catalog has changed.");
        return result;
    }

    private static (string Platform, string Architecture) GetPlatform()
    {
        var platform = OperatingSystem.IsWindows() ? "windows" :
            OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst() ? "macos" :
            OperatingSystem.IsLinux() ? "linux" : throw new PlatformNotSupportedException("Microsoft OpenJDK is not supported on this platform.");
        var architecture = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "aarch64",
            _ => throw new PlatformNotSupportedException("Microsoft OpenJDK requires x64 or ARM64.")
        };
        return (platform, architecture);
    }

    public async Task<IReadOnlyList<OpenJdkRelease>> GetReleasesAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        await catalogGate.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && releases != null && DateTimeOffset.UtcNow - catalogChecked < TimeSpan.FromDays(1))
                return releases;
            var (platform, architecture) = GetPlatform();
            var catalog = await http.GetStringAsync(CatalogUrl, cancellationToken);
            releases = ParseReleases(catalog, platform, architecture);
            catalogChecked = DateTimeOffset.UtcNow;
            return releases;
        }
        finally { catalogGate.Release(); }
    }

    public static OpenJdkInstallation Inspect(string path, bool managed = false, bool active = false)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException("Choose an absolute JDK home folder.", nameof(path));
        path = Path.GetFullPath(path);
        if (!File.Exists(Path.Combine(path, "bin", OperatingSystem.IsWindows() ? "java.exe" : "java")) ||
            !File.Exists(Path.Combine(path, "bin", OperatingSystem.IsWindows() ? "javac.exe" : "javac")))
            throw new InvalidOperationException("Choose a JDK home containing bin/java and bin/javac (on macOS, use Contents/Home).");
        var metadata = File.ReadAllText(Path.Combine(path, "release"));
        var rawVersion = Regex.Match(metadata, "(?m)^JAVA_VERSION=\"(?<value>[^\"]+)\"").Groups["value"].Value;
        var version = Regex.Match(rawVersion, @"^\d+(?:\.\d+){1,3}").Value;
        if (!Version.TryParse(version, out _))
            throw new InvalidOperationException("The JDK release file does not contain a supported Java version.");
        var vendor = Regex.Match(metadata, "(?m)^IMPLEMENTOR=\"(?<value>[^\"]+)\"").Groups["value"].Value;
        return new(path, version, vendor.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase), managed, active);
    }

    public async Task<IReadOnlyList<OpenJdkInstallation>> GetInstallationsAsync(CancellationToken cancellationToken = default)
    {
        var active = await settings.GetEffectiveJdkPathAsync();
        return await Task.Run(() =>
        {
            var candidates = new List<(string Path, bool Managed, string? Version)>();
            if (active != null) candidates.Add((active, false, null));
            if (Directory.Exists(root))
            {
                foreach (var directory in Directory.EnumerateDirectories(root, "microsoft-jdk-*"))
                {
                    var version = Path.GetFileName(directory)["microsoft-jdk-".Length..];
                    if (Version.TryParse(version, out _))
                    {
                        try { candidates.Add((FindHome(directory), true, version)); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                        { logger.LogWarning($"Ignoring incomplete managed JDK at {directory}: {ex.Message}"); }
                    }
                }
            }
            candidates.AddRange(new AndroidSdk.JdkLocator().LocateJdk().Select(j => (j.Home.FullName, false, (string?)null)));
            var installations = new List<OpenJdkInstallation>();
            var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            foreach (var group in candidates.GroupBy(c => c.Path, comparer))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidate = group.OrderByDescending(c => c.Managed).First();
                try
                {
                    var installation = Inspect(candidate.Path, candidate.Managed, comparer.Equals(candidate.Path, active));
                    if (candidate.Managed)
                    {
                        ValidateRelease(installation, candidate.Version!);
                        installation = installation with { Version = candidate.Version! };
                    }
                    installations.Add(installation);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
                { logger.LogWarning($"Ignoring unusable JDK at {candidate.Path}: {ex.Message}"); }
            }
            return (IReadOnlyList<OpenJdkInstallation>)installations.OrderByDescending(i => i.IsActive)
                .ThenByDescending(i => Version.Parse(i.Version)).ToArray();
        }, cancellationToken);
    }

    private static string FindHome(string directory)
    {
        if (File.Exists(Path.Combine(directory, "release"))) return directory;
        var homes = Directory.EnumerateFiles(directory, "release", SearchOption.AllDirectories)
            .Select(Path.GetDirectoryName).Where(p => p != null).ToArray();
        if (homes.Length != 1)
            throw new InvalidOperationException("The OpenJDK archive must contain exactly one JDK home.");
        return homes[0]!;
    }

    public Task<string> InstallAsync(int majorVersion, IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        InstallAsync(
            majorVersion,
            progress is null ? null : new Progress<OpenJdkInstallProgress>(p => { if (p.LogToOutput) progress.Report(p.Message); }),
            cancellationToken);

    public async Task<string> InstallAsync(int majorVersion, IProgress<OpenJdkInstallProgress>? progress, CancellationToken cancellationToken = default)
    {
        await installGate.WaitAsync(cancellationToken);
        string? staging = null;
        try
        {
            // Refresh at installation time rather than pinning the patch shown when the dialog opened.
            var release = (await GetReleasesAsync(true, cancellationToken)).FirstOrDefault(r => r.MajorVersion == majorVersion)
                ?? throw new InvalidOperationException($"Microsoft OpenJDK {majorVersion} is not available for this computer.");
            Directory.CreateDirectory(root);
            var target = Path.Combine(root, $"microsoft-jdk-{release.Version}");
            if (Directory.Exists(target))
            {
                var existing = FindHome(target);
                ValidateRelease(Inspect(existing), release.Version);
                await VerifyExecutablesAsync(existing, cancellationToken);
                return existing;
            }
            staging = Path.Combine(root, $".install-{Guid.NewGuid():N}");
            Directory.CreateDirectory(staging);
            var archive = Path.Combine(staging, release.DownloadUrl.AbsolutePath.EndsWith(".zip") ? "jdk.zip" : "jdk.tar.gz");
            progress?.Report(new OpenJdkInstallProgress($"Downloading Microsoft OpenJDK {release.Version}...", Percent: 0));
            await DownloadArchiveAsync(release, archive, progress, cancellationToken);
            progress?.Report(new OpenJdkInstallProgress("Verifying Microsoft's SHA-256 checksum...", Percent: 88));
            var checksum = await http.GetStringAsync(new Uri(release.DownloadUrl + ".sha256sum.txt"), cancellationToken);
            await VerifyChecksumAsync(archive, checksum, cancellationToken);
            var unpacked = Path.Combine(staging, "unpacked");
            Directory.CreateDirectory(unpacked);
            progress?.Report(new OpenJdkInstallProgress("Extracting OpenJDK...", Percent: 92));
            await Task.Run(async () =>
            {
                if (archive.EndsWith(".zip"))
                    ExtractZipWithCancellation(archive, unpacked, cancellationToken);
                else
                {
                    await using var file = File.OpenRead(archive);
                    await using var gzip = new GZipStream(file, CompressionMode.Decompress);
                    await TarFile.ExtractToDirectoryAsync(gzip, unpacked, false, cancellationToken);
                }
            }, cancellationToken);
            var home = FindHome(unpacked);
            ValidateRelease(Inspect(home), release.Version);
            await VerifyExecutablesAsync(home, cancellationToken);
            var relativeHome = Path.GetRelativePath(unpacked, home);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(unpacked, target);
            var installedHome = Path.GetFullPath(Path.Combine(target, relativeHome));
            progress?.Report(new OpenJdkInstallProgress($"Microsoft OpenJDK {release.Version} installed at {installedHome}", Percent: 100));
            logger.LogInformation($"Installed Microsoft OpenJDK {release.Version} at {installedHome}");
            return installedHome;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError("Microsoft OpenJDK installation failed.", ex);
            throw;
        }
        finally
        {
            if (staging != null && Directory.Exists(staging))
            {
                try { Directory.Delete(staging, true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { logger.LogWarning($"Could not clean OpenJDK staging folder {staging}: {ex.Message}"); }
            }
            installGate.Release();
        }
    }

    private async Task DownloadArchiveAsync(
        OpenJdkRelease release,
        string archivePath,
        IProgress<OpenJdkInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var totalBytes = response.Content.Headers.ContentLength is > 0 ? response.Content.Headers.ContentLength : null;

        // Disposing the response on cancellation aborts a stalled socket read immediately.
        using var cancelRegistration = cancellationToken.Register(() => response.Dispose());
        try
        {
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = File.Create(archivePath);

            var buffer = new byte[64 * 1024];
            long downloaded = 0;
            var lastReportedPercent = -1;
            var lastReportedAt = Environment.TickCount64;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0)
                    break;

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                downloaded += read;

                if (progress is null)
                    continue;

                var now = Environment.TickCount64;
                int? downloadPercent = totalBytes is > 0
                    ? Math.Clamp((int)(downloaded * 100L / totalBytes.Value), 0, 100)
                    : null;
                int? overallPercent = downloadPercent is { } dp
                    ? Math.Clamp((int)(dp * 85L / 100L), 0, 85)
                    : null;

                var percentChanged = downloadPercent is { } p && p != lastReportedPercent;
                var intervalElapsed = now - lastReportedAt >= 150;
                if (percentChanged || intervalElapsed || (totalBytes is > 0 && downloaded >= totalBytes.Value))
                {
                    if (downloadPercent is { } pct)
                        lastReportedPercent = pct;
                    lastReportedAt = now;

                    var status = totalBytes is > 0
                        ? $"Downloading Microsoft OpenJDK {release.Version}... {downloadPercent}% ({FormatMegabytes(downloaded)} / {FormatMegabytes(totalBytes.Value)} MB)"
                        : $"Downloading Microsoft OpenJDK {release.Version}... {FormatMegabytes(downloaded)} MB";
                    progress.Report(new OpenJdkInstallProgress(status, overallPercent, downloaded, totalBytes, LogToOutput: false));
                }
            }
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private static string FormatMegabytes(long bytes) =>
        (bytes / (1024d * 1024d)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

    private static void ExtractZipWithCancellation(string archivePath, string destinationDirectory, CancellationToken cancellationToken)
    {
        var fullDestination = Path.GetFullPath(destinationDirectory);
        var destinationWithSep = fullDestination.EndsWith(Path.DirectorySeparatorChar)
            ? fullDestination
            : fullDestination + Path.DirectorySeparatorChar;

        using var zip = ZipFile.OpenRead(archivePath);
        foreach (var entry in zip.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entryPath = Path.GetFullPath(Path.Combine(fullDestination, entry.FullName));
            if (!entryPath.StartsWith(destinationWithSep, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidDataException($"Archive entry '{entry.FullName}' points outside the destination directory.");

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(entryPath);
            }
            else
            {
                var parent = Path.GetDirectoryName(entryPath);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);
                entry.ExtractToFile(entryPath, overwrite: true);
            }
        }
    }

    private static void ValidateRelease(OpenJdkInstallation installed, string version)
    {
        var expected = Version.Parse(version);
        if (!installed.IsMicrosoft || Version.Parse(installed.Version) != expected)
            throw new InvalidOperationException($"The installation does not match Microsoft OpenJDK {version} (found {installed.Version}).");
    }

    public static async Task VerifyChecksumAsync(string archive, string checksum, CancellationToken cancellationToken = default)
    {
        var match = Regex.Match(checksum.Trim(), @"\A(?<hash>[a-fA-F0-9]{64})(?:\s|$)");
        if (!match.Success) throw new InvalidDataException("Microsoft returned an invalid SHA-256 checksum.");
        await using var stream = File.OpenRead(archive);
        var actual = await SHA256.HashDataAsync(stream, cancellationToken);
        if (!CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(match.Groups["hash"].Value)))
            throw new InvalidDataException("OpenJDK checksum verification failed. The download was not installed.");
    }

    private static async Task VerifyExecutablesAsync(string home, CancellationToken cancellationToken)
    {
        foreach (var tool in new[] { "java", "javac" })
        {
            var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(home, "bin", tool + (OperatingSystem.IsWindows() ? ".exe" : "")))
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            start.ArgumentList.Add("-version");
            using var process = System.Diagnostics.Process.Start(start) ?? throw new IOException($"Could not start {tool}.");
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); throw; }
            await Task.WhenAll(output, error);
            if (process.ExitCode != 0) throw new InvalidOperationException($"{tool} could not run: {await error}");
        }
    }

    public async Task CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        await updateGate.WaitAsync(cancellationToken);
        try
        {
            var installed = (await GetInstallationsAsync(cancellationToken)).Where(i => i.IsMicrosoft).ToArray();
            if (installed.Length == 0) AvailableUpdates = [];
            else
            {
                var available = await GetReleasesAsync(cancellationToken: cancellationToken);
                AvailableUpdates = (from installation in installed
                                    join release in available on Version.Parse(installation.Version).Major equals release.MajorVersion
                                    where IsUpdateAvailable(installation, release)
                                    select new OpenJdkUpdate(installation, release)).ToArray();
            }
            UpdateCheckError = null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            UpdateCheckError = "Could not check Microsoft OpenJDK updates. Open Manage OpenJDK to retry.";
            logger.LogWarning($"Could not check Microsoft OpenJDK updates: {ex.Message}");
        }
        finally { updateGate.Release(); UpdatesChanged?.Invoke(); }
    }

    public async Task UninstallAsync(string path, CancellationToken cancellationToken = default)
    {
        await installGate.WaitAsync(cancellationToken);
        try
        {
            var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var home = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            var active = await settings.GetEffectiveJdkPathAsync();
            if (active != null && comparer.Equals(home, Path.TrimEndingDirectorySeparator(Path.GetFullPath(active))))
                throw new InvalidOperationException("Choose another JDK before removing the active one.");
            var managedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            var folder = home;
            while (Path.GetDirectoryName(folder) is { } parent && !comparer.Equals(parent, managedRoot))
                folder = parent;
            if (!comparer.Equals(Path.GetDirectoryName(folder), managedRoot) ||
                !Path.GetFileName(folder).StartsWith("microsoft-jdk-", StringComparison.Ordinal))
                throw new InvalidOperationException("Sherpa can only remove JDKs it installed.");
            await Task.Run(() => Directory.Delete(folder, true), cancellationToken);
            logger.LogInformation($"Removed Microsoft OpenJDK at {folder}");
        }
        finally { installGate.Release(); }
        await CheckForUpdatesAsync(cancellationToken);
    }

    public static bool IsUpdateAvailable(OpenJdkInstallation installed, OpenJdkRelease available)
    {
        var current = Version.Parse(installed.Version);
        var latest = Version.Parse(available.Version);
        if (current.Major != latest.Major) return false;
        return latest > current;
    }

    public void StartUpdateChecks()
    {
        if (Screenshots.ScreenshotMode.Current.IsEnabled || Interlocked.Exchange(ref started, 1) != 0) return;
        _ = RunUpdateChecksAsync();
    }

    private async Task RunUpdateChecksAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), lifetime.Token);
            using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
            do { await CheckForUpdatesAsync(lifetime.Token); }
            while (await timer.WaitForNextTickAsync(lifetime.Token));
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { logger.LogError("OpenJDK background update checks stopped.", ex); }
    }

    public void Dispose() { lifetime.Cancel(); lifetime.Dispose(); }
}
