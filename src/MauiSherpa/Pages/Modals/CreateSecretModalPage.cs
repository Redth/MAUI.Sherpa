using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using MauiSherpa.Pages.Forms;

namespace MauiSherpa.Pages.Modals;

public class CreateSecretModalPage : HybridFormPage<SecretCreateResult>
{
    protected override string FormTitle => "Create Secret";
    protected override string SubmitButtonText => "Create";
    protected override string BlazorRoute => "/modal/edit-secret";

    public CreateSecretModalPage(
        HybridFormBridgeHolder bridgeHolder,
        string initialFolderPath = "/",
        IReadOnlyList<string>? folderPaths = null)
        : base(bridgeHolder)
    {
        ConfigureParameters(initialFolderPath, folderPaths);
    }

    private void ConfigureParameters(string initialFolderPath, IReadOnlyList<string>? folderPaths)
    {
        var normalizedInitialFolder = SecretPath.NormalizeFolderPath(initialFolderPath);
        var normalizedFolders = new[] { "/" }
            .Concat(folderPaths ?? Array.Empty<string>())
            .Append(normalizedInitialFolder)
            .Select(SecretPath.NormalizeFolderPath)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path == "/" ? "" : path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Bridge.Parameters["Mode"] = "Create";
        Bridge.Parameters["FolderPath"] = normalizedInitialFolder;
        Bridge.Parameters["FolderPaths"] = normalizedFolders;
        Bridge.Parameters["Key"] = "";
        Bridge.Parameters["FullKey"] = "";
        Bridge.Parameters["Description"] = "";
        Bridge.Parameters["Type"] = ManagedSecretType.String;
        Bridge.Parameters["FileName"] = "";
        Bridge.Parameters["Metadata"] = new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
