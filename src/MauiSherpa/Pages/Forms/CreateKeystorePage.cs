#if MACOSAPP
using Microsoft.Maui.Platforms.MacOS.Platform;
#endif
#if LINUXGTK
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Forms;

public record KeystoreCreateResult(
    string Alias,
    string KeystorePassword,
    string KeyPassword,
    string CN,
    string OU,
    string Organization,
    string City,
    string State,
    string Country,
    int ValidityDays,
    string KeyAlgorithm,
    int KeySize);

public class CreateKeystorePage : HybridFormPage<KeystoreCreateResult>
{
    protected override string FormTitle => "Create Keystore";
    protected override string SubmitButtonText => "Create";
    protected override string BlazorRoute => "/modal/create-keystore";

    public CreateKeystorePage(HybridFormBridgeHolder bridgeHolder)
        : base(bridgeHolder)
    {
#if MACOSAPP
        MacOSPage.SetModalSheetMinWidth(this, 560);
#elif LINUXGTK
        GtkPage.SetModalMinWidth(this, 560);
#endif
    }
}
