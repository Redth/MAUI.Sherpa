using Shiny.Mediator;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests.Apple;

namespace MauiSherpa.Core.ViewModels;

public class XcodeManagementViewModel : ViewModelBase
{
    private readonly IXcodeService _xcodeService;
    private readonly IMediator _mediator;
    private readonly IAppleDownloadAuthService _authService;

    public string Title => "Xcode Management";

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

    private IReadOnlyList<XcodeInstallation> _installedXcodes = [];
    public IReadOnlyList<XcodeInstallation> InstalledXcodes
    {
        get => _installedXcodes;
        set => SetProperty(ref _installedXcodes, value);
    }

    private IReadOnlyList<XcodeRelease> _availableReleases = [];
    public IReadOnlyList<XcodeRelease> AvailableReleases
    {
        get => _availableReleases;
        set => SetProperty(ref _availableReleases, value);
    }

    private IReadOnlyList<SimulatorRuntimeStorage> _runtimeStorage = [];
    public IReadOnlyList<SimulatorRuntimeStorage> RuntimeStorage
    {
        get => _runtimeStorage;
        set => SetProperty(ref _runtimeStorage, value);
    }

    private bool _isLoadingAvailable;
    public bool IsLoadingAvailable
    {
        get => _isLoadingAvailable;
        set => SetProperty(ref _isLoadingAvailable, value);
    }

    private bool _isLoadingRuntimes;
    public bool IsLoadingRuntimes
    {
        get => _isLoadingRuntimes;
        set => SetProperty(ref _isLoadingRuntimes, value);
    }

    private bool _showBetas;
    public bool ShowBetas
    {
        get => _showBetas;
        set => SetProperty(ref _showBetas, value);
    }

    public bool IsAuthenticated => _authService.IsAuthenticated;
    public string? AuthenticatedAppleId => _authService.CurrentAppleId;

    public XcodeManagementViewModel(
        IXcodeService xcodeService,
        IMediator mediator,
        IAppleDownloadAuthService authService,
        IAlertService alertService,
        ILoggingService loggingService)
        : base(alertService, loggingService)
    {
        _xcodeService = xcodeService;
        _mediator = mediator;
        _authService = authService;
        _authService.AuthStateChanged += () => OnPropertyChanged(nameof(IsAuthenticated));
    }

    public async Task LoadInstalledAsync()
    {
        IsLoading = true;
        StatusMessage = "Discovering installed Xcode versions...";

        try
        {
            var result = await _mediator.Request(new GetInstalledXcodesRequest());
            InstalledXcodes = result.Result;
            StatusMessage = InstalledXcodes.Count > 0
                ? $"Found {InstalledXcodes.Count} Xcode installation(s)"
                : "No Xcode installations found";
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to load installed Xcodes: {ex.Message}", ex);
            StatusMessage = "Failed to discover Xcode installations";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task LoadAvailableAsync()
    {
        IsLoadingAvailable = true;

        try
        {
            var result = await _mediator.Request(new GetAvailableXcodesRequest());
            AvailableReleases = result.Result;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to load available releases: {ex.Message}", ex);
        }
        finally
        {
            IsLoadingAvailable = false;
        }
    }

    public async Task LoadRuntimeStorageAsync()
    {
        IsLoadingRuntimes = true;

        try
        {
            var result = await _mediator.Request(new GetRuntimeStorageRequest());
            RuntimeStorage = result.Result;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to load runtime storage: {ex.Message}", ex);
        }
        finally
        {
            IsLoadingRuntimes = false;
        }
    }

    public async Task SelectXcodeAsync(XcodeInstallation xcode)
    {
        if (xcode.IsSelected) return;

        var confirmed = await AlertService.ShowConfirmAsync(
            "Switch Active Xcode",
            $"Switch active Xcode to {Path.GetFileName(xcode.Path)} (v{xcode.Version})?\n\nThis requires administrator privileges.",
            "Switch",
            "Cancel");

        if (!confirmed) return;

        StatusMessage = $"Switching to Xcode {xcode.Version}...";
        var success = await _xcodeService.SelectXcodeAsync(xcode.Path);

        if (success)
        {
            await AlertService.ShowToastAsync($"Switched to Xcode {xcode.Version}");
            await _mediator.FlushStores("apple:xcode:installed");
            await LoadInstalledAsync();
        }
        else
        {
            await AlertService.ShowAlertAsync("Error", "Failed to switch Xcode. The operation may have been cancelled.");
        }
    }

    public async Task UninstallXcodeAsync(XcodeInstallation xcode)
    {
        if (xcode.IsSelected)
        {
            await AlertService.ShowAlertAsync("Cannot Uninstall", "Cannot uninstall the currently active Xcode. Switch to a different version first.");
            return;
        }

        var confirmed = await AlertService.ShowConfirmAsync(
            "Uninstall Xcode",
            $"Move Xcode {xcode.Version} ({Path.GetFileName(xcode.Path)}) to the Trash?\n\nYou can restore it from the Trash if needed.",
            "Uninstall",
            "Cancel");

        if (!confirmed) return;

        StatusMessage = $"Uninstalling Xcode {xcode.Version}...";
        var success = await _xcodeService.UninstallXcodeAsync(xcode.Path);

        if (success)
        {
            await AlertService.ShowToastAsync($"Xcode {xcode.Version} moved to Trash");
            await _mediator.FlushStores("apple:xcode:installed");
            await LoadInstalledAsync();
        }
        else
        {
            await AlertService.ShowAlertAsync("Error", "Failed to uninstall Xcode. The operation may have been cancelled.");
        }
    }

    public async Task<AppleAuthResult> SignInAsync(string appleId, string password)
    {
        StatusMessage = "Authenticating with Apple Developer...";
        var result = await _authService.AuthenticateAsync(appleId, password);

        if (result.Success)
            StatusMessage = $"Signed in as {appleId}";
        else if (result.RequiresTwoFactor)
            StatusMessage = "Two-factor authentication required";
        else
            StatusMessage = result.ErrorMessage ?? "Authentication failed";

        return result;
    }

    public async Task<AppleAuthResult> SubmitTwoFactorCodeAsync(string code, TwoFactorMethod? method = null)
    {
        StatusMessage = "Verifying security code...";
        var result = await _authService.SubmitTwoFactorCodeAsync(code, method);

        if (result.Success)
            StatusMessage = $"Signed in as {_authService.CurrentAppleId}";
        else
            StatusMessage = result.ErrorMessage ?? "Verification failed";

        return result;
    }

    public async Task SignOutAsync()
    {
        await _authService.SignOutAsync();
        StatusMessage = "Signed out";
    }

    public IReadOnlyList<XcodeRelease> FilteredAvailableReleases
    {
        get
        {
            if (ShowBetas) return AvailableReleases;
            return AvailableReleases.Where(r => !r.IsBeta).ToList();
        }
    }

    public bool IsInstalled(XcodeRelease release)
    {
        return InstalledXcodes.Any(i =>
            i.Version == release.Version || i.BuildNumber == release.BuildNumber);
    }

    public long TotalRuntimeStorageBytes => RuntimeStorage
        .Where(r => r.SizeBytes.HasValue)
        .Sum(r => r.SizeBytes!.Value);
}
