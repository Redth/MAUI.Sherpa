using MauiSherpa.Core.Interfaces;
using MauiSherpa.Pages.Forms;
#if MACOSAPP
using Microsoft.Maui.Platform.MacOS;
#endif

namespace MauiSherpa.Pages.Modals;

public class AppleIdentityPage : HybridFormPage<AppleIdentity>
{
    private readonly bool _isEditing;

    protected override string FormTitle => _isEditing ? "Edit Apple Identity" : "Add Apple Identity";
    protected override string SubmitButtonText => "Save";
    protected override string BlazorRoute => "/modal/apple-identity";

    public AppleIdentityPage(
        HybridFormBridgeHolder bridgeHolder,
        AppleIdentity? identity = null)
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
