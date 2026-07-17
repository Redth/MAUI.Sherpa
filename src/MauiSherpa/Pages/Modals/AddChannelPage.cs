using MauiSherpa.Pages.Forms;
#if MACOSAPP
using Microsoft.Maui.Platforms.MacOS.Platform;
#endif
#if LINUXGTK
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Modals;

/// <summary>
/// Native hybrid modal for tracking a new .NET SDK channel with dotnetup.
/// Returns the selected channel string (e.g. "latest", "9.0.1xx", "10.0.203"),
/// or null when cancelled.
/// </summary>
public class AddChannelPage : HybridFormPage<string?>
{
    protected override string FormTitle => "Track an SDK channel";
    protected override string SubmitButtonText => "Install & track";
    protected override string BlazorRoute => "/modal/add-channel";

    public AddChannelPage(HybridFormBridgeHolder bridgeHolder, IReadOnlyList<string> trackedChannels)
        : base(bridgeHolder)
    {
        Bridge.Parameters["TrackedChannels"] = trackedChannels;

#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, true);
        MacOSPage.SetModalSheetMinWidth(this, 560);
#elif LINUXGTK
        GtkPage.SetModalSizesToContent(this, true);
        GtkPage.SetModalMinWidth(this, 560);
#endif
    }
}
