using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public static class AndroidSdkLocations
{
    public static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        path = path.Trim();
        if (path == "~" || path.StartsWith("~/", StringComparison.Ordinal))
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.Length > 1 ? path[2..] : "");
        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException("Choose an absolute Android SDK folder path.");
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (Directory.Exists(fullPath))
            fullPath = new DirectoryInfo(fullPath).ResolveLinkTarget(true)?.FullName ?? fullPath;
        return fullPath;
    }

    public static AndroidSdkLocation Inspect(string path, string label = "Custom location", string? activePath = null)
    {
        path = Normalize(path);
        var active = activePath != null && PathComparer.Equals(path, Normalize(activePath));
        try
        {
            if (File.Exists(path))
                return new(path, label, AndroidSdkLocationStatus.Unrelated, active, "Choose a folder, not a file.");
            if (!Directory.Exists(path))
                return new(path, label, AndroidSdkLocationStatus.NotInstalled, active);

            var executable = OperatingSystem.IsWindows() ? "sdkmanager.bat" : "sdkmanager";
            if (File.Exists(Path.Combine(path, "tools", "bin", executable)) ||
                (Directory.Exists(Path.Combine(path, "cmdline-tools")) &&
                 Directory.EnumerateDirectories(Path.Combine(path, "cmdline-tools"))
                    .Any(dir => File.Exists(Path.Combine(dir, "bin", executable)))))
                return new(path, label, AndroidSdkLocationStatus.Installed, active);

            var entries = Directory.EnumerateFileSystemEntries(path).Select(Path.GetFileName).ToArray();
            if (entries.Length == 0)
                return new(path, label, AndroidSdkLocationStatus.NotInstalled, active);
            string[] sdkEntries = ["cmdline-tools", "tools", "platform-tools", "platforms", "build-tools", "emulator",
                "system-images", "sources", "licenses", "extras", "skins", "patcher", "ndk", "ndk-bundle", "cmake",
                ".temp", ".knownPackages", "package.xml"];
            var status = entries.All(e => sdkEntries.Contains(e, StringComparer.Ordinal))
                ? AndroidSdkLocationStatus.Incomplete : AndroidSdkLocationStatus.Unrelated;
            return new(path, label, status, active);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(path, label, AndroidSdkLocationStatus.Inaccessible, active, ex.Message);
        }
    }

    public static string ValidateInstallTarget(string path)
    {
        path = Normalize(path);
        if (PathComparer.Equals(path, Path.GetPathRoot(path)) ||
            PathComparer.Equals(path, Normalize(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))))
            throw new ArgumentException("Choose a dedicated Android SDK folder, not your home folder or a disk root.");
        var location = Inspect(path);
        if (location.Status == AndroidSdkLocationStatus.Installed)
            throw new InvalidOperationException("An Android SDK already exists here. Use this SDK instead of reinstalling it.");
        if (location.Status is AndroidSdkLocationStatus.Unrelated or AndroidSdkLocationStatus.Inaccessible)
            throw new InvalidOperationException(location.Error ?? "This folder contains unrelated files. Choose an empty folder or an incomplete Android SDK.");
        return path;
    }

    public static IReadOnlyList<(string Path, string Label)> GetCommonPaths()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
            return [
                (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk"), "Android Studio / MAUI"),
                (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Android", "android-sdk"), "Visual Studio / Xamarin (legacy)")
            ];
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
            return [
                (Path.Combine(home, "Library", "Android", "sdk"), "Android Studio / MAUI"),
                (Path.Combine(home, "Library", "Developer", "Xamarin", "android-sdk-macosx"), "Xamarin (legacy)")
            ];
        return [(Path.Combine(home, "Android", "Sdk"), "Android Studio / MAUI")];
    }
}
