using AndroidSdk;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public class OpenJdkSettingsService : IOpenJdkSettingsService
{
    private const string JdkPathKey = "openjdk_custom_path";

    private readonly ISecureStorageService _storage;
    private readonly ILoggingService _logger;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly string? _originalJavaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
    private readonly string? _originalJdkHome = Environment.GetEnvironmentVariable("JDK_HOME");

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

        return GetUsablePath();
    }

    public async Task SetCustomJdkPathAsync(string? path)
    {
        await EnsureInitializedAsync();
        if (!string.IsNullOrWhiteSpace(path))
        {
            if (!Path.IsPathFullyQualified(path) || !IsJdkHome(path))
                throw new ArgumentException("Choose an absolute JDK home containing bin/java and bin/javac.", nameof(path));
            path = Path.GetFullPath(path);
        }
        else path = null;

        if (string.IsNullOrEmpty(path))
        {
            await _storage.RemoveAsync(JdkPathKey);
            RestoreEnvironment();
            _logger.LogInformation("Custom JDK path cleared");
            _detectedJdkPath = DetectJdkPath();
        }
        else
        {
            await _storage.SetAsync(JdkPathKey, path);
            _logger.LogInformation($"Custom JDK path set to: {path}");
        }

        _customJdkPath = path;
        ApplyEnvironment(path ?? _detectedJdkPath);
        JdkPathChanged?.Invoke();
    }

    public async Task ResetToDefaultAsync()
    {
        await SetCustomJdkPathAsync(null);
    }

    public async Task InitializeAsync()
    {
        await _initializationGate.WaitAsync();
        try
        {
            if (_initialized) return;

            try
            {
                _customJdkPath = await _storage.GetAsync(JdkPathKey);
                if (!string.IsNullOrEmpty(_customJdkPath))
                {
                    _logger.LogInformation($"Loading custom JDK path: {_customJdkPath}");
                    if (!IsJdkHome(_customJdkPath))
                        _logger.LogWarning("The selected JDK is missing java or javac. Install or select another OpenJDK.");
                }
                else
                {
                    _detectedJdkPath = DetectJdkPath();
                    if (!string.IsNullOrEmpty(_detectedJdkPath))
                        _logger.LogInformation($"Auto-detected JDK at: {_detectedJdkPath}");
                    else
                        _logger.LogWarning("No JDK detected");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to initialize JDK settings: {ex.Message}", ex);
                _detectedJdkPath = DetectJdkPath();
            }

            ApplyEnvironment(GetUsablePath());
            _initialized = true;
        }
        finally { _initializationGate.Release(); }
    }

    private async Task EnsureInitializedAsync()
    {
        if (!_initialized)
            await InitializeAsync();
    }

    private static string? DetectJdkPath()
    {
        var locator = new JdkLocator();
        var jdks = locator.LocateJdk();
        return jdks.FirstOrDefault()?.Home?.FullName;
    }

    private static bool IsJdkHome(string path) =>
        File.Exists(Path.Combine(path, "bin", OperatingSystem.IsWindows() ? "java.exe" : "java")) &&
        File.Exists(Path.Combine(path, "bin", OperatingSystem.IsWindows() ? "javac.exe" : "javac"));

    private string? GetUsablePath()
    {
        var path = _customJdkPath ?? _detectedJdkPath;
        return path != null && IsJdkHome(path) ? path : null;
    }

    private void ApplyEnvironment(string? path)
    {
        // AndroidSdk discovers Java at tool construction. These are process-only, not shell settings.
        if (path != null)
        {
            Environment.SetEnvironmentVariable("JAVA_HOME", path);
            Environment.SetEnvironmentVariable("JDK_HOME", path);
        }
        else RestoreEnvironment();
    }

    private void RestoreEnvironment()
    {
        Environment.SetEnvironmentVariable("JAVA_HOME", _originalJavaHome);
        Environment.SetEnvironmentVariable("JDK_HOME", _originalJdkHome);
    }
}
