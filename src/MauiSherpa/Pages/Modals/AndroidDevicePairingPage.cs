using MauiSherpa.Pages.Forms;
#if MACOSAPP
using Microsoft.Maui.Platforms.MacOS.Platform;
#endif
#if LINUXGTK
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Modals;

public sealed class AndroidDevicePairingPage : HybridFormPage<bool>
{
    private readonly CancellationTokenSource lifetime = new();
    private bool disappeared;

    protected override string FormTitle => "Pair Android Device";
    protected override string SubmitButtonText => "Show QR Code";
    protected override double SubmitButtonMinimumWidth => 140;
    protected override string BlazorRoute => "/modal/android-device-pairing";

    public AndroidDevicePairingPage(HybridFormBridgeHolder bridgeHolder)
        : base(bridgeHolder)
    {
        Bridge.Result = false;
        Bridge.Parameters["LifetimeToken"] = lifetime.Token;

#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, false);
        MacOSPage.SetModalSheetWidth(this, 640);
        MacOSPage.SetModalSheetHeight(this, 620);
#elif LINUXGTK
        GtkPage.SetModalSizesToContent(this, false);
        GtkPage.SetModalWidth(this, 640);
        GtkPage.SetModalHeight(this, 620);
#endif
    }

    protected override void OnDisappearing()
    {
        if (!disappeared)
        {
            disappeared = true;
            lifetime.Cancel();
            lifetime.Dispose();
        }
        base.OnDisappearing();
    }
}
