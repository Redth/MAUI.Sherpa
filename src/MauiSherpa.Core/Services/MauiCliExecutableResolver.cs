using System.Runtime.InteropServices;

namespace MauiSherpa.Core.Services;

public static class MauiCliExecutableResolver
{
    public static string? Resolve(
        string? userProfile = null,
        string? pathEnvironment = null,
        bool? isWindows = null)
    {
        var windows = isWindows ?? RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var executableName = windows ? "maui.exe" : "maui";
        var profile = userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (!string.IsNullOrWhiteSpace(profile))
        {
            var globalToolPath = Path.Combine(profile, ".dotnet", "tools", executableName);
            if (File.Exists(globalToolPath))
                return globalToolPath;
        }

        var path = pathEnvironment ?? Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return null;

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim(), executableName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
