using MauiSherpa.Pages.Forms;
using MauiSherpa.Workloads.Models;
#if MACOSAPP
using Microsoft.Maui.Platforms.MacOS.Platform;
#endif
#if LINUXGTK
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Modals;

public sealed class WorkloadSetPage : HybridFormPage<string?>
{
    protected override string FormTitle => "Change workload set";
    protected override string SubmitButtonText => "Review change";
    protected override string BlazorRoute => "/modal/workload-set";

    public WorkloadSetPage(
        HybridFormBridgeHolder bridgeHolder,
        DotnetWorkloadInventory inventory,
        string? initialVersion = null)
        : base(bridgeHolder)
    {
        Bridge.Parameters["Inventory"] = inventory;
        Bridge.Parameters["InitialVersion"] = initialVersion;

#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, true);
        MacOSPage.SetModalSheetMinWidth(this, 700);
#elif LINUXGTK
        GtkPage.SetModalSizesToContent(this, true);
        GtkPage.SetModalMinWidth(this, 700);
#endif
    }
}
