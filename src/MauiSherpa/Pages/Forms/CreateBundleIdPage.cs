using Microsoft.Maui.Controls;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Pages.Forms;

public class CreateBundleIdPage : FormPage<AppleBundleId>
{
    private readonly IAppleConnectService _appleService;

    private Entry _identifierEntry = null!;
    private Entry _nameEntry = null!;
    private Picker _platformPicker = null!;

    protected override string FormTitle => "Register Bundle ID";

    protected override bool CanSubmit =>
        !string.IsNullOrWhiteSpace(_identifierEntry?.Text) &&
        !string.IsNullOrWhiteSpace(_nameEntry?.Text) &&
        _platformPicker?.SelectedIndex >= 0;

    public CreateBundleIdPage(IAppleConnectService appleService)
    {
        _appleService = appleService;
    }

    protected override View BuildFormContent()
    {
        _identifierEntry = CreateEntry("com.example.myapp");
        _nameEntry = CreateEntry("My App");
        _platformPicker = CreatePicker(null, new[] { "iOS", "macOS" });

        return new VerticalStackLayout
        {
            Spacing = 16,
            Children =
            {
                CreateFormGroup("Bundle Identifier", _identifierEntry),
                CreateFormGroup("Name", _nameEntry),
                CreateFormGroup("Platform", _platformPicker,
                    "Mac Catalyst apps use the iOS platform. Select macOS only for native AppKit apps."),
            }
        };
    }

    protected override async Task<AppleBundleId> OnSubmitAsync()
    {
        var platform = _platformPicker.SelectedIndex == 0 ? "IOS" : "MAC_OS";
        return await _appleService.CreateBundleIdAsync(
            _identifierEntry.Text.Trim(),
            _nameEntry.Text.Trim(),
            platform);
    }
}
