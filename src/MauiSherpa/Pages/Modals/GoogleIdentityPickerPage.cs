using MauiSherpa.Core.Interfaces;
using MauiSherpa.Pages.Forms;
#if MACOSAPP
using Microsoft.Maui.Platforms.MacOS.Platform;
#endif
#if LINUXGTK
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Modals;

public class GoogleIdentityPickerPage : HybridFormPage<GoogleIdentity?>
{
    protected override string FormTitle => "Add Google Identity";
    protected override string SubmitButtonText => "Add";
    protected override string BlazorRoute => "/modal/google-identity-picker";

    public GoogleIdentityPickerPage(
        HybridFormBridgeHolder bridgeHolder,
        IReadOnlyList<GoogleIdentity> googleIdentities,
        IReadOnlyList<string> existingIdentityIds)
        : base(bridgeHolder)
    {
        Bridge.Parameters["GoogleIdentities"] = googleIdentities;
        Bridge.Parameters["ExistingIdentityIds"] = existingIdentityIds;

#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, true);
        MacOSPage.SetModalSheetMinWidth(this, 450);
#elif LINUXGTK
        GtkPage.SetModalSizesToContent(this, true);
        GtkPage.SetModalMinWidth(this, 450);
#endif
    }
}
