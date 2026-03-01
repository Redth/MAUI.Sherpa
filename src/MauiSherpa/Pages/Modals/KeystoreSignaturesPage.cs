using MauiSherpa.Core.Interfaces;
using MauiSherpa.Pages.Forms;
#if MACOSAPP
using Microsoft.Maui.Platform.MacOS;
#endif

namespace MauiSherpa.Pages.Modals;

public class KeystoreSignaturesPage : HybridViewPage
{
    protected override string FormTitle { get; }
    protected override string BlazorRoute => "/modal/keystore-signatures";

    public KeystoreSignaturesPage(ModalParameterService modalParams, string alias, string keystorePath, string keystoreId)
    {
        FormTitle = $"Signatures — {alias}";
        modalParams.Clear();
        modalParams.Set("Alias", alias);
        modalParams.Set("KeystorePath", keystorePath);
        modalParams.Set("KeystoreId", keystoreId);
#if MACOSAPP
        MacOSPage.SetModalSheetWidth(this, 900);
        MacOSPage.SetModalSheetHeight(this, 500);
#endif
    }
}
