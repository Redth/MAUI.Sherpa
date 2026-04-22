using MauiSherpa.Pages.Forms;
#if MACOSAPP
using Microsoft.Maui.Platform.MacOS;
#endif
#if LINUXGTK
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Modals;

public class PublishSecretsPage : WizardFormPage<bool>
{
    protected override string FormTitle => "Publish Secrets";
    protected override string DefaultSubmitText => "Publish";
    protected override string BlazorRoute => "/modal/publish-secrets";

    public PublishSecretsPage(
        HybridFormBridgeHolder bridgeHolder,
        Dictionary<string, string> secrets)
        : base(bridgeHolder)
    {
        Bridge.Parameters["Secrets"] = secrets;
#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, false);
        MacOSPage.SetModalSheetWidth(this, 650);
        MacOSPage.SetModalSheetHeight(this, 500);
#elif LINUXGTK
        GtkPage.SetModalSizesToContent(this, false);
        GtkPage.SetModalWidth(this, 600);
        GtkPage.SetModalHeight(this, 500);
#endif
    }
}
