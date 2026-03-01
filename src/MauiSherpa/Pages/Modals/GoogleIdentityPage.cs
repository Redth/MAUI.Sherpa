using MauiSherpa.Core.Interfaces;
using MauiSherpa.Pages.Forms;
#if MACOSAPP
using Microsoft.Maui.Platform.MacOS;
#endif

namespace MauiSherpa.Pages.Modals;

public class GoogleIdentityPage : HybridFormPage<GoogleIdentity>
{
    private readonly bool _isEditing;

    protected override string FormTitle => _isEditing ? "Edit Google Identity" : "Add Google Identity";
    protected override string SubmitButtonText => "Save";
    protected override string BlazorRoute => "/modal/google-identity";

    public GoogleIdentityPage(
        HybridFormBridgeHolder bridgeHolder,
        GoogleIdentity? identity = null)
        : base(bridgeHolder)
    {
        _isEditing = identity != null;
        if (identity != null)
            Bridge.Parameters["Identity"] = identity;
#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, false);
        MacOSPage.SetModalSheetWidth(this, 500);
        MacOSPage.SetModalSheetHeight(this, 550);
#endif
    }
}
