using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests.Android;
using Shiny.Mediator;

namespace MauiSherpa.Core.ViewModels;

public class AndroidSdkViewModel : ViewModelBase
{
    private readonly IAndroidSdkService _sdkService;
    private readonly IAndroidSdkSettingsService _sdkSettings;
    private readonly IMediator _mediator;

    public string Title => "Android SDK Management";

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    private string _statusMessage = "Ready";
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    private string? _sdkPath;
    public string? SdkPath
    {
        get => _sdkPath;
        set => SetProperty(ref _sdkPath, value);
    }

    private bool _isSdkInstalled;
    public bool IsSdkInstalled
    {
        get => _isSdkInstalled;
        set => SetProperty(ref _isSdkInstalled, value);
    }

    private IReadOnlyList<SdkPackageInfo> _installedPackages = [];
    public IReadOnlyList<SdkPackageInfo> InstalledPackages
    {
        get => _installedPackages;
        set => SetProperty(ref _installedPackages, value);
    }

    private IReadOnlyList<SdkPackageInfo> _availablePackages = [];
    public IReadOnlyList<SdkPackageInfo> AvailablePackages
    {
        get => _availablePackages;
        set => SetProperty(ref _availablePackages, value);
    }

    private IReadOnlyList<DeviceInfo> _devices = [];
    public IReadOnlyList<DeviceInfo> Devices
    {
        get => _devices;
        set => SetProperty(ref _devices, value);
    }

    private string _searchFilter = string.Empty;
    public string SearchFilter
    {
        get => _searchFilter;
        set
        {
            if (SetProperty(ref _searchFilter, value))
                OnPropertyChanged(nameof(FilteredAvailablePackages));
        }
    }

    public IReadOnlyList<SdkPackageInfo> FilteredAvailablePackages =>
        string.IsNullOrWhiteSpace(SearchFilter)
            ? AvailablePackages
            : AvailablePackages
                .Where(p => p.Path.Contains(SearchFilter, StringComparison.OrdinalIgnoreCase) ||
                           p.Description.Contains(SearchFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();

    public AndroidSdkViewModel(
        IAndroidSdkService sdkService,
        IAndroidSdkSettingsService sdkSettings,
        IMediator mediator,
        IAlertService alertService,
        ILoggingService loggingService)
        : base(alertService, loggingService)
    {
        _sdkService = sdkService;
        _sdkSettings = sdkSettings;
        _mediator = mediator;
    }

    public override async Task InitializeAsync()
    {
        await DetectSdkAsync();
    }

    public async Task DetectSdkAsync(bool forceRefresh = false)
    {
        IsLoading = true;
        StatusMessage = "Detecting Android SDK...";

        try
        {
            if (forceRefresh)
            {
                await _mediator.FlushStores("android:sdkpath");
            }
            
            var (_, sdkPath) = await _mediator.Request(new GetSdkPathRequest());
            
            SdkPath = sdkPath;
            IsSdkInstalled = !string.IsNullOrEmpty(sdkPath);

            if (IsSdkInstalled)
            {
                StatusMessage = $"SDK found at: {SdkPath}";
                await RefreshPackagesAsync(forceRefresh);
            }
            else
            {
                StatusMessage = "Android SDK not found. Click 'Install SDK' to download.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            Logger.LogError("Failed to detect SDK", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task RefreshPackagesAsync(bool forceRefresh = false)
    {
        if (!IsSdkInstalled) return;

        IsLoading = true;
        StatusMessage = "Loading packages...";

        try
        {
            if (forceRefresh)
            {
                await _mediator.FlushStores("android:packages:installed");
                await _mediator.FlushStores("android:packages:available");
                await _mediator.FlushStores("android:devices");
            }
            
            // Run requests in parallel for faster loading
            var installedTask = _mediator.Request(new GetInstalledPackagesRequest());
            var availableTask = _mediator.Request(new GetAvailablePackagesRequest());
            var devicesTask = _mediator.Request(new GetAndroidDevicesRequest());
            
            await Task.WhenAll(installedTask, availableTask, devicesTask);
            
            var (_, installed) = await installedTask;
            var (_, available) = await availableTask;
            var (_, devices) = await devicesTask;
            
            InstalledPackages = installed;
            AvailablePackages = available;
            Devices = devices;
            
            StatusMessage = $"Loaded {InstalledPackages.Count} installed, {AvailablePackages.Count} available packages";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading packages: {ex.Message}";
            Logger.LogError("Failed to refresh packages", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task InstallPackageAsync(string packagePath)
    {
        IsLoading = true;
        var progress = new Progress<string>(msg => StatusMessage = msg);

        try
        {
            var success = await _sdkService.InstallPackageAsync(packagePath, progress);
            if (success)
            {
                await AlertService.ShowToastAsync($"Installed {packagePath}");
                await RefreshPackagesAsync(forceRefresh: true);
            }
            else
            {
                await AlertService.ShowAlertAsync("Installation Failed", $"Could not install {packagePath}");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            Logger.LogError($"Failed to install {packagePath}", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task UninstallPackageAsync(string packagePath)
    {
        var confirm = await AlertService.ShowConfirmAsync(
            "Uninstall Package",
            $"Are you sure you want to uninstall {packagePath}?");

        if (!confirm) return;

        IsLoading = true;
        StatusMessage = $"Uninstalling {packagePath}...";

        try
        {
            var success = await _sdkService.UninstallPackageAsync(packagePath);
            if (success)
            {
                await AlertService.ShowToastAsync($"Uninstalled {packagePath}");
                await RefreshPackagesAsync(forceRefresh: true);
            }
            else
            {
                await AlertService.ShowAlertAsync("Uninstall Failed", $"Could not uninstall {packagePath}");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            Logger.LogError($"Failed to uninstall {packagePath}", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task AcquireSdkAsync()
    {
        IsLoading = true;
        var progress = new Progress<string>(msg => StatusMessage = msg);

        try
        {
            var success = await _sdkService.AcquireSdkAsync(progress: progress);
            if (success)
            {
                IsSdkInstalled = true;
                SdkPath = _sdkService.SdkPath;
                await AlertService.ShowToastAsync("Android SDK installed successfully!");
                await RefreshPackagesAsync(forceRefresh: true);
            }
            else
            {
                await AlertService.ShowAlertAsync("Installation Failed", "Could not download Android SDK");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            Logger.LogError("Failed to acquire SDK", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }
}
