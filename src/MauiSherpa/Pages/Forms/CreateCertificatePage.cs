using Microsoft.Maui.Controls;
#if MACOSAPP
using Microsoft.Maui.Platforms.MacOS.Platform;
#endif
#if LINUXGTK
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Forms;

public record CreateCertificateFormResult(
    string CertificateType,
    bool SaveToDisk,
    string? Passphrase);

public class CreateCertificatePage : FormPage<CreateCertificateFormResult>
{
    private sealed record CertificateTypeOption(string Label, string Value);

    private Picker _typePicker = null!;
    private CheckBox _saveToDiskCheckBox = null!;
    private Entry _passphraseEntry = null!;

    private readonly bool _requireDiskExport;

    private static readonly CertificateTypeOption[] CertificateTypes =
    {
        new("Apple Development", "DEVELOPMENT"),
        new("Apple Distribution", "DISTRIBUTION"),
        new("iOS App Development", "IOS_DEVELOPMENT"),
        new("iOS Distribution (App Store Connect and Ad Hoc)", "IOS_DISTRIBUTION"),
        new("Mac Development", "MAC_APP_DEVELOPMENT"),
        new("Mac App Distribution", "MAC_APP_DISTRIBUTION"),
        new("Mac Installer Distribution", "MAC_INSTALLER_DISTRIBUTION"),
        new("Developer ID Application", "DEVELOPER_ID_APPLICATION"),
        new("Developer ID Kernel Extension", "DEVELOPER_ID_KEXT"),
        new("Pass Type ID", "PASS_TYPE_ID"),
        new("Pass Type ID with NFC", "PASS_TYPE_ID_WITH_NFC"),
    };

    protected override string FormTitle => "Create Certificate";
    protected override double FormBodyHeightRequest => 440;

    protected override bool CanSubmit => _typePicker?.SelectedIndex >= 0;

    public CreateCertificatePage(bool requireDiskExport)
    {
        _requireDiskExport = requireDiskExport;

#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, false);
        MacOSPage.SetModalSheetWidth(this, 620);
        MacOSPage.SetModalSheetHeight(this, 640);
#elif LINUXGTK
        GtkPage.SetModalSizesToContent(this, false);
        GtkPage.SetModalWidth(this, 620);
        GtkPage.SetModalHeight(this, 640);
#endif
    }

    protected override View BuildFormContent()
    {
        _typePicker = CreatePicker(null, CertificateTypes.Select(type => type.Label).ToList());
        _typePicker.SelectedIndex = 0;

        _saveToDiskCheckBox = new CheckBox
        {
            IsChecked = _requireDiskExport,
            IsEnabled = !_requireDiskExport,
            VerticalOptions = LayoutOptions.Center,
        };
        _saveToDiskCheckBox.SetDynamicResource(CheckBox.ColorProperty, FormTheme.AccentPrimary);

        var saveToDiskLabel = new Label
        {
            Text = "Save a copy to disk",
            FontSize = 14,
            VerticalOptions = LayoutOptions.Center,
        };
        saveToDiskLabel.SetDynamicResource(Label.TextColorProperty, FormTheme.TextPrimary);

        var saveToDiskRow = new HorizontalStackLayout
        {
            Spacing = 8,
            Children = { _saveToDiskCheckBox, saveToDiskLabel },
        };

        _passphraseEntry = CreatePasswordEntry("Optional; leave empty for an unprotected P12");
        var passphraseGroup = CreateFormGroup(
            "P12 Passphrase",
            _passphraseEntry,
            "Only used for the copy saved to disk");
        passphraseGroup.IsVisible = _saveToDiskCheckBox.IsChecked;
        _saveToDiskCheckBox.CheckedChanged += (_, args) =>
            passphraseGroup.IsVisible = args.Value;

        var storageHelp = _requireDiskExport
            ? "Required because this platform does not provide a supported certificate store"
            : "The certificate is installed in the platform certificate store and synced to default providers";

        return new VerticalStackLayout
        {
            Spacing = 16,
            Children =
            {
                CreateFormGroup("Certificate Type", _typePicker),
                CreateFormGroup("Certificate Storage", saveToDiskRow, storageHelp),
                passphraseGroup,
            }
        };
    }

    protected override Task<CreateCertificateFormResult> OnSubmitAsync()
    {
        var certType = CertificateTypes[_typePicker.SelectedIndex].Value;
        var saveToDisk = _saveToDiskCheckBox.IsChecked;
        var passphrase = saveToDisk ? _passphraseEntry.Text ?? string.Empty : null;

        return Task.FromResult(new CreateCertificateFormResult(
            certType,
            saveToDisk,
            passphrase));
    }
}
