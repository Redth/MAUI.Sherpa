using System.Runtime.InteropServices;

namespace MauiSherpa.Workloads.Services;

/// <summary>
/// Resolves dotnetup runtime identifiers (RIDs) and builds the public aka.ms
/// download URLs used to acquire the dotnetup binary.
///
/// Mirrors the detection logic in the official get-dotnetup.sh / get-dotnetup.ps1 scripts:
/// downloads come from <c>https://aka.ms/dotnet/dotnetup/{quality}/dotnetup-{rid}[.exe]</c>
/// with a companion <c>.sha512</c> checksum file.
/// </summary>
public static class DotnetUpRuntimeIdentifier
{
    /// <summary>The default build quality. Only "daily" is published today.</summary>
    public const string DefaultQuality = "daily";

    private const string BaseUrl = "https://aka.ms/dotnet/dotnetup";

    /// <summary>The set of RIDs for which dotnetup binaries are published.</summary>
    public static readonly IReadOnlyList<string> SupportedRids = new[]
    {
        "win-x64", "win-arm64",
        "linux-x64", "linux-arm64",
        "linux-musl-x64", "linux-musl-arm64",
        "osx-x64", "osx-arm64"
    };

    /// <summary>
    /// Detects the current runtime identifier (e.g. "osx-arm64", "win-x64").
    /// On macOS this accounts for Rosetta (uname/process arch can report x64 on Apple Silicon).
    /// </summary>
    public static string DetectCurrent()
    {
        var os = DetectOs();
        var arch = DetectArch();
        return $"{os}-{arch}";
    }

    private static string DetectOs()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "win";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || OperatingSystem.IsMacCatalyst())
            return "osx";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return IsMusl() ? "linux-musl" : "linux";

        throw new PlatformNotSupportedException(
            "dotnetup is only available for Windows, macOS, and Linux.");
    }

    private static string DetectArch()
    {
        var arch = RuntimeInformation.OSArchitecture;
        return arch switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            // Under Rosetta the process can report X64; OSArchitecture already reflects the
            // emulated value, so the env-var override below covers native arm64 detection.
            _ => throw new PlatformNotSupportedException(
                $"Unsupported architecture for dotnetup: {arch}.")
        };
    }

    private static bool IsMusl()
    {
        // Heuristic mirroring the install script: presence of musl loader in /lib.
        try
        {
            if (Directory.Exists("/lib"))
            {
                foreach (var f in Directory.EnumerateFileSystemEntries("/lib", "ld-musl-*"))
                    return true;
            }
        }
        catch
        {
            // Best-effort; default to glibc.
        }
        return false;
    }

    /// <summary>
    /// True when dotnetup publishes a binary for the given RID.
    /// </summary>
    public static bool IsSupportedRid(string rid) =>
        SupportedRids.Contains(rid, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The file name of the published binary for a RID (e.g. "dotnetup-osx-arm64",
    /// "dotnetup-win-x64.exe").
    /// </summary>
    public static string GetDownloadFileName(string rid) =>
        IsWindowsRid(rid) ? $"dotnetup-{rid}.exe" : $"dotnetup-{rid}";

    /// <summary>
    /// The local file name the binary is installed as ("dotnetup" or "dotnetup.exe").
    /// </summary>
    public static string GetExecutableFileName(string rid) =>
        IsWindowsRid(rid) ? "dotnetup.exe" : "dotnetup";

    /// <summary>
    /// The aka.ms URL of the dotnetup binary for a RID and quality.
    /// </summary>
    public static string GetDownloadUrl(string rid, string? quality = null) =>
        $"{BaseUrl}/{quality ?? DefaultQuality}/{GetDownloadFileName(rid)}";

    /// <summary>
    /// The aka.ms URL of the SHA-512 checksum companion file.
    /// </summary>
    public static string GetChecksumUrl(string rid, string? quality = null) =>
        $"{GetDownloadUrl(rid, quality)}.sha512";

    private static bool IsWindowsRid(string rid) =>
        rid.StartsWith("win-", StringComparison.OrdinalIgnoreCase);
}
