using MauiSherpa.Core.Interfaces;
using MauiSherpa.Pages.Forms;

namespace MauiSherpa.Pages.Modals;

public class PushProjectPage : HybridFormPage<PushProject>
{
    private readonly bool isCreate;

    protected override string FormTitle => isCreate ? "Create Push Project" : "Edit Push Project";
    protected override string SubmitButtonText => isCreate ? "Create Project" : "Save";
    protected override string BlazorRoute => "/modal/push-project";

    public PushProjectPage(HybridFormBridgeHolder bridgeHolder, PushProject project, bool isCreate)
        : base(bridgeHolder)
    {
        this.isCreate = isCreate;
        Bridge.Parameters["Project"] = project;
    }
}
