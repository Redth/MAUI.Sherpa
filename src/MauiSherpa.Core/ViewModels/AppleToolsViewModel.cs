using Shiny.Mediator;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests.Apple;

namespace MauiSherpa.Core.ViewModels;

public class AppleToolsViewModel : ViewModelBase
{
    private readonly IXcodeService _xcodeService;
    private readonly IMediator _mediator;

    public string Title => "Apple Development Tools";

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

    private bool _isLoadingAvailable;
    public bool IsLoadingAvailable
    {
        get => _isLoadingAvailable;
        set => SetProperty(ref _isLoadingAvailable, value);
    }

    private bool _showBetas;
    public bool ShowBetas
    {
        get => _showBetas;
        set => SetProperty(ref _showBetas, value);
    }

    public AppleToolsViewModel(
        IXcodeService xcodeService,
        IMediator mediator,
        IAlertService alertService,
        ILoggingService loggingService)
        : base(alertService, loggingService)
    {
        _xcodeService = xcodeService;
        _mediator = mediator;
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
}
