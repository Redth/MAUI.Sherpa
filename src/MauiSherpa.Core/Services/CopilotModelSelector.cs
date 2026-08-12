using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public static class CopilotModelSelector
{
    public const string AutomaticModelId = "auto";

    public static string SelectPreferred(IEnumerable<CopilotModelOption> models)
    {
        return models
            .Select(model => new
            {
                Model = model,
                Version = GetGptVersion(model.Id),
                VariantPriority = GetVariantPriority(model.Id)
            })
            .Where(candidate => candidate.Version is not null)
            .OrderByDescending(candidate => candidate.Version)
            .ThenByDescending(candidate => candidate.VariantPriority)
            .Select(candidate => candidate.Model.Id)
            .FirstOrDefault() ?? AutomaticModelId;
    }

    private static Version? GetGptVersion(string modelId)
    {
        var markerIndex = modelId.IndexOf("gpt-", StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0 || (markerIndex > 0 && modelId[markerIndex - 1] != '/'))
            return null;

        var versionStart = markerIndex + "gpt-".Length;
        var versionEnd = versionStart;
        while (versionEnd < modelId.Length &&
               (char.IsDigit(modelId[versionEnd]) || modelId[versionEnd] == '.'))
        {
            versionEnd++;
        }

        var versionText = modelId[versionStart..versionEnd].TrimEnd('.');
        if (!versionText.Contains('.'))
            versionText += ".0";

        return Version.TryParse(versionText, out var version) ? version : null;
    }

    private static int GetVariantPriority(string modelId)
    {
        if (modelId.Contains("-sol", StringComparison.OrdinalIgnoreCase))
            return 2;
        if (modelId.Contains("-terra", StringComparison.OrdinalIgnoreCase))
            return 1;
        return 0;
    }
}
