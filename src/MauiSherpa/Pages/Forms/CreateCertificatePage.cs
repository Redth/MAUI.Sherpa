using Microsoft.Maui.Controls;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Pages.Forms;

public class CreateCertificatePage : FormPage<AppleCertificateCreateResult>
{
    private readonly IAppleConnectService _appleService;

    private Picker _typePicker = null!;
    private Entry _commonNameEntry = null!;
    private Entry _passphraseEntry = null!;

    private static readonly string[] CertTypeLabels =
    {
        "iOS Development",
        "iOS Distribution",
        "Mac Development",
        "Mac App Distribution",
        "Mac Installer Distribution",
        "Developer ID Application",
        "Developer ID Kernel Extension",
    };

    private static readonly string[] CertTypeValues =
    {
        "IOS_DEVELOPMENT",
        "IOS_DISTRIBUTION",
        "MAC_APP_DEVELOPMENT",
        "MAC_APP_DISTRIBUTION",
        "MAC_INSTALLER_DISTRIBUTION",
        "DEVELOPER_ID_APPLICATION",
        "DEVELOPER_ID_KEXT",
    };

    protected override string FormTitle => "Create Certificate";

    // Always submittable — all fields have defaults or are optional
    protected override bool CanSubmit => _typePicker?.SelectedIndex >= 0;

    public CreateCertificatePage(IAppleConnectService appleService)
    {
        _appleService = appleService;
    }

    protected override View BuildFormContent()
    {
        _typePicker = CreatePicker(null, CertTypeLabels);

        _commonNameEntry = CreateEntry(Environment.MachineName);
        _passphraseEntry = CreatePasswordEntry("Leave empty for no passphrase");

        return new VerticalStackLayout
        {
            Spacing = 16,
            Children =
            {
                CreateFormGroup("Certificate Type", _typePicker),
                CreateFormGroup("Common Name", _commonNameEntry,
                    "Defaults to your machine name if not specified"),
                CreateFormGroup("PFX Passphrase", _passphraseEntry,
                    "Password to protect the exported certificate file"),
            }
        };
    }

    protected override async Task<AppleCertificateCreateResult> OnSubmitAsync()
    {
        var certType = CertTypeValues[_typePicker.SelectedIndex];
        var commonName = string.IsNullOrWhiteSpace(_commonNameEntry.Text) ? null : _commonNameEntry.Text.Trim();
        var passphrase = string.IsNullOrWhiteSpace(_passphraseEntry.Text) ? null : _passphraseEntry.Text;

        return await _appleService.CreateCertificateAsync(certType, commonName, passphrase);
    }
}
