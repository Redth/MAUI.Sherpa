using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using MauiSherpa.Pages.Forms;

namespace MauiSherpa.Pages.Modals;

public record EditSecretResult(
    string OriginalKey,
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
        ManagedSecret secret,
        IReadOnlyList<string>? folderPaths = null)
        : base(bridgeHolder)
    {
        ConfigureParameters(secret, folderPaths);
    }

    private void ConfigureParameters(ManagedSecret secret, IReadOnlyList<string>? folderPaths)
    {
        var normalizedFolders = new[] { "/" }
            .Concat(folderPaths ?? Array.Empty<string>())
            .Select(SecretPath.NormalizeFolderPath)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path == "/" ? "" : path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Bridge.Parameters["FolderPath"] = GetSecretFolder(secret.Key);
        Bridge.Parameters["FolderPaths"] = normalizedFolders;
        Bridge.Parameters["Key"] = GetSecretName(secret.Key);
        Bridge.Parameters["FullKey"] = secret.Key;
        Bridge.Parameters["Description"] = secret.Description ?? "";
        Bridge.Parameters["Type"] = secret.Type;
        Bridge.Parameters["FileName"] = secret.OriginalFileName ?? "";
    }

    private static string GetSecretFolder(string key)
    {
        var lastSeparator = key.LastIndexOf('/');
        return lastSeparator < 0 ? "/" : SecretPath.NormalizeFolderPath(key[..lastSeparator]);
    }

    private static string GetSecretName(string key)
    {
        var lastSeparator = key.LastIndexOf('/');
        return lastSeparator < 0 ? key : key[(lastSeparator + 1)..];
    }
}
