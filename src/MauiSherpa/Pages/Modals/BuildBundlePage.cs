using MauiSherpa.Core.Interfaces;
using MauiSherpa.Pages.Forms;
#if MACOSAPP
using Microsoft.Maui.Platforms.MacOS.Platform;
#endif
#if LINUXGTK
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Modals;

/// <summary>
/// Modal for assembling a profile into a <c>.sherpabundle</c> file. Lets the user
/// pick the environment name, platforms, build substitution maps, and deploy
/// targets, then resolves signing material and writes the bundle to disk.
/// </summary>
public class BuildBundlePage : HybridFormPage<bool>
{
    protected override string FormTitle => "Build Sherpa Bundle";
    protected override string SubmitButtonText => "Build & Save";
    protected override string BlazorRoute => "/modal/build-bundle";

    public BuildBundlePage(
        HybridFormBridgeHolder bridgeHolder,
        PublishProfile profile)
        : base(bridgeHolder)
    {
        Bridge.Parameters["Profile"] = profile;

#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, false);
        MacOSPage.SetModalSheetWidth(this, 750);
        MacOSPage.SetModalSheetHeight(this, 700);
#elif LINUXGTK
        GtkPage.SetModalSizesToContent(this, false);
        GtkPage.SetModalWidth(this, 700);
        GtkPage.SetModalHeight(this, 700);
#endif
    }
}
