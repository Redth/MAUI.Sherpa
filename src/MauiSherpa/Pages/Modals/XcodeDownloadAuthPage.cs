using MauiSherpa.Pages.Forms;
#if MACOSAPP
using Microsoft.Maui.Platform.MacOS;
#endif
#if LINUXGTK
using Platform.Maui.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Modals;

public class XcodeDownloadAuthPage : WizardFormPage<bool>
{
    protected override string FormTitle => "Apple Developer Sign In";
    protected override string DefaultSubmitText => "Verify";
    protected override string BlazorRoute => "/modal/xcode-download-auth";

    public XcodeDownloadAuthPage(HybridFormBridgeHolder bridgeHolder)
        : base(bridgeHolder)
    {
#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, false);
        MacOSPage.SetModalSheetWidth(this, 520);
        MacOSPage.SetModalSheetHeight(this, 420);
#elif LINUXGTK
        GtkPage.SetModalSizesToContent(this, false);
        GtkPage.SetModalWidth(this, 520);
        GtkPage.SetModalHeight(this, 420);
#endif
    }
}
