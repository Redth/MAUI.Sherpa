using MauiSherpa.Core.Interfaces;
using MauiSherpa.Pages.Forms;
#if MACOSAPP
using Microsoft.Maui.Platform.MacOS;
#endif

namespace MauiSherpa.Pages.Modals;

public class ManageCapabilitiesPage : HybridViewPage
{
    protected override string FormTitle => "Capabilities";
    protected override string BlazorRoute => "/modal/manage-capabilities";

    public ManageCapabilitiesPage(ModalParameterService modalParams, AppleBundleId bundleId)
    {
        modalParams.Clear();
        modalParams.Set("BundleId", bundleId);
#if MACOSAPP
        MacOSPage.SetModalSheetWidth(this, 700);
        MacOSPage.SetModalSheetHeight(this, 550);
#endif
    }
}
