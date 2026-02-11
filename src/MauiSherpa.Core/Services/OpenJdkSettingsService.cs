using System.Diagnostics;
using System.Runtime.InteropServices;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public class OpenJdkSettingsService : IOpenJdkSettingsService
{
    private const string JdkPathKey = "openjdk_custom_path";

    private readonly ISecureStorageService _storage;
    private readonly ILoggingService _logger;

    private string? _customJdkPath;
    private string? _detectedJdkPath;
    private bool _initialized;

    public string? CustomJdkPath => _customJdkPath;

    public event Action? JdkPathChanged;

    public OpenJdkSettingsService(
        ISecureStorageService storage,
        ILoggingService logger)
    {
        _storage = storage;
        _logger = logger;
    }

    public async Task<string?> GetEffectiveJdkPathAsync()
    {
        await EnsureInitializedAsync();

        if (!string.IsNullOrEmpty(_customJdkPath))
            return _customJdkPath;

        return _detectedJdkPath;
    }

    public async Task SetCustomJdkPathAsync(string? path)
    {
        _customJdkPath = path;

        if (string.IsNullOrEmpty(path))
        {
            await _storage.RemoveAsync(JdkPathKey);
            _logger.LogInformation("Custom JDK path cleared");
            _detectedJdkPath = await DetectJdkPathAsync();
        }
        else
        {
            await _storage.SetAsync(JdkPathKey, path);
            _logger.LogInformation($"Custom JDK path set to: {path}");
        }

        JdkPathChanged?.Invoke();
    }

    public async Task ResetToDefaultAsync()
    {
        await SetCustomJdkPathAsync(null);
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        try
        {
            _customJdkPath = await _storage.GetAsync(JdkPathKey);

            if (!string.IsNullOrEmpty(_customJdkPath))
            {
                _logger.LogInformation($"Loading custom JDK path: {_customJdkPath}");
            }
            else
            {
                _detectedJdkPath = await DetectJdkPathAsync();
                if (!string.IsNullOrEmpty(_detectedJdkPath))
                    _logger.LogInformation($"Auto-detected JDK at: {_detectedJdkPath}");
                else
                    _logger.LogWarning("No JDK detected");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to initialize JDK settings: {ex.Message}", ex);
            _detectedJdkPath = await DetectJdkPathAsync();
        }

        _initialized = true;
    }

    private async Task EnsureInitializedAsync()
    {
        if (!_initialized)
            await InitializeAsync();
    }

    private async Task<string?> DetectJdkPathAsync()
    {
        // Check JAVA_HOME first
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(javaHome) && await ValidateJdkPathAsync(javaHome))
            return javaHome;

        // Check common paths
        foreach (var path in GetCommonJdkPaths())
        {
            if (await ValidateJdkPathAsync(path))
                return path;
        }

        return null;
    }

    private async Task<bool> ValidateJdkPathAsync(string jdkPath)
    {
        try
        {
            var javaExe = Path.Combine(jdkPath, "bin",
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "java.exe" : "java");

            if (!File.Exists(javaExe))
                return false;

            var psi = new ProcessStartInfo
            {
                FileName = javaExe,
                Arguments = "-version",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<string> GetCommonJdkPaths()
    {
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
        {
            // Microsoft OpenJDK (installed by dotnet workloads)
            var jvmDir = "/Library/Java/JavaVirtualMachines";
            if (Directory.Exists(jvmDir))
            {
                foreach (var dir in Directory.GetDirectories(jvmDir).OrderByDescending(d => d))
                {
                    var home = Path.Combine(dir, "Contents", "Home");
                    if (Directory.Exists(home))
                        yield return home;
                }
            }

            // Homebrew
            var homebrewPaths = new[]
            {
                "/opt/homebrew/opt/openjdk/libexec/openjdk.jdk/Contents/Home",
                "/opt/homebrew/opt/openjdk@17/libexec/openjdk.jdk/Contents/Home",
                "/opt/homebrew/opt/openjdk@21/libexec/openjdk.jdk/Contents/Home",
                "/usr/local/opt/openjdk/libexec/openjdk.jdk/Contents/Home",
            };
            foreach (var p in homebrewPaths)
                if (Directory.Exists(p))
                    yield return p;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            // Microsoft OpenJDK
            var msJdkDir = Path.Combine(programFiles, "Microsoft");
            if (Directory.Exists(msJdkDir))
            {
                foreach (var dir in Directory.GetDirectories(msJdkDir, "jdk-*").OrderByDescending(d => d))
                    yield return dir;
            }

            // Eclipse Adoptium / Temurin
            foreach (var dir in Directory.GetDirectories(programFiles, "Eclipse Adoptium*"))
            {
                foreach (var jdk in Directory.GetDirectories(dir, "jdk-*").OrderByDescending(d => d))
                    yield return jdk;
            }

            // Oracle JDK
            var oracleDir = Path.Combine(programFiles, "Java");
            if (Directory.Exists(oracleDir))
            {
                foreach (var dir in Directory.GetDirectories(oracleDir, "jdk*").OrderByDescending(d => d))
                    yield return dir;
            }
        }
        else // Linux
        {
            var linuxPaths = new[]
            {
                "/usr/lib/jvm/java-21-openjdk",
                "/usr/lib/jvm/java-17-openjdk",
                "/usr/lib/jvm/java-11-openjdk",
                "/usr/lib/jvm/default-java",
            };
            foreach (var p in linuxPaths)
                if (Directory.Exists(p))
                    yield return p;
        }
    }
}
