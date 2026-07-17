using MauiSherpa.Pages.Forms;
using MauiSherpa.Workloads.Models;
#if MACOSAPP
using Microsoft.Maui.Platforms.MacOS.Platform;
#endif
#if LINUXGTK
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Modals;

public sealed class WorkloadSelectionPage : HybridFormPage<WorkloadSelectionResult?>
{
    protected override string FormTitle => "Manage .NET workloads";
    protected override string SubmitButtonText => "Review & apply changes";

    protected override string BlazorRoute => "/modal/manage-workloads";

    public WorkloadSelectionPage(
        HybridFormBridgeHolder bridgeHolder,
        DotnetWorkloadInventory inventory)
        : base(bridgeHolder)
    {
        Bridge.Parameters["Inventory"] = inventory;

#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, false);
        MacOSPage.SetModalSheetWidth(this, 820);
        MacOSPage.SetModalSheetHeight(this, 700);
#elif LINUXGTK
        GtkPage.SetModalSizesToContent(this, false);
        GtkPage.SetModalWidth(this, 820);
        GtkPage.SetModalHeight(this, 700);
#endif
    }
}
