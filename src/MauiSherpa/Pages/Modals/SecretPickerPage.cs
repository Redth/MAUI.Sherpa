using MauiSherpa.Core.Interfaces;
using MauiSherpa.Pages.Forms;
#if MACOSAPP
using Microsoft.Maui.Platform.MacOS;
#endif
#if LINUXGTK
using Platform.Maui.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Modals;

public class SecretPickerPage : HybridFormPage<string?>
{
    protected override string FormTitle => "Add Secret Mapping";
    protected override string SubmitButtonText => "Add";
    protected override string BlazorRoute => "/modal/secret-picker";

    public SecretPickerPage(
        HybridFormBridgeHolder bridgeHolder,
        IReadOnlyList<ManagedSecret> managedSecrets,
        IReadOnlyList<string> existingKeys)
        : base(bridgeHolder)
    {
        Bridge.Parameters["ManagedSecrets"] = managedSecrets;
        Bridge.Parameters["ExistingKeys"] = existingKeys;

#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, true);
        MacOSPage.SetModalSheetMinWidth(this, 450);
#elif LINUXGTK
        GtkPage.SetModalSizesToContent(this, true);
        GtkPage.SetModalMinWidth(this, 450);
#endif
    }
}
