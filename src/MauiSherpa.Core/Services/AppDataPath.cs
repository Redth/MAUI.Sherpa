namespace MauiSherpa.Core.Services;

/// <summary>
/// Provides the correct application data directory path, avoiding TCC-protected
/// locations on Mac Catalyst where Environment.SpecialFolder.ApplicationData
/// incorrectly resolves to ~/Documents/.config/ (a TCC-protected path).
/// </summary>
public static class AppDataPath
{
    private static string? _cachedPath;

    /// <summary>
    /// Returns the application data directory (e.g. ~/Library/Application Support/MauiSherpa/).
    /// Creates the directory if it doesn't exist.
    /// </summary>
    public static string GetAppDataDirectory()
    {
        if (_cachedPath != null)
            return _cachedPath;

        string baseDir;

        if (OperatingSystem.IsMacCatalyst() || OperatingSystem.IsMacOS())
        {
            // Use ~/Library/Application Support/ directly — NOT SpecialFolder.ApplicationData
            // which resolves to ~/Documents/.config/ on Mac Catalyst (TCC-protected)
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            baseDir = Path.Combine(home, "Library", "Application Support");
        }
        else
        {
            baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }

        _cachedPath = Path.Combine(baseDir, "MauiSherpa");
        Directory.CreateDirectory(_cachedPath);
        return _cachedPath;
    }
}
