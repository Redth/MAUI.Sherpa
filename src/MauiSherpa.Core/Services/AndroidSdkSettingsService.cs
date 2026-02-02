using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public class AndroidSdkSettingsService : IAndroidSdkSettingsService
{
    private const string SdkPathKey = "android_sdk_custom_path";
    
    private readonly IAndroidSdkService _sdkService;
    private readonly ISecureStorageService _storage;
    private readonly ILoggingService _logger;
    
    private string? _customSdkPath;
    private bool _initialized;

    public string? CustomSdkPath => _customSdkPath;
    
    public event Action? SdkPathChanged;

    public AndroidSdkSettingsService(
        IAndroidSdkService sdkService,
        ISecureStorageService storage,
        ILoggingService logger)
    {
        _sdkService = sdkService;
        _storage = storage;
        _logger = logger;
    }

    public async Task<string?> GetEffectiveSdkPathAsync()
    {
        await EnsureInitializedAsync();
        
        // If we have a custom path, use it
        if (!string.IsNullOrEmpty(_customSdkPath))
        {
            return _customSdkPath;
        }
        
        // Otherwise return the current SDK path (which may be auto-detected)
        return _sdkService.SdkPath;
    }

    public async Task SetCustomSdkPathAsync(string? path)
    {
        _customSdkPath = path;
        
        if (string.IsNullOrEmpty(path))
        {
            await _storage.RemoveAsync(SdkPathKey);
            _logger.LogInformation("Custom SDK path cleared");
            
            // Re-detect default
            await _sdkService.DetectSdkAsync();
        }
        else
        {
            await _storage.SetAsync(SdkPathKey, path);
            _logger.LogInformation($"Custom SDK path set to: {path}");
            
            // Apply the new path
            await _sdkService.SetSdkPathAsync(path);
        }
        
        SdkPathChanged?.Invoke();
    }

    public async Task ResetToDefaultAsync()
    {
        await SetCustomSdkPathAsync(null);
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        
        try
        {
            _customSdkPath = await _storage.GetAsync(SdkPathKey);
            
            if (!string.IsNullOrEmpty(_customSdkPath))
            {
                _logger.LogInformation($"Loading custom SDK path: {_customSdkPath}");
                await _sdkService.SetSdkPathAsync(_customSdkPath);
            }
            else
            {
                await _sdkService.DetectSdkAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to initialize SDK settings: {ex.Message}", ex);
            await _sdkService.DetectSdkAsync();
        }
        
        _initialized = true;
    }

    private async Task EnsureInitializedAsync()
    {
        if (!_initialized)
        {
            await InitializeAsync();
        }
    }
}
