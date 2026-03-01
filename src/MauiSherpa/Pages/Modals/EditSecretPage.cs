using MauiSherpa.Core.Interfaces;
using MauiSherpa.Pages.Forms;

namespace MauiSherpa.Pages.Modals;

public record EditSecretResult(
    string Key,
    string? Description,
    byte[]? Value,
    byte[]? FileBytes,
    ManagedSecretType Type);

public class EditSecretPage : HybridFormPage<EditSecretResult>
{
    protected override string FormTitle => "Edit Secret";
    protected override string SubmitButtonText => "Update";
    protected override string BlazorRoute => "/modal/edit-secret";

    public EditSecretPage(
        HybridFormBridgeHolder bridgeHolder,
        ManagedSecret secret)
        : base(bridgeHolder)
    {
        ConfigureParameters(secret);
    }

    private void ConfigureParameters(ManagedSecret secret)
    {
        Bridge.Parameters["Key"] = secret.Key;
        Bridge.Parameters["Description"] = secret.Description ?? "";
        Bridge.Parameters["Type"] = secret.Type;
        Bridge.Parameters["FileName"] = secret.OriginalFileName ?? "";
    }
}
