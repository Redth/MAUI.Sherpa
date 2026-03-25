namespace MauiSherpa.Core.Services;

public static class DevFlowVersionSupport
{
    public static readonly Version MinimumSupportedVersion = new(0, 1, 0);

    public static bool TryParseComparableVersion(string? versionText, out Version version)
    {
        version = new Version();

        if (string.IsNullOrWhiteSpace(versionText))
            return false;

        var comparableVersion = versionText.Trim().TrimStart('v', 'V');

        var buildMetadataIndex = comparableVersion.IndexOf('+');
        if (buildMetadataIndex >= 0)
            comparableVersion = comparableVersion[..buildMetadataIndex];

        var prereleaseIndex = comparableVersion.IndexOf('-');
        if (prereleaseIndex >= 0)
            comparableVersion = comparableVersion[..prereleaseIndex];

        return Version.TryParse(comparableVersion, out version);
    }

    public static string FormatVersionLabel(string? versionText)
    {
        if (string.IsNullOrWhiteSpace(versionText))
            return "unknown";

        var displayVersion = versionText.Trim().TrimStart('v', 'V');

        var buildMetadataIndex = displayVersion.IndexOf('+');
        if (buildMetadataIndex >= 0)
            displayVersion = displayVersion[..buildMetadataIndex];

        return $"v{displayVersion}";
    }
}
