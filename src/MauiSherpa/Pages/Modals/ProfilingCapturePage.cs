using MauiSherpa.Core.Models.Profiling;
using MauiSherpa.Pages.Forms;
#if MACOSAPP
using Microsoft.Maui.Platforms.MacOS.Platform;
#endif
#if LINUXGTK
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Modals;

public class ProfilingCapturePage : HybridFormPage<ProfilingSessionManifest>
{
    protected override string FormTitle => "Capture profile";
    protected override string SubmitButtonText => "Start profile";
    protected override string BlazorRoute => "/modal/profiling-capture";

    public ProfilingCapturePage(HybridFormBridgeHolder bridgeHolder)
        : base(bridgeHolder)
    {
#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, false);
        MacOSPage.SetModalSheetWidth(this, 760);
        MacOSPage.SetModalSheetHeight(this, 740);
#elif LINUXGTK
        GtkPage.SetModalSizesToContent(this, false);
        GtkPage.SetModalWidth(this, 760);
        GtkPage.SetModalHeight(this, 740);
#endif
    }
}
