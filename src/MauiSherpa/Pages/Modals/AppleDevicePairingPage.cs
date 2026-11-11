using MauiSherpa.Pages.Forms;
#if MACOSAPP
using Microsoft.Maui.Platforms.MacOS.Platform;
#endif

namespace MauiSherpa.Pages.Modals;

public sealed class AppleDevicePairingPage : HybridFormPage<bool>
{
    private readonly CancellationTokenSource lifetime = new();
    private bool disappeared;

    protected override string FormTitle => "Pair Apple Device";
    protected override string SubmitButtonText => "Pair via USB";
    protected override double SubmitButtonMinimumWidth => 164;
    protected override string BlazorRoute => "/modal/apple-device-pairing";

    public AppleDevicePairingPage(HybridFormBridgeHolder bridgeHolder, string? identifier = null)
        : base(bridgeHolder)
    {
        Bridge.Result = false;
        Bridge.Parameters["DeviceIdentifier"] = identifier;
        Bridge.Parameters["LifetimeToken"] = lifetime.Token;

#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, false);
        MacOSPage.SetModalSheetWidth(this, 620);
        MacOSPage.SetModalSheetHeight(this, 650);
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
