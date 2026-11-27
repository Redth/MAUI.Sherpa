using MauiSherpa.Core.Interfaces;
#if MACOSAPP
using Microsoft.Maui.Platforms.MacOS.Platform;
#endif
#if LINUXGTK
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Forms;

public class CreateSimulatorPage : HybridFormPage<bool>
{
    protected override string FormTitle => "Create Simulator";
    protected override string SubmitButtonText => "Create";
    protected override string BlazorRoute => "/modal/create-simulator";

    public CreateSimulatorPage(
        HybridFormBridgeHolder bridgeHolder,
        ISimulatorService simulatorService,
        IReadOnlyList<SimulatorRuntime> runtimes,
        IReadOnlyList<SimulatorDeviceType> allDeviceTypes)
        : base(bridgeHolder)
    {
        Bridge.Parameters["SimulatorService"] = simulatorService;
        Bridge.Parameters["Runtimes"] = runtimes;
        Bridge.Parameters["AllDeviceTypes"] = allDeviceTypes;

#if MACOSAPP
        MacOSPage.SetModalSheetMinWidth(this, 560);
#elif LINUXGTK
        GtkPage.SetModalMinWidth(this, 560);
#endif
    }
}
