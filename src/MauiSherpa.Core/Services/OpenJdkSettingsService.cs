using AndroidSdk;
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
            _detectedJdkPath = DetectJdkPath();
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

        _initialized = true;
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
}
