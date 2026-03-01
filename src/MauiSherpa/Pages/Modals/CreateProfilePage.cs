using MauiSherpa.Core.Interfaces;
using MauiSherpa.Pages.Forms;
#if MACOSAPP
using Microsoft.Maui.Platform.MacOS;
#endif

namespace MauiSherpa.Pages.Modals;

public class CreateProfilePage : WizardFormPage<AppleProfile>
{
    protected override string FormTitle => "Create Provisioning Profile";
    protected override string DefaultSubmitText => "Create Profile";
    protected override string BlazorRoute => "/modal/create-profile";

    public CreateProfilePage(HybridFormBridgeHolder bridgeHolder)
        : base(bridgeHolder)
    {
#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, false);
        MacOSPage.SetModalSheetWidth(this, 650);
        MacOSPage.SetModalSheetHeight(this, 650);
#endif
    }
}
