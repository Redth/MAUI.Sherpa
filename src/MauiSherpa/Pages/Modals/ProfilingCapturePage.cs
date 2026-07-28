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
    protected override double ActionButtonHeight => 44;

    public ProfilingCapturePage(HybridFormBridgeHolder bridgeHolder)
        : base(bridgeHolder)
    {
#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, false);
        MacOSPage.SetModalSheetWidth(this, 780);
        MacOSPage.SetModalSheetHeight(this, 760);
#elif LINUXGTK
        GtkPage.SetModalSizesToContent(this, false);
        GtkPage.SetModalWidth(this, 780);
        GtkPage.SetModalHeight(this, 760);
#endif
    }
}
