using MauiSherpa.Core.Models.Profiling;
using MauiSherpa.Pages.Forms;
#if MACOSAPP
using Microsoft.Maui.Platform.MacOS;
#endif
#if LINUXGTK
using Platform.Maui.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Modals;

public class ProfilingCaptureWizardPage : WizardFormPage<ProfilingSessionManifest>
{
    protected override string FormTitle => "New Profiling Session";
    protected override string DefaultSubmitText => "Start Capture";
    protected override string BlazorRoute => "/modal/profiling-wizard";

    public ProfilingCaptureWizardPage(HybridFormBridgeHolder bridgeHolder)
        : base(bridgeHolder)
    {
#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, false);
        MacOSPage.SetModalSheetWidth(this, 750);
        MacOSPage.SetModalSheetHeight(this, 700);
#elif LINUXGTK
        GtkPage.SetModalSizesToContent(this, false);
        GtkPage.SetModalWidth(this, 750);
        GtkPage.SetModalHeight(this, 700);
#endif
    }
}
