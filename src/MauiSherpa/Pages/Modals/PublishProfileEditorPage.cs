using MauiSherpa.Core.Interfaces;
using MauiSherpa.Pages.Forms;
#if MACOSAPP
using Microsoft.Maui.Platform.MacOS;
#endif
#if LINUXGTK
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Modals;

public class PublishProfileEditorPage : HybridFormPage<PublishProfile?>
{
    protected override string FormTitle => "Publish Profile";
    protected override string SubmitButtonText => "Save";
    protected override string BlazorRoute => "/modal/publish-profile-editor";

    public PublishProfileEditorPage(
        HybridFormBridgeHolder bridgeHolder,
        PublishProfile? existingProfile = null)
        : base(bridgeHolder)
    {
        if (existingProfile is not null)
            Bridge.Parameters["Profile"] = existingProfile;

#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, false);
        MacOSPage.SetModalSheetWidth(this, 750);
        MacOSPage.SetModalSheetHeight(this, 650);
#elif LINUXGTK
        GtkPage.SetModalSizesToContent(this, false);
        GtkPage.SetModalWidth(this, 700);
        GtkPage.SetModalHeight(this, 650);
#endif
    }
}
