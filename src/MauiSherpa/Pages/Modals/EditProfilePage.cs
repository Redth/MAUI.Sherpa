using MauiSherpa.Core.Interfaces;
using MauiSherpa.Pages.Forms;
#if MACOSAPP
using Microsoft.Maui.Platform.MacOS;
#endif

namespace MauiSherpa.Pages.Modals;

public class EditProfilePage : HybridFormPage<AppleProfile>
{
    protected override string FormTitle => "Edit Profile";
    protected override string SubmitButtonText => "Regenerate Profile";
    protected override string BlazorRoute => "/modal/edit-profile";

    public EditProfilePage(
        HybridFormBridgeHolder bridgeHolder,
        AppleProfile profile)
        : base(bridgeHolder)
    {
#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, false);
        MacOSPage.SetModalSheetWidth(this, 650);
        MacOSPage.SetModalSheetHeight(this, 600);
#endif
        ConfigureParameters(profile);
    }

    private void ConfigureParameters(AppleProfile profile)
    {
        Bridge.Parameters["Profile"] = profile;
    }
}
