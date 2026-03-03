using MauiSherpa.Core.Interfaces;
using MauiSherpa.Pages.Forms;
#if MACOSAPP
using Microsoft.Maui.Platform.MacOS;
#endif
#if LINUXGTK
using Platform.Maui.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Modals;

public class AppleConfigWizardPage : WizardFormPage<AppleConfigVM?>
{
    protected override string FormTitle => "Apple Configuration";
    protected override string DefaultSubmitText => "Done";
    protected override string BlazorRoute => "/modal/apple-config-wizard";

    public AppleConfigWizardPage(
        HybridFormBridgeHolder bridgeHolder,
        List<AppleIdentity> appleIdentities,
        List<ManagedSecret> managedSecrets,
        AppleConfigVM? existingConfig = null)
        : base(bridgeHolder)
    {
        Bridge.Parameters["appleIdentities"] = appleIdentities;
        Bridge.Parameters["managedSecrets"] = managedSecrets;

        if (existingConfig != null)
            Bridge.Parameters["existingConfig"] = existingConfig;

#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, false);
        MacOSPage.SetModalSheetWidth(this, 680);
        MacOSPage.SetModalSheetHeight(this, 600);
#elif LINUXGTK
        GtkPage.SetModalSizesToContent(this, false);
        GtkPage.SetModalWidth(this, 680);
        GtkPage.SetModalHeight(this, 600);
#endif
    }
}
