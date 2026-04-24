using MauiSherpa.Pages.Forms;
#if MACOSAPP
using Microsoft.Maui.Platform.MacOS;
#endif
#if LINUXGTK
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Modals;

public class CISecretsWizardPage : WizardFormPage<bool>
{
    protected override string FormTitle => "CI Secrets Wizard";
    protected override string DefaultSubmitText => "Copy All Secrets";
    protected override string BlazorRoute => "/modal/ci-secrets-wizard";

    public CISecretsWizardPage(HybridFormBridgeHolder bridgeHolder)
        : base(bridgeHolder)
    {
#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, false);
        MacOSPage.SetModalSheetWidth(this, 700);
        MacOSPage.SetModalSheetHeight(this, 650);
#elif LINUXGTK
        GtkPage.SetModalSizesToContent(this, false);
        GtkPage.SetModalWidth(this, 700);
        GtkPage.SetModalHeight(this, 650);
#endif
    }
}
