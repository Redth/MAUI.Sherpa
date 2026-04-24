using MauiSherpa.Pages.Forms;
#if MACOSAPP
using Microsoft.Maui.Platform.MacOS;
#endif
#if LINUXGTK
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Modals;

public class PepkExportPage : HybridFormPage<bool>
{
    protected override string FormTitle { get; }
    protected override string SubmitButtonText => "Export";
    protected override string BlazorRoute => "/modal/pepk-export";

    public PepkExportPage(
        HybridFormBridgeHolder bridgeHolder,
        string alias,
        string keystorePath)
        : base(bridgeHolder)
    {
        FormTitle = $"Export PEPK — {alias}";
        Bridge.Parameters["Alias"] = alias;
        Bridge.Parameters["KeystorePath"] = keystorePath;
#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, false);
        MacOSPage.SetModalSheetWidth(this, 500);
        MacOSPage.SetModalSheetHeight(this, 450);
#elif LINUXGTK
        GtkPage.SetModalSizesToContent(this, false);
        GtkPage.SetModalWidth(this, 500);
        GtkPage.SetModalHeight(this, 450);
#endif
    }
}
