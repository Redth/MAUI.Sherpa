using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Pages.Forms;

public class CreateBundleIdPage : HybridFormPage<AppleBundleId>
{
    protected override string FormTitle => "Register Bundle ID";
    protected override string SubmitButtonText => "Create";
    protected override string BlazorRoute => "/modal/create-bundle-id";

    public CreateBundleIdPage(
        HybridFormBridgeHolder bridgeHolder,
        IAppleConnectService appleService)
        : base(bridgeHolder)
    {
        Bridge.Parameters["AppleService"] = appleService;
    }
}
