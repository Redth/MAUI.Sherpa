using MauiSherpa.Core.Interfaces;
using MauiSherpa.Pages.Forms;
#if MACOSAPP
using Microsoft.Maui.Platform.MacOS;
#endif
#if LINUXGTK
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Modals;

public class RuntimePickerPage : HybridFormPage<DownloadableSimulatorRuntime?>
{
    protected override string FormTitle => "Add Simulator Runtime";
    protected override string SubmitButtonText => "Install";
    protected override string BlazorRoute => "/modal/runtime-picker";

    public RuntimePickerPage(
        HybridFormBridgeHolder bridgeHolder,
        IReadOnlyList<DownloadableSimulatorRuntime> downloadableRuntimes,
        IReadOnlyList<string> installedRuntimeBuilds)
        : base(bridgeHolder)
    {
        Bridge.Parameters["DownloadableRuntimes"] = downloadableRuntimes;
        Bridge.Parameters["InstalledRuntimeBuilds"] = installedRuntimeBuilds;

#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, false);
        MacOSPage.SetModalSheetWidth(this, 760);
        MacOSPage.SetModalSheetHeight(this, 640);
#elif LINUXGTK
        GtkPage.SetModalSizesToContent(this, false);
        GtkPage.SetModalWidth(this, 760);
        GtkPage.SetModalHeight(this, 640);
#endif
    }
}
