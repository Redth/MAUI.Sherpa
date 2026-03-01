using MauiSherpa.Pages.Forms;
#if MACOSAPP
using Microsoft.Maui.Platform.MacOS;
#endif

namespace MauiSherpa.Pages.Modals;

public class ExportSettingsPage : HybridFormPage<bool>
{
    protected override string FormTitle => "Export Settings";
    protected override string SubmitButtonText => "Export";
    protected override string BlazorRoute => "/modal/export-settings";

    public ExportSettingsPage(HybridFormBridgeHolder bridgeHolder)
        : base(bridgeHolder)
    {
#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, false);
        MacOSPage.SetModalSheetWidth(this, 500);
        MacOSPage.SetModalSheetHeight(this, 600);
#endif
    }
}
