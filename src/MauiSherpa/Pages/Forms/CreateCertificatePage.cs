using Microsoft.Maui.Controls;
using MauiSherpa.Core.Services;
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
    private Picker _typePicker = null!;
    private CheckBox _saveToDiskCheckBox = null!;
    private Entry _passphraseEntry = null!;

    private readonly bool _requireDiskExport;

    private AppleCertificateTypeOption SelectedType => AppleCertificateTypes.All[_typePicker.SelectedIndex];

    protected override string FormTitle => "Create Certificate";
    protected override string SubmitButtonText =>
        _typePicker?.SelectedIndex >= 0 && !SelectedType.CanCreateInApp
            ? "Open Apple Developer"
            : "Create";
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
        _typePicker = CreatePicker(null, AppleCertificateTypes.All.Select(type => type.PickerLabel).ToList());
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

        var storageGroup = CreateFormGroup("Certificate Storage", saveToDiskRow, storageHelp);
        var description = new Label { FontSize = 13 };
        description.SetDynamicResource(Label.TextColorProperty, FormTheme.TextSecondary);
        var portalHelp = new Label { FontSize = 13 };
        portalHelp.SetDynamicResource(Label.TextColorProperty, FormTheme.TextPrimary);

        void UpdateSelection()
        {
            if (_typePicker.SelectedIndex < 0)
            {
                UpdateSubmitEnabled();
                return;
            }

            var type = SelectedType;
            description.Text = $"{type.Category}: {type.Description}";
            portalHelp.Text = type.CanCreateInApp ? null :
                $"{type.PortalReason}\n\nOpen Apple Developer, sign in with the intended team, and select \"{type.Name}\". " +
                "Sherpa can generate a certificate request and securely retain its private key before opening the portal. " +
                "Follow Apple's steps to create the certificate. Storage and P12 export are handled outside Sherpa for this flow.";
            portalHelp.IsVisible = !type.CanCreateInApp;
            storageGroup.IsVisible = type.CanCreateInApp;
            passphraseGroup.IsVisible = type.CanCreateInApp && _saveToDiskCheckBox.IsChecked;
            UpdateSubmitEnabled();
        }

        _typePicker.SelectedIndexChanged += (_, _) => UpdateSelection();
        UpdateSelection();

        return new VerticalStackLayout
        {
            Spacing = 16,
            Children =
            {
                CreateFormGroup("Certificate Type", _typePicker),
                description,
                portalHelp,
                storageGroup,
                passphraseGroup,
            }
        };
    }

    protected override Task<CreateCertificateFormResult> OnSubmitAsync()
    {
        var certType = SelectedType.Value;
        var saveToDisk = SelectedType.CanCreateInApp && _saveToDiskCheckBox.IsChecked;
        var passphrase = saveToDisk ? _passphraseEntry.Text ?? string.Empty : null;

        return Task.FromResult(new CreateCertificateFormResult(
            certType,
            saveToDisk,
            passphrase));
    }
}
