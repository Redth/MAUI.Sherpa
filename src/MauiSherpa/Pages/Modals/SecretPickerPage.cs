using MauiSherpa.Core.Interfaces;
using MauiSherpa.Pages.Forms;
#if MACOSAPP
using Microsoft.Maui.Platforms.MacOS.Platform;
#endif
#if LINUXGTK
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Modals;

public class SecretPickerPage : HybridFormPage<string?>
{
    readonly string _title;
    readonly string _submitButtonText;

    protected override string FormTitle => _title;
    protected override string SubmitButtonText => _submitButtonText;
    protected override string BlazorRoute => "/modal/secret-picker";

    public SecretPickerPage(
        HybridFormBridgeHolder bridgeHolder,
        IReadOnlyList<ManagedSecret> managedSecrets,
        IReadOnlyList<string> existingKeys,
        string title = "Add Secret Mapping",
        string submitButtonText = "Add")
        : base(bridgeHolder)
    {
        _title = title;
        _submitButtonText = submitButtonText;
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
