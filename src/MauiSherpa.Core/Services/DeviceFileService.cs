using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public class DeviceFileService : IDeviceFileService
{
    private readonly IAndroidSdkService _sdkService;
    private readonly ILoggingService _logger;

    public DeviceFileService(IAndroidSdkService sdkService, ILoggingService logger)
    {
        _sdkService = sdkService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DeviceFileEntry>> ListAsync(string serial, string path, CancellationToken ct = default)
    {
        // Always append trailing / so ls lists directory contents (not the symlink itself)
        var listPath = path.TrimEnd('/') + "/";
        var output = await RunAdbAsync(serial, $"shell ls -la \"{listPath}\"", ct);
        if (string.IsNullOrEmpty(output))
            return Array.Empty<DeviceFileEntry>();

        var entries = new List<DeviceFileEntry>();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var entry = ParseLsLine(line.Trim(), path);
            if (entry != null && entry.Name != "." && entry.Name != "..")
                entries.Add(entry);
        }

        return entries
            .OrderByDescending(e => e.IsDirectory)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();
    }

    public async Task PullAsync(string serial, string remotePath, string localPath, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        progress?.Report($"Pulling {Path.GetFileName(remotePath)}...");
        await RunAdbAsync(serial, $"pull \"{remotePath}\" \"{localPath}\"", ct);
        progress?.Report("Done.");
    }

    public async Task PushAsync(string serial, string localPath, string remotePath, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        progress?.Report($"Pushing {Path.GetFileName(localPath)}...");
        await RunAdbAsync(serial, $"push \"{localPath}\" \"{remotePath}\"", ct);
        progress?.Report("Done.");
    }

    public async Task DeleteAsync(string serial, string remotePath, CancellationToken ct = default)
    {
        await RunAdbAsync(serial, $"shell rm -rf \"{remotePath}\"", ct);
    }

    private async Task<string> RunAdbAsync(string serial, string arguments, CancellationToken ct)
    {
        var adbPath = GetAdbPath();
        if (adbPath == null) return string.Empty;

        var psi = new ProcessStartInfo
        {
            FileName = adbPath,
            Arguments = $"-s {serial} {arguments}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return string.Empty;

        var output = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return output;
    }

    private string? GetAdbPath()
    {
        var sdkPath = _sdkService.SdkPath;
        if (string.IsNullOrEmpty(sdkPath)) return null;
        return Path.Combine(sdkPath, "platform-tools", "adb");
    }

    // Parse a line from `ls -la` output
    // Format: drwxrwxr-x  2 root root  4096 2024-01-15 10:30 dirname
    // Also handles: drwxrws--x 78 media_rw ext_data_rw 8192 2026-01-08 10:33 data
    private static readonly Regex LsRegex = new(
        @"^([drwxlsStT\-]{10})\s+\d+\s+\S+\s+\S+\s+(\d+)\s+(\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2})\s+(.+)$",
        RegexOptions.Compiled);

    // Fallback for lines with ? permissions (no read access)
    // e.g.: d?????????   ? ?      ?             ?                ? data_mirror
    private static readonly Regex LsFallbackRegex = new(
        @"^([dl\-\?]{1}[\?rwxsStT\-]{9})\s+.*\s(\S+?)(\s*->\s*\S+)?$",
        RegexOptions.Compiled);

    private static DeviceFileEntry? ParseLsLine(string line, string parentPath)
    {
        // Skip "total N" header line
        if (line.StartsWith("total ", StringComparison.OrdinalIgnoreCase))
            return null;

        var match = LsRegex.Match(line);
        if (match.Success)
        {
            var perms = match.Groups[1].Value;
            var size = long.TryParse(match.Groups[2].Value, out var s) ? s : 0;
            var dateStr = match.Groups[3].Value;
            var name = match.Groups[4].Value;

            // Handle symlinks: name -> target
            var isSymlink = perms.StartsWith('l');
            if (isSymlink)
            {
                var arrowIdx = name.IndexOf(" -> ", StringComparison.Ordinal);
                if (arrowIdx > 0)
                    name = name[..arrowIdx];
            }

            // Directories start with 'd'; symlinks need special handling
            var isDir = perms.StartsWith('d');
            // For symlinks, check if size is small (typical for dir symlinks)
            // We can't reliably tell if a symlink points to a dir without stat
            if (isSymlink)
                isDir = true; // Treat symlinks as navigable (user can click to try)

            DateTimeOffset? modified = null;
            if (DateTimeOffset.TryParseExact(dateStr, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                modified = dt;

            var fullPath = parentPath.TrimEnd('/') + "/" + name;
            return new DeviceFileEntry(name, fullPath, isDir, size, perms, modified);
        }

        // Fallback: parse lines with ? characters (no access)
        var fallback = LsFallbackRegex.Match(line);
        if (fallback.Success)
        {
            var perms = fallback.Groups[1].Value;
            var name = fallback.Groups[2].Value;

            if (name == "." || name == ".." || name == "?") return null;

            var isDir = perms[0] == 'd' || perms[0] == 'l';
            var fullPath = parentPath.TrimEnd('/') + "/" + name;
            return new DeviceFileEntry(name, fullPath, isDir, 0, perms, null);
        }

        return null;
    }
}
