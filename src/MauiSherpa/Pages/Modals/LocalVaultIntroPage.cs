using MauiSherpa.Core.Interfaces;
using MauiSherpa.Pages.Forms;
#if MACOSAPP
using Microsoft.Maui.Platforms.MacOS.Platform;
#endif
#if LINUXGTK
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Modals;

public sealed class LocalVaultIntroPage : HybridFormPage<LocalVaultIntroductionResult?>
{
    protected override string FormTitle => "Use Sherpa's Local Vault";
    protected override string SubmitButtonText => "Enable Local Vault";
    protected override string BlazorRoute => "/modal/local-vault-intro";

    public LocalVaultIntroPage(HybridFormBridgeHolder bridgeHolder, bool previewOnly)
        : base(bridgeHolder)
    {
        Bridge.Parameters["PreviewOnly"] = previewOnly;
        Bridge.PreventClose = true;
        Bridge.PreventSubmitClose = true;
        Bridge.SetActionState(false, actionText: "Enable Local Vault",
            secondaryActionText: "Configure Later", secondaryEnabled: false);
#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, false);
        MacOSPage.SetModalSheetWidth(this, 620);
        MacOSPage.SetModalSheetHeight(this, 520);
#elif LINUXGTK
        GtkPage.SetModalSizesToContent(this, false);
        GtkPage.SetModalWidth(this, 620);
        GtkPage.SetModalHeight(this, 520);
#elif WINDOWS
        Platforms.Windows.WindowsModalPresentation.SetPreferredSize(this, 620, 520);
#endif
    }
}
