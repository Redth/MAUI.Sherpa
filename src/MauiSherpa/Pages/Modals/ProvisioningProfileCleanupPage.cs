using MauiSherpa.Pages.Forms;
#if MACOSAPP
using Microsoft.Maui.Platforms.MacOS.Platform;
#endif
#if LINUXGTK
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Modals;

public sealed class ProvisioningProfileCleanupPage : HybridFormPage<int>
{
    protected override string FormTitle => "Clean Up Provisioning Profiles";
    protected override string SubmitButtonText => "Delete Selected";
    protected override bool IsDestructiveSubmit => true;
    protected override string BlazorRoute => "/modal/provisioning-profile-cleanup";

    public ProvisioningProfileCleanupPage(HybridFormBridgeHolder bridgeHolder)
        : base(bridgeHolder)
    {
#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, false);
        MacOSPage.SetModalSheetWidth(this, 900);
        MacOSPage.SetModalSheetHeight(this, 680);
#elif LINUXGTK
        GtkPage.SetModalSizesToContent(this, false);
        GtkPage.SetModalWidth(this, 900);
        GtkPage.SetModalHeight(this, 680);
#endif
    }
}
