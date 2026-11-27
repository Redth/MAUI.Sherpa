#if MACOSAPP
using Microsoft.Maui.Platforms.MacOS.Platform;
#elif LINUXGTK
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Forms;

public record CreateCertificateRequestFormResult(string CommonName, string? Email, string Passphrase, bool UseEc);

public class CreateCertificateRequestPage : HybridFormPage<CreateCertificateRequestFormResult>
{
    protected override string FormTitle => "Generate Certificate Request";
    protected override string SubmitButtonText => "Save Request";
    protected override string BlazorRoute => "/modal/create-certificate-request";

    public string? SavePath => Bridge.Parameters.GetValueOrDefault("SavePath") as string;

    public CreateCertificateRequestPage(
        HybridFormBridgeHolder bridgeHolder,
        string commonName,
        bool isPaymentProcessing,
        bool storeKeyInKeychain,
        string suggestedFileName)
        : base(bridgeHolder)
    {
        Bridge.Parameters["CommonName"] = commonName;
        Bridge.Parameters["IsPaymentProcessing"] = isPaymentProcessing;
        Bridge.Parameters["StoreKeyInKeychain"] = storeKeyInKeychain;
        Bridge.Parameters["SuggestedFileName"] = suggestedFileName;
        Bridge.Parameters["SavePath"] = null;

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
}
