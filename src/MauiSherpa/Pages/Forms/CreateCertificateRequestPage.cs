using System.Net.Mail;
using Microsoft.Maui.Controls;
#if MACOSAPP
using Microsoft.Maui.Platforms.MacOS.Platform;
#elif LINUXGTK
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Forms;

public record CreateCertificateRequestFormResult(string CommonName, string? Email, string Passphrase, bool UseEc);

public class CreateCertificateRequestPage : FormPage<CreateCertificateRequestFormResult>
{
    private readonly string _commonName;
    private readonly bool _isPaymentProcessing;
    private readonly bool _storeKeyInKeychain;
    private Entry _nameEntry = null!;
    private Entry _emailEntry = null!;
    private Entry _passwordEntry = null!;
    private Picker? _keyPicker;

    protected override string FormTitle => "Generate Certificate Request";
    protected override string SubmitButtonText => "Save Request";
    protected override double FormBodyHeightRequest => 440;
    protected override bool CanSubmit =>
        !string.IsNullOrWhiteSpace(_nameEntry?.Text) &&
        (_storeKeyInKeychain || !string.IsNullOrWhiteSpace(_passwordEntry?.Text)) &&
        (string.IsNullOrWhiteSpace(_emailEntry?.Text) || MailAddress.TryCreate(_emailEntry.Text.Trim(), out _));

    public CreateCertificateRequestPage(string commonName, bool isPaymentProcessing, bool storeKeyInKeychain)
    {
        _commonName = commonName;
        _isPaymentProcessing = isPaymentProcessing;
        _storeKeyInKeychain = storeKeyInKeychain;
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
        _nameEntry = CreateEntry("Name for this signing key");
        _nameEntry.AutomationId = "csr-common-name";
        _nameEntry.Text = _commonName;
        _emailEntry = CreateEntry("Optional email address", Keyboard.Email);
        _emailEntry.AutomationId = "csr-email";
        var content = new VerticalStackLayout
        {
            Spacing = 16,
            Children =
            {
                CreateFormGroup("Common Name", _nameEntry),
                CreateFormGroup("Email Address", _emailEntry, "Optional; enter a valid email address if provided"),
            }
        };

        if (!_storeKeyInKeychain)
        {
            _passwordEntry = CreatePasswordEntry("Required to protect the private key");
            _passwordEntry.AutomationId = "csr-passphrase";
            content.Children.Add(CreateFormGroup("Private Key Passphrase", _passwordEntry,
                "Keep this passphrase. You will need it to use the issued certificate with its private key."));
        }

        if (_isPaymentProcessing)
        {
            _keyPicker = CreatePicker(null,
                ["ECC 256 (outside China mainland)", "RSA 2048 (China mainland)"]);
            _keyPicker.AutomationId = "csr-payment-region";
            _keyPicker.SelectedIndex = 0;
            content.Children.Add(CreateFormGroup("Payment Processing Region", _keyPicker,
                "If your payment provider supplies the CSR, use theirs instead of generating one here."));
        }

        var help = new Label
        {
            Text = _storeKeyInKeychain
                ? "Choose where to save the request (Desktop by default). The private key is stored in your login Keychain, " +
                    "just like a request created in Keychain Access. Upload the request to Apple, then open the downloaded " +
                    ".cer file in Keychain Access. No separate .key file or passphrase is needed."
                : "Choose where to save the request (Desktop by default). An encrypted .key file is saved beside it. " +
                    "Keep that key and its passphrase to combine with Apple's downloaded certificate. " +
                    "Upload only the .certSigningRequest file to Apple, never the .key file.",
            FontSize = 13,
        };
        help.SetDynamicResource(Label.TextColorProperty, FormTheme.TextSecondary);
        content.Children.Add(help);
        return content;
    }

    protected override Task<CreateCertificateRequestFormResult> OnSubmitAsync() =>
        Task.FromResult(new CreateCertificateRequestFormResult(
            _nameEntry.Text.Trim(),
            string.IsNullOrWhiteSpace(_emailEntry.Text) ? null : _emailEntry.Text.Trim(),
            _passwordEntry?.Text ?? string.Empty,
            _isPaymentProcessing && _keyPicker!.SelectedIndex == 0));
}
