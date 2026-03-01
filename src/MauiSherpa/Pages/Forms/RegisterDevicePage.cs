using Microsoft.Maui.Controls;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Pages.Forms;

public class RegisterDevicePage : FormPage<AppleDevice>
{
    private readonly IAppleConnectService _appleService;

    private Entry _udidEntry = null!;
    private Entry _nameEntry = null!;
    private Picker _platformPicker = null!;

    protected override string FormTitle => "Register Device";
    protected override string SubmitButtonText => "Register";

    protected override bool CanSubmit =>
        !string.IsNullOrWhiteSpace(_udidEntry?.Text) &&
        !string.IsNullOrWhiteSpace(_nameEntry?.Text) &&
        _platformPicker?.SelectedIndex >= 0;

    public RegisterDevicePage(IAppleConnectService appleService)
    {
        _appleService = appleService;
    }

    protected override View BuildFormContent()
    {
        _udidEntry = CreateEntry("00008030-000000000000002E");
        _nameEntry = CreateEntry("John's iPhone");
        _platformPicker = CreatePicker(null, new[] { "iOS", "macOS" });

        return new VerticalStackLayout
        {
            Spacing = 16,
            Children =
            {
                CreateFormGroup("Device UDID", _udidEntry,
                    "Find UDID in Finder (macOS) or iTunes (Windows) when device is connected"),
                CreateFormGroup("Device Name", _nameEntry),
                CreateFormGroup("Platform", _platformPicker),
            }
        };
    }

    protected override async Task<AppleDevice> OnSubmitAsync()
    {
        var platform = _platformPicker.SelectedIndex == 0 ? "IOS" : "MACOS";
        return await _appleService.RegisterDeviceAsync(
            _udidEntry.Text.Trim(),
            _nameEntry.Text.Trim(),
            platform);
    }
}
