using System.Text.Json;
using MauiSherpa.Core.Interfaces;
using Shiny.Mediator;

namespace MauiSherpa.Core.Services;

public class AndroidSdkSettingsService : IAndroidSdkSettingsService
{
    private const string SdkPathKey = "android_sdk_custom_path";
    private const string KnownPathsKey = "android_sdk_known_paths";
    private readonly IAndroidSdkService _sdkService;
    private readonly ISecureStorageService _storage;
    private readonly ILoggingService _logger;
    private readonly IMediator? _mediator;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;
    private string? _customSdkPath;

    public string? CustomSdkPath => _customSdkPath;
    public event Action? SdkPathChanged;

    public AndroidSdkSettingsService(IAndroidSdkService sdkService, ISecureStorageService storage,
        ILoggingService logger, IMediator? mediator = null)
    {
        _sdkService = sdkService;
        _storage = storage;
        _logger = logger;
        _mediator = mediator;
    }

    public async Task<string?> GetEffectiveSdkPathAsync()
    {
        await InitializeAsync();
        return _sdkService.SdkPath;
    }

    public async Task InitializeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_initialized) return;
            var path = await _storage.GetAsync(SdkPathKey);
            if (!string.IsNullOrWhiteSpace(path))
            {
                if (!await _sdkService.SetSdkPathAsync(path))
                    throw new InvalidOperationException($"The saved Android SDK at '{path}' is unavailable or incomplete. Choose another SDK.");
                _customSdkPath = _sdkService.SdkPath;
            }
            else
                await _sdkService.DetectSdkAsync();
            _initialized = true;
        }
        finally { _gate.Release(); }
    }

    public async Task SetCustomSdkPathAsync(string? path)
    {
        await _gate.WaitAsync();
        string? previousCustom = null;
        try
        {
            previousCustom = _initialized ? _customSdkPath : await _storage.GetAsync(SdkPathKey);
            if (_sdkService.IsBusy)
                throw new InvalidOperationException("Wait for the current Android SDK operation to finish before switching SDKs.");
            var reset = string.IsNullOrWhiteSpace(path);
            path = reset ? null : AndroidSdkLocations.Normalize(path!);
            if (path == null)
                path = await _sdkService.GetDefaultSdkPathAsync();
            if (path == null || AndroidSdkLocations.Inspect(path).Status != AndroidSdkLocationStatus.Installed)
                throw new InvalidOperationException("No usable Android SDK was found at this location. Choose a folder containing Android command-line tools.");
            try
            {
                if (reset)
                    await _storage.RemoveAsync(SdkPathKey);
                else
                    await _storage.SetAsync(SdkPathKey, path);
                if (!await _sdkService.SetSdkPathAsync(path))
                    throw new InvalidOperationException("Could not activate the selected Android SDK. The previous SDK remains active.");
            }
            catch
            {
                if (previousCustom == null) await _storage.RemoveAsync(SdkPathKey);
                else await _storage.SetAsync(SdkPathKey, previousCustom);
                _customSdkPath = previousCustom;
                throw;
            }

            _customSdkPath = reset ? null : _sdkService.SdkPath;
            _initialized = true;
            _logger.LogInformation($"Active Android SDK: {_sdkService.SdkPath}");
            await FlushSdkCachesAsync();
            SdkPathChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Could not switch Android SDK: {ex.Message}", ex);
            throw;
        }
        finally { _gate.Release(); }
    }

    public Task ResetToDefaultAsync() => SetCustomSdkPathAsync(null);

    public async Task<IReadOnlyList<AndroidSdkLocation>> GetKnownLocationsAsync()
    {
        // Discovery must remain available when a persisted SDK has been removed.
        var saved = await _storage.GetAsync(SdkPathKey);
        var historyJson = await _storage.GetAsync(KnownPathsKey);
        var history = historyJson == null ? [] : JsonSerializer.Deserialize<string[]>(historyJson) ?? [];
        var candidates = new List<(string Path, string Label)>(AndroidSdkLocations.GetCommonPaths());
        foreach (var variable in new[] { "ANDROID_HOME", "ANDROID_SDK_ROOT" })
            if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value)
                candidates.Add((value, variable));
        if (_sdkService.SdkPath is { } active)
            candidates.Add((active, "Current SDK"));
        if (!string.IsNullOrWhiteSpace(saved))
            candidates.Add((saved, "Selected SDK"));
        candidates.AddRange(history.Select(p => (p, "Previously used")));
        return await Task.Run(() => candidates
            .Select(c => AndroidSdkLocations.Inspect(c.Path, c.Label, _sdkService.SdkPath))
            .GroupBy(l => l.Path, AndroidSdkLocations.PathComparer)
            .Select(g => g.First())
            .ToArray());
    }

    public async Task RememberLocationAsync(string path)
    {
        await _gate.WaitAsync();
        try
        {
            path = AndroidSdkLocations.Normalize(path);
            var json = await _storage.GetAsync(KnownPathsKey);
            var history = json == null ? [] : JsonSerializer.Deserialize<string[]>(json) ?? [];
            await _storage.SetAsync(KnownPathsKey, JsonSerializer.Serialize(
                new[] { path }.Concat(history).Distinct(AndroidSdkLocations.PathComparer).Take(20)));
        }
        finally { _gate.Release(); }
    }

    private async Task FlushSdkCachesAsync()
    {
        if (_mediator == null) return;
        foreach (var key in new[] { "android:sdkpath", "android:packages:installed", "android:packages:available",
            "android:devices", "android:emulators", "android:systemimages", "android:devicedefs", "android:skins" })
            await _mediator.FlushStores(key);
    }
}
