using Microsoft.Maui.Controls;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Pages.Forms;

public record RegisterDeviceFormResult(AppleDevice Device, bool AddToProfiles);

public class RegisterDevicePage : FormPage<RegisterDeviceFormResult>
{
    private readonly IAppleConnectService _appleService;

    private Entry _udidEntry = null!;
    private Entry _nameEntry = null!;
    private Picker _platformPicker = null!;
    private CheckBox _addToProfilesCheckBox = null!;

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
        _platformPicker.SelectedIndex = 0;

        _addToProfilesCheckBox = new CheckBox
        {
            IsChecked = true,
            VerticalOptions = LayoutOptions.Center,
        };
        _addToProfilesCheckBox.SetDynamicResource(CheckBox.ColorProperty, FormTheme.AccentPrimary);

        var addToProfilesLabel = new Label
        {
            Text = "Add to provisioning profiles",
            FontSize = 14,
            VerticalOptions = LayoutOptions.Center,
        };
        addToProfilesLabel.SetDynamicResource(Label.TextColorProperty, FormTheme.TextPrimary);

        var addToProfilesRow = new HorizontalStackLayout
        {
            Spacing = 8,
            Children = { _addToProfilesCheckBox, addToProfilesLabel },
        };

        return new VerticalStackLayout
        {
            Spacing = 16,
            Children =
            {
                CreateFormGroup("Device UDID", _udidEntry,
                    "Find UDID in Finder (macOS) or iTunes (Windows) when device is connected"),
                CreateFormGroup("Device Name", _nameEntry),
                CreateFormGroup("Platform", _platformPicker),
                CreateFormGroup("Provisioning Profiles", addToProfilesRow,
                    "After registering, choose which development and ad hoc profiles to recreate with this device"),
            }
        };
    }

    protected override async Task<RegisterDeviceFormResult> OnSubmitAsync()
    {
        var platform = _platformPicker.SelectedIndex == 0 ? "IOS" : "MACOS";
        var device = await _appleService.RegisterDeviceAsync(
            _udidEntry.Text.Trim(),
            _nameEntry.Text.Trim(),
            platform);
        return new RegisterDeviceFormResult(device, _addToProfilesCheckBox.IsChecked);
    }
}
