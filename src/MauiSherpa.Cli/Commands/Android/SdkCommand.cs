using System.CommandLine;
using MauiSherpa.Cli.Helpers;

namespace MauiSherpa.Cli.Commands.Android;

public static class SdkCommand
{
    public static Command Create()
    {
        var cmd = new Command("sdk", "Manage Android SDK installation and packages.");
        cmd.Add(CreateInfoCommand());
        cmd.Add(CreatePackagesCommand());
        cmd.Add(CreateInstallCommand());
        cmd.Add(CreateUninstallCommand());
        return cmd;
    }

    private static string? FindSdkPath()
    {
        var sdkPath = Environment.GetEnvironmentVariable("ANDROID_HOME")
                      ?? Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");

        if (!string.IsNullOrEmpty(sdkPath) && Directory.Exists(sdkPath))
            return sdkPath;

        var defaults = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Android", "sdk"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Android", "Sdk"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk"),
        };

        return defaults.FirstOrDefault(Directory.Exists);
    }

    private static string? FindSdkManager(string sdkPath)
    {
        var candidates = new[]
        {
            Path.Combine(sdkPath, "cmdline-tools", "latest", "bin", "sdkmanager"),
            Path.Combine(sdkPath, "tools", "bin", "sdkmanager"),
        };

        if (OperatingSystem.IsWindows())
            candidates = candidates.Select(c => c + ".bat").ToArray();

        return candidates.FirstOrDefault(File.Exists);
    }

    private static Command CreateInfoCommand()
    {
        var cmd = new Command("info", "Show Android SDK location and installation status.");
        cmd.SetAction(async (parseResult, ct) =>
        {
            var json = parseResult.GetValue(CliOptions.Json);
            var sdkPath = FindSdkPath();

            var info = new
            {
                installed = sdkPath is not null,
                path = sdkPath,
                androidHome = Environment.GetEnvironmentVariable("ANDROID_HOME"),
                hasAdb = sdkPath is not null && File.Exists(Path.Combine(sdkPath, "platform-tools", OperatingSystem.IsWindows() ? "adb.exe" : "adb")),
                hasEmulator = sdkPath is not null && Directory.Exists(Path.Combine(sdkPath, "emulator")),
                hasSdkManager = sdkPath is not null && FindSdkManager(sdkPath) is not null,
            };

            if (json)
            {
                Output.WriteJson(info);
                return;
            }

            if (!info.installed)
            {
                Output.WriteError("Android SDK not found. Set ANDROID_HOME or install via Android Studio.");
                return;
            }

            Output.WriteSuccess($"Android SDK: {info.path}");
            Output.WriteInfo($"ADB: {(info.hasAdb ? "available" : "missing")}");
            Output.WriteInfo($"Emulator: {(info.hasEmulator ? "available" : "missing")}");
            Output.WriteInfo($"SDK Manager: {(info.hasSdkManager ? "available" : "missing")}");
        });
        return cmd;
    }

    private static Command CreatePackagesCommand()
    {
        var cmd = new Command("packages", "List installed and available Android SDK packages.");
        var installedOpt = new Option<bool>("--installed") { Description = "Show only installed packages" };
        var availableOpt = new Option<bool>("--available") { Description = "Show only available packages" };
        cmd.Add(installedOpt);
        cmd.Add(availableOpt);
        cmd.SetAction(async (parseResult, ct) =>
        {
            var installed = parseResult.GetValue(installedOpt);
            var available = parseResult.GetValue(availableOpt);
            var json = parseResult.GetValue(CliOptions.Json);
            var sdkPath = FindSdkPath();
            if (sdkPath is null)
            {
                Output.WriteError("Android SDK not found.");
                return;
            }

            var sdkManager = FindSdkManager(sdkPath);
            if (sdkManager is null)
            {
                Output.WriteError("SDK Manager not found. Install cmdline-tools first.");
                return;
            }

            var args = "--list";
            var result = await ProcessRunner.RunAsync(sdkManager, args);

            if (result.ExitCode != 0)
            {
                Output.WriteError($"sdkmanager failed: {result.Error}");
                return;
            }

            if (json)
            {
                var packages = ParsePackageList(result.Output, installed, available);
                Output.WriteJson(new { packages });
            }
            else
            {
                Console.WriteLine(result.Output);
            }
        });
        return cmd;
    }

    private static Command CreateInstallCommand()
    {
        var cmd = new Command("install", "Install an Android SDK package.\n\nExamples:\n  maui-sherpa android sdk install 'platform-tools'\n  maui-sherpa android sdk install 'platforms;android-35'");
        var pkgArg = new Argument<string>("package") { Description = "SDK package path (e.g., 'platform-tools', 'platforms;android-35')" };
        cmd.Add(pkgArg);
        cmd.SetAction(async (parseResult, ct) =>
        {
            var package = parseResult.GetValue(pkgArg);
            var sdkPath = FindSdkPath();
            if (sdkPath is null) { Output.WriteError("Android SDK not found."); return; }

            var sdkManager = FindSdkManager(sdkPath);
            if (sdkManager is null) { Output.WriteError("SDK Manager not found."); return; }

            Console.WriteLine($"Installing {package}...");
            var result = await ProcessRunner.RunAsync(sdkManager, $"--install \"{package}\"");
            if (result.ExitCode == 0)
                Output.WriteSuccess($"Installed {package}");
            else
                Output.WriteError($"Failed to install {package}: {result.Error}");
        });
        return cmd;
    }

    private static Command CreateUninstallCommand()
    {
        var cmd = new Command("uninstall", "Uninstall an Android SDK package.");
        var pkgArg = new Argument<string>("package") { Description = "SDK package path to uninstall" };
        cmd.Add(pkgArg);
        cmd.SetAction(async (parseResult, ct) =>
        {
            var package = parseResult.GetValue(pkgArg);
            var sdkPath = FindSdkPath();
            if (sdkPath is null) { Output.WriteError("Android SDK not found."); return; }

            var sdkManager = FindSdkManager(sdkPath);
            if (sdkManager is null) { Output.WriteError("SDK Manager not found."); return; }

            Console.WriteLine($"Uninstalling {package}...");
            var result = await ProcessRunner.RunAsync(sdkManager, $"--uninstall \"{package}\"");
            if (result.ExitCode == 0)
                Output.WriteSuccess($"Uninstalled {package}");
            else
                Output.WriteError($"Failed to uninstall {package}: {result.Error}");
        });
        return cmd;
    }

    private static List<object> ParsePackageList(string output, bool installedOnly, bool availableOnly)
    {
        var packages = new List<object>();
        var section = "";

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Installed packages:")) { section = "installed"; continue; }
            if (trimmed.StartsWith("Available Packages:") || trimmed.StartsWith("Available Updates:")) { section = "available"; continue; }
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("---") || trimmed.StartsWith("Path")) continue;

            if (installedOnly && section != "installed") continue;
            if (availableOnly && section != "available") continue;

            var parts = trimmed.Split('|').Select(p => p.Trim()).ToArray();
            if (parts.Length >= 2)
            {
                packages.Add(new
                {
                    path = parts[0],
                    version = parts.Length > 1 ? parts[1] : null,
                    description = parts.Length > 2 ? parts[2] : null,
                    status = section,
                });
            }
        }

        return packages;
    }
}
