using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Pages.Forms;

public record RegisterDeviceFormResult(AppleDevice Device, bool AddToProfiles);

public class RegisterDevicePage : HybridFormPage<RegisterDeviceFormResult>
{
    protected override string FormTitle => "Register Device";
    protected override string SubmitButtonText => "Register";
    protected override string BlazorRoute => "/modal/register-device";

    public RegisterDevicePage(
        HybridFormBridgeHolder bridgeHolder,
        IAppleConnectService appleService)
        : base(bridgeHolder)
    {
        Bridge.Parameters["AppleService"] = appleService;
    }
}
