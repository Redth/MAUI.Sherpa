using MauiSherpa.Core.Interfaces;
using MauiSherpa.Pages.Forms;
#if MACOSAPP
using Microsoft.Maui.Platform.MacOS;
#endif
#if LINUXGTK
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Modals;

public class ImportSettingsPage : HybridFormPage<BackupImportResult>
{
    protected override string FormTitle => "Import Settings";
    protected override string SubmitButtonText => "Import";
    protected override string BlazorRoute => "/modal/import-settings";

    public ImportSettingsPage(HybridFormBridgeHolder bridgeHolder)
        : base(bridgeHolder)
    {
#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, false);
        MacOSPage.SetModalSheetWidth(this, 500);
        MacOSPage.SetModalSheetHeight(this, 350);
#elif LINUXGTK
        GtkPage.SetModalSizesToContent(this, false);
        GtkPage.SetModalWidth(this, 500);
        GtkPage.SetModalHeight(this, 350);
#endif
    }
}
