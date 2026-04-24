using MauiSherpa.Core.Interfaces;
using MauiSherpa.Pages.Forms;
#if MACOSAPP
using Microsoft.Maui.Platform.MacOS;
#endif
#if LINUXGTK
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Modals;

public class PublishReviewPage : WizardFormPage<bool>
{
    protected override string FormTitle => "Publish Secrets";
    protected override string DefaultSubmitText => "Publish";
    protected override string BlazorRoute => "/modal/publish-review";

    public PublishReviewPage(
        HybridFormBridgeHolder bridgeHolder,
        PublishProfile profile)
        : base(bridgeHolder)
    {
        Bridge.Parameters["Profile"] = profile;
#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, false);
        MacOSPage.SetModalSheetWidth(this, 650);
        MacOSPage.SetModalSheetHeight(this, 550);
#elif LINUXGTK
        GtkPage.SetModalSizesToContent(this, false);
        GtkPage.SetModalWidth(this, 600);
        GtkPage.SetModalHeight(this, 550);
#endif
    }
}
